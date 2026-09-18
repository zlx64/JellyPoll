<script lang="ts">
  import Poster from './Poster.svelte';
  import type { StandingEntry } from '../lib/types';

  let { standings, closed = false }: { standings: StandingEntry[]; closed?: boolean } = $props();

  const medalFor = (rank: number): string => (rank === 1 ? '🥇' : rank === 2 ? '🥈' : rank === 3 ? '🥉' : '');
</script>

<div class="standings">
  {#each standings as entry (entry.suggestionId)}
    <div class="row card" class:missing={entry.itemMissing} class:gold={entry.rank === 1 && closed}>
      <span class="medal" class:gold-text={entry.rank === 1}>{medalFor(entry.rank) || entry.rank}</span>
      <Poster itemId={entry.itemId} name={entry.name} size={40} />
      <div class="info">
        <div class="name">{entry.name} <span class="dim">{entry.year ?? ''}</span></div>
        <div class="dim">
          {entry.points} pts · {entry.firstPlaceCount} first{entry.firstPlaceCount === 1 ? '' : 's'} · {entry.voterCount} voter{entry.voterCount === 1 ? '' : 's'}
          {#if entry.itemMissing}· no longer in library{/if}
          {#if entry.rank === 1 && closed}<strong> · Watch next</strong>{/if}
        </div>
      </div>
    </div>
  {:else}
    <p class="dim">No suggestions yet.</p>
  {/each}
</div>

<style>
  .standings { display: flex; flex-direction: column; gap: 0.5rem; }
  .row { display: flex; align-items: center; gap: 0.7rem; padding: 0.5rem; }
  .row.missing { opacity: 0.45; }
  .row.gold { border: 1px solid var(--jp-gold); }
  .medal {
    width: 2rem;
    text-align: center;
    font-weight: 700;
    color: var(--jp-text-dim);
    flex-shrink: 0;
  }
  .gold-text { color: var(--jp-gold); }
  .info { min-width: 0; }
  .name { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .info .dim { font-size: 0.8rem; }
</style>
