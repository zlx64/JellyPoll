using Jellyfin.Plugin.JellyPoll.Voting;

namespace Jellyfin.Plugin.JellyPoll.Voting;

/// <summary>
/// Pure Borda count scoring with the documented tie-break chain (doc 03).
/// Stateless and deterministic: same input always yields the same output.
/// </summary>
public static class BordaCalculator
{
    /// <summary>
    /// Computes standings for a poll snapshot.
    /// Scoring: with N eligible suggestions, position p on a ballot earns N - p points;
    /// unranked or missing suggestions earn 0 (doc 03 §2).
    /// Ties: 1) more first-place ranks, 2) head-to-head within the tied group,
    /// 3) earlier suggestion, 4) itemId ascending (doc 03 §3).
    /// </summary>
    public static IReadOnlyList<StandingEntry> Compute(VotingInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var eligible = input.Suggestions.Where(s => !s.ItemMissing).ToList();
        var n = eligible.Count;
        var eligibleIds = eligible.Select(s => s.Id).ToHashSet();
        var byId = eligible.ToDictionary(s => s.Id);

        var points = eligible.ToDictionary(s => s.Id, _ => 0L);
        var firsts = eligible.ToDictionary(s => s.Id, _ => 0);
        var voters = eligible.ToDictionary(s => s.Id, _ => 0);

        // Per-ballot derived positions (deduplicated, foreign ids ignored, re-derived from list order).
        var ballotPositions = new List<Dictionary<string, int>>(input.Ballots.Count);

        foreach (var ballot in input.Ballots)
        {
            var positions = new Dictionary<string, int>();
            var p = 0;
            foreach (var sid in ballot.OrderedSuggestionIds)
            {
                if (!eligibleIds.Contains(sid) || positions.ContainsKey(sid))
                {
                    continue;
                }

                positions[sid] = ++p;
            }

            foreach (var (sid, pos) in positions)
            {
                points[sid] += n - pos;
                voters[sid] += 1;
                if (pos == 1)
                {
                    firsts[sid] += 1;
                }
            }

            ballotPositions.Add(positions);
        }

        // Primary sort: points desc, first-place count desc.
        // Groups tied on BOTH are resolved pairwise, then by suggestedAt, then itemId.
        var ordered = eligible
            .OrderByDescending(s => points[s.Id])
            .ThenByDescending(s => firsts[s.Id])
            .ThenBy(s => s.SuggestedAt)
            .ThenBy(s => s.ItemId, StringComparer.Ordinal)
            .ToList();

        // Resolve (points, firsts) tie groups with head-to-head where decidable.
        var result = new List<SuggestionRecord>(ordered.Count);
        var i = 0;
        while (i < ordered.Count)
        {
            var group = new List<SuggestionRecord> { ordered[i] };
            var j = i + 1;
            while (j < ordered.Count
                   && points[ordered[j].Id] == points[ordered[i].Id]
                   && firsts[ordered[j].Id] == firsts[ordered[i].Id])
            {
                group.Add(ordered[j]);
                j++;
            }

            result.AddRange(group.Count == 1 ? group : OrderByPairwise(group, ballotPositions));
            i = j;
        }

        // Assign dense ranks.
        var entries = new List<StandingEntry>(result.Count);
        var rank = 1;
        foreach (var s in result)
        {
            entries.Add(new StandingEntry(rank++, s.Id, points[s.Id], firsts[s.Id], voters[s.Id], ItemMissing: false));
        }

        // Missing suggestions appended after all eligible rows, by suggestedAt (doc 03 §6).
        foreach (var s in input.Suggestions
                     .Where(s => s.ItemMissing)
                     .OrderBy(s => s.SuggestedAt))
        {
            entries.Add(new StandingEntry(rank++, s.Id, 0, 0, 0, ItemMissing: true));
        }

        return entries;
    }

    /// <summary>
    /// Within a tie group, a candidate that is pairwise preferred (ranked higher) by more ballots
    /// than EVERY other member moves to the front; otherwise the base order stands (doc 03 §3 step 3).
    /// A ballot only contributes to a pair when it ranks both members.
    /// </summary>
    private static IReadOnlyList<SuggestionRecord> OrderByPairwise(
        IReadOnlyList<SuggestionRecord> group,
        IReadOnlyList<Dictionary<string, int>> ballotPositions)
    {
        var winnerIndex = -1;
        for (var c = 0; c < group.Count; c++)
        {
            var beatsAll = true;
            for (var m = 0; m < group.Count && beatsAll; m++)
            {
                if (m == c)
                {
                    continue;
                }

                var cPreferred = 0;
                var mPreferred = 0;
                foreach (var positions in ballotPositions)
                {
                    var hasC = positions.TryGetValue(group[c].Id, out var pc);
                    var hasM = positions.TryGetValue(group[m].Id, out var pm);
                    if (hasC && hasM)
                    {
                        if (pc < pm)
                        {
                            cPreferred++;
                        }
                        else if (pm < pc)
                        {
                            mPreferred++;
                        }
                    }
                }

                if (cPreferred <= mPreferred)
                {
                    beatsAll = false;
                }
            }

            if (beatsAll)
            {
                winnerIndex = c;
                break;
            }
        }

        if (winnerIndex <= 0)
        {
            return group; // no unique pairwise winner (or already first) — keep base order
        }

        var withWinnerFirst = new List<SuggestionRecord>(group.Count) { group[winnerIndex] };
        for (var k = 0; k < group.Count; k++)
        {
            if (k != winnerIndex)
            {
                withWinnerFirst.Add(group[k]);
            }
        }

        return withWinnerFirst;
    }
}
