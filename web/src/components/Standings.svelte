<script lang="ts">
  import type { StandingEntry } from '../lib/types';
  import { t } from '../lib/i18n.svelte';
  import { detailUrl } from '../lib/api';
  import Poster from './Poster.svelte';
  import Icon from './Icon.svelte';

  let {
    standings,
    closed = false
  }: {
    standings: StandingEntry[];
    closed?: boolean;
  } = $props();
</script>

{#if standings.length === 0}
  <p class="empty dim"><Icon name="group" size={16} /> {t('standings.empty')}</p>
{:else}
  <ol class="list">
    {#each standings as entry, i (entry.SuggestionId)}
      <li class="row">
        <span class="rankcell">
          {#if i < 3}
            <Icon name="trophy" size={i === 0 ? 20 : 15} class={i === 0 ? 'gold' : i === 1 ? 'silver' : 'bronze'} />
          {:else}
            <span class="num">{i + 1}</span>
          {/if}
        </span>
        <Poster itemId={entry.ItemId} name={entry.Name} size={32} missing={entry.ItemMissing} />
        <div class="info">
          {#if entry.ItemMissing}
            <span class="name dim">{entry.Name}</span>
          {:else}
            <a class="name titlelink" href={detailUrl(entry.ItemId)} target="_blank" rel="noreferrer">{entry.Name}</a>
          {/if}
          <span class="sub dim">
            <Icon name="group" size={11} /> {t('standings.votes', { count: entry.VoterCount })}
            {#if entry.FirstPlaceCount > 0} · {t('standings.firstPlaces', { count: entry.FirstPlaceCount })}{/if}
          </span>
        </div>
        <span class="pts">{t('standings.points', { count: entry.Points })}</span>
      </li>
    {/each}
  </ol>
{/if}

<style>
  .empty { display: flex; align-items: center; gap: 0.4rem; font-size: 0.85rem; margin: 0.2rem 0; }
  .list { list-style: none; margin: 0.4rem 0 0; padding: 0; display: flex; flex-direction: column; gap: 0.3rem; }
  .row {
    display: flex; align-items: center; gap: 0.5rem;
    padding: 0.3rem; border-radius: var(--jp-radius-sm);
    background: var(--jp-surface-2); border: 1px solid transparent;
    transition: border-color 0.12s ease;
  }
  .row:hover { border-color: var(--jp-border); }
  .rankcell { width: 1.5rem; display: inline-flex; justify-content: center; flex-shrink: 0; }
  .rankcell :global(.gold) { color: var(--jp-gold); }
  .rankcell :global(.silver) { color: var(--jp-silver); }
  .rankcell :global(.bronze) { color: var(--jp-bronze); }
  .num { font-size: 0.8rem; font-weight: 700; color: var(--jp-text-dim); }
  .info { flex: 1; min-width: 0; display: flex; flex-direction: column; gap: 0.08rem; }
  .name {
    font-size: 0.86rem; font-weight: 500;
    overflow: hidden; text-overflow: ellipsis; white-space: nowrap;
  }
  .titlelink { color: inherit; text-decoration: none; }
  .titlelink:hover { color: var(--jp-accent); text-decoration: underline; }
  .sub { font-size: 0.72rem; display: inline-flex; align-items: center; gap: 0.25rem; }
  .pts {
    font-size: 0.75rem; font-weight: 700; color: var(--jp-text-dim);
    background: var(--jp-surface-3); padding: 0.15rem 0.5rem; border-radius: 999px;
    white-space: nowrap;
  }
</style>
