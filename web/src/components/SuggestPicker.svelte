<script lang="ts">
  import { jellyfin, jellypoll } from '../lib/api';
  import { t, tType, errorMessage } from '../lib/i18n.svelte';
  import type { JellyfinSearchItem, PollMeta } from '../lib/types';
  import Poster from './Poster.svelte';
  import Icon from './Icon.svelte';

  let {
    poll,
    existingItemIds,
    onAdded
  }: {
    poll: PollMeta;
    existingItemIds: Set<string>;
    onAdded: (msg: string, isError: boolean, suggestionIds?: string[]) => void;
  } = $props();

  let searchTerm = $state('');
  let rawResults = $state<JellyfinSearchItem[]>([]);
  let searching = $state(false);
  let busyItemId = $state<string | null>(null);

  // Hide movies/series/episodes that are already in the poll (matched by item id,
  // normalized the same way as existingItemIds).
  const results = $derived(
    rawResults.filter((r) => !existingItemIds.has(r.Id.replace(/-/g, '').toLowerCase()))
  );

  // Collections (BoxSet) are intentionally excluded from the movie search —
  // they are browsed only through the "Browse collections" menu below.
  const includeTypes = [
    'Movie',
    ...(poll.AllowEpisodes ? ['Episode'] : []),
    ...(poll.AllowSeries ? ['Series'] : [])
  ].join(',');

  let debounceTimer: ReturnType<typeof setTimeout> | undefined;

  // Collections are browsed through the plugin's own endpoint (below),
  // not through the /Items movie search.
  let showCollections = $state(false);
  let collections = $state<{ Id: string; Name: string; Year?: number | null; MovieCount: number }[]>([]);
  let loadingCollections = $state(false);

  function toggleCollections() {
    showCollections = !showCollections;
    if (showCollections) {
      void loadCollections();
    }
  }

  async function loadCollections() {
    loadingCollections = true;
    try {
      const res = await jellypoll.listCollections(searchTerm.trim() || undefined);
      collections = res.Items;
    } catch (e) {
      onAdded(errorMessage(e), true);
    } finally {
      loadingCollections = false;
    }
  }

  function onInput() {
    clearTimeout(debounceTimer);
    if (!searchTerm.trim()) {
      rawResults = [];
      return;
    }
    debounceTimer = setTimeout(search, 300);
  }

  async function search() {
    searching = true;
    try {
      const res = await jellyfin.searchItems(searchTerm.trim(), includeTypes, 10);
      rawResults = res.Items;
    } finally {
      searching = false;
    }
  }

  type Suggestable = { Id: string; Name: string };

  async function suggest(item: Suggestable) {
    busyItemId = item.Id;
    try {
      const res = await jellypoll.addSuggestion(poll.Id, item.Id);
      if (res.Suggestion) {
        onAdded(t('picker.added', { name: item.Name }), false, [res.Suggestion.Id]);
      } else {
        // Collection (BoxSet): expanded into per-movie suggestions.
        const added = res.Suggestions ?? [];
        const parts = [t('picker.collectionAdded', { name: item.Name, count: added.length })];
        const skippedExisting = res.SkippedExisting ?? 0;
        const skippedOverLimit = res.SkippedOverLimit ?? 0;
        if (skippedExisting > 0) parts.push(t('picker.alreadyInPoll', { count: skippedExisting }));
        if (skippedOverLimit > 0) parts.push(t('picker.skippedOverLimit', { count: skippedOverLimit }));
        onAdded(parts.join(' · '), false, added.map((s) => s.Id));
      }

      // Drop the card from results — it's now in the poll and gets filtered out.
      rawResults = rawResults.filter((r) => r.Id !== item.Id);
    } catch (e) {
      if ((e as { code?: string }).code === 'duplicate_suggestion') {
        // Already in the poll — show a friendly notice, not an error.
        onAdded(errorMessage(e), false);
      } else {
        onAdded(errorMessage(e), true);
      }
    } finally {
      busyItemId = null;
    }
  }
</script>

