<script lang="ts">
  import Poster from './Poster.svelte';
  import type { StandingEntry } from '../lib/types';

  let { standings, closed = false }: { standings: StandingEntry[]; closed?: boolean } = $props();

  const medalFor = (rank: number): string => (rank === 1 ? '🥇' : rank === 2 ? '🥈' : rank === 3 ? '🥉' : '');
</script>

<div class="standings">
  {#each standings as entry (entry.SuggestionId)}
    <div class="row card" class:missing={entry.ItemMissing} class:gold={entry.Rank === 1 && closed}>
      <span class="medal" class:gold-text={entry.Rank === 1}>{medalFor(entry.Rank) || entry.Rank}</span>
      <Poster itemId={entry.ItemId} name={entry.Name} size={40} />
      <div class="info">
        <div class="name">{entry.Name} <span class="dim">{entry.Year ?? ''}</span></div>
        <div class="dim">
          {entry.Points} pts · {entry.FirstPlaceCount} first{entry.FirstPlaceCount === 1 ? '' : 's'} · {entry.VoterCount} voter{entry.VoterCount === 1 ? '' : 's'}
          {#if entry.ItemMissing}· no longer in library{/if}
          {#if entry.Rank === 1 && closed}<strong> · Watch next</strong>{/if}
        </div>
      </div>
    </div>
  {:else}
    <p class="dim">No suggestions yet.</p>
  {/each}
</div>

<style>
  .Standings { display: flex; flex-direction: column; gap: 0.5rem; }
  .row { display: flex; align-items: center; gap: 0.7rem; padding: 0.5rem; }
  .row.missing { opacity: 0.45; }
  .row.Gold { border: 1px solid var(--jp-gold); }
  .medal {
    width: 2rem;
    text-align: center;
    font-weight: 700;
    color: var(--jp-text-dim);
    flex-shrink: 0;
  }
  .Gold-text { color: var(--jp-gold); }
  .info { min-width: 0; }
  .Name { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .info .dim { font-size: 0.8rem; }
</style>