<div class="card picker">
  <h3 class="sechead"><Icon name="movie" size={18} /> {t('picker.title')}</h3>
  <div class="searchwrap">
    <Icon name="search" size={17} class="searchicon" />
    <input
      type="search"
      placeholder={t('picker.searchPlaceholder')}
      bind:value={searchTerm}
      oninput={onInput}
    />
    {#if searching}<span class="spinner"></span>{/if}
  </div>

  <button class="link" type="button" onclick={toggleCollections}>
    <Icon name="stacks" size={16} />
    {showCollections ? t('picker.collections') : t('picker.browseCollections')}
    <Icon name={showCollections ? 'keyboard_arrow_up' : 'keyboard_arrow_down'} size={16} />
  </button>

  {#if showCollections}
    {#if loadingCollections}
      <p class="dim small"><Icon name="search" size={13} /> {t('picker.loadingCollections')}</p>
    {:else if collections.length === 0}
      <p class="dim small">{t('picker.noCollections')}</p>
    {:else}
      <div class="list">
        {#each collections as item (item.Id)}
          <button class="row" onclick={() => suggest(item)} disabled={busyItemId === item.Id}>
            <Poster itemId={item.Id} name={item.Name} size={32} />
            <div class="info">
              <span class="name">{item.Name}</span>
              <span class="sub dim">
                <Icon name="stacks" size={11} />
                {t('picker.collectionMovies', { count: item.MovieCount })}
              </span>
            </div>
            <span class="addicon"><Icon name="add" size={18} /></span>
            {#if busyItemId === item.Id}<span class="busy"><span class="busspin"></span></span>{/if}
          </button>
        {/each}
      </div>
    {/if}
  {/if}

  {#if searching}
    <p class="dim small"><Icon name="search" size={13} /> {t('picker.searching')}</p>
  {:else if results.length > 0}
    <div class="list">
      {#each results as item (item.Id)}
        <button class="row" onclick={() => suggest(item)} disabled={busyItemId === item.Id}>
          <Poster itemId={item.Id} name={item.Name} size={32} />
          <div class="info">
            <span class="name">{item.Name}</span>
              <span class="sub dim">
                <Icon name={item.Type === 'BoxSet' ? 'stacks' : 'movie'} size={11} />
                {tType(item.Type)}{item.ProductionYear ? ' · ' + item.ProductionYear : ''}
              </span>
          </div>
          <span class="addicon"><Icon name="add" size={18} /></span>
          {#if busyItemId === item.Id}<span class="busy"><span class="busspin"></span></span>{/if}
        </button>
      {/each}
    </div>
  {:else if searchTerm.trim()}
    <p class="dim small"><Icon name="search" size={13} /> {t('picker.noResults')}</p>
  {:else}
    <p class="dim small">{t('picker.hint')}</p>
  {/if}
</div>

<style>
  .small { display: flex; align-items: center; gap: 0.3rem; font-size: 0.8rem; margin: 0.3rem 0 0; }
  .searchwrap { position: relative; margin-top: 0.55rem; }
  .searchwrap :global(.searchicon) {
    position: absolute; left: 0.6rem; top: 50%; transform: translateY(-50%);
    color: var(--jp-text-dim); pointer-events: none;
  }
  .searchwrap input { padding-left: 2.1rem; }
  .spinner {
    position: absolute; right: 0.6rem; top: 50%;
    width: 14px; height: 14px; margin-top: -7px;
    border: 2px solid var(--jp-text-dim); border-top-color: transparent;
    border-radius: 50%; animation: spin 0.7s linear infinite;
  }
  @keyframes spin { to { transform: rotate(360deg); } }
  .link {
    align-self: flex-start;
    background: none; border: none;
    color: var(--jp-accent);
    padding: 0.25rem 0;
    font-size: 0.85rem; font-weight: 500;
    display: inline-flex; align-items: center; gap: 0.25rem;
  }
  .link:hover { background: transparent; text-decoration: underline; }
  .list { display: flex; flex-direction: column; gap: 0.3rem; margin-top: 0.6rem; }
  .row {
    display: flex; align-items: center; gap: 0.5rem;
    width: 100%;
    padding: 0.3rem 0.5rem;
    background: var(--jp-surface-2);
    border: 1px solid transparent;
    border-radius: var(--jp-radius-sm);
    text-align: left;
    position: relative;
    transition: border-color 0.12s ease, background 0.12s ease;
  }
  .row:hover:not(:disabled) { border-color: var(--jp-border); background: var(--jp-surface-3); }
  .info { flex: 1; min-width: 0; display: flex; flex-direction: column; gap: 0.08rem; }
  .name {
    font-size: 0.86rem; font-weight: 500;
    overflow: hidden; text-overflow: ellipsis; white-space: nowrap;
  }
  .sub {
    font-size: 0.72rem;
    display: inline-flex; align-items: center; gap: 0.25rem;
    min-width: 0;
  }
  .addicon { color: var(--jp-text-dim); display: inline-flex; flex-shrink: 0; transition: color 0.12s ease; }
  .row:hover:not(:disabled) .addicon { color: var(--jp-accent); }
  .busy {
    position: absolute; inset: 0; display: grid; place-items: center;
    background: rgba(0, 0, 0, 0.45);
    border-radius: var(--jp-radius-sm);
  }
  .busspin {
    width: 16px; height: 16px;
    border: 2px solid #fff; border-top-color: transparent;
    border-radius: 50%; animation: spin 0.7s linear infinite;
  }
</style>
