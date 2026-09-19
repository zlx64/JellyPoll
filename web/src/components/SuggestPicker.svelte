<script lang="ts">
  import { jellyfin, jellypoll } from '../lib/api';
  import type { JellyfinSearchItem, PollMeta } from '../lib/types';
  import Poster from './Poster.svelte';
  import Icon from './Icon.svelte';

  let { poll, onAdded }: { poll: PollMeta; onAdded: (msg: string, isError: boolean, suggestionIds?: string[]) => void } = $props();

  let searchTerm = $state('');
  let results = $state<JellyfinSearchItem[]>([]);
  let searching = $state(false);
  let busyItemId = $state<string | null>(null);

  const includeTypes = [
    'Movie',
    'BoxSet',
    ...(poll.AllowEpisodes ? ['Episode'] : []),
    ...(poll.AllowSeries ? ['Series'] : [])
  ].join(',');

  let debounceTimer: ReturnType<typeof setTimeout> | undefined;

  // Jellyfin 12's /Items searchTerm and /Search/Hints both exclude BoxSets,
  // so collections are browsed through the plugin's own endpoint.
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
      onAdded(e instanceof Error ? e.message : String(e), true);
    } finally {
      loadingCollections = false;
    }
  }

  function onInput() {
    clearTimeout(debounceTimer);
    if (!searchTerm.trim()) {
      results = [];
      return;
    }
    debounceTimer = setTimeout(search, 300);
  }

  async function search() {
    searching = true;
    try {
      const res = await jellyfin.searchItems(searchTerm.trim(), includeTypes);
      results = res.Items;
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
        onAdded(`"${item.Name}" added to the poll.`, false, [res.Suggestion.Id]);
      } else {
        // Collection (BoxSet): expanded into per-movie suggestions.
        const added = res.Suggestions ?? [];
        const parts = [`"${item.Name}" — ${added.length} movie${added.length === 1 ? '' : 's'} added`];
        if ((res.SkippedExisting ?? 0) > 0) parts.push(`${res.SkippedExisting} already in poll`);
        if ((res.SkippedOverLimit ?? 0) > 0) parts.push(`${res.SkippedOverLimit} skipped (limit reached)`);
        onAdded(parts.join(' · '), false, added.map((s) => s.Id));
      }

      // Drop the card from results — resuggesting it yields "already in poll".
      results = results.filter((r) => r.Id !== item.Id);
    } catch (e) {
      onAdded(e instanceof Error ? e.message : String(e), true);
    } finally {
      busyItemId = null;
    }
  }
</script>

<div class="card picker">
  <h3 class="sechead"><Icon name="movie" size={18} /> Suggest a title</h3>
  <div class="searchwrap">
    <Icon name="search" size={17} class="searchicon" />
    <input
      type="search"
      placeholder="Search the library…"
      bind:value={searchTerm}
      oninput={onInput}
    />
    {#if searching}<span class="spinner"></span>{/if}
  </div>

  <button class="link" type="button" onclick={toggleCollections}>
    <Icon name="stacks" size={16} />
    {showCollections ? 'Collections' : 'Browse collections'}
    <Icon name={showCollections ? 'keyboard_arrow_up' : 'keyboard_arrow_down'} size={16} />
  </button>

  {#if showCollections}
    {#if loadingCollections}
      <p class="dim small"><Icon name="search" size={13} /> Loading collections…</p>
    {:else if collections.length === 0}
      <p class="dim small">No collections in your libraries.</p>
    {:else}
      <div class="grid">
        {#each collections as item (item.Id)}
          <button class="item" onclick={() => suggest(item)} disabled={busyItemId === item.Id}>
            <Poster itemId={item.Id} name={item.Name} size={72} />
            <div class="label">
              <div class="name">{item.Name}</div>
              <div class="dim"><Icon name="stacks" size={11} /> Collection · {item.MovieCount} movie{item.MovieCount === 1 ? '' : 's'}</div>
            </div>
            {#if busyItemId === item.Id}<span class="busy">Adding…</span>{/if}
          </button>
        {/each}
      </div>
    {/if}
  {/if}

  {#if searching}
    <p class="dim small"><Icon name="search" size={13} /> Searching…</p>
  {:else if results.length > 0}
    <div class="grid">
      {#each results as item (item.Id)}
        <button class="item" onclick={() => suggest(item)} disabled={busyItemId === item.Id}>
          <Poster itemId={item.Id} name={item.Name} size={72} />
          <div class="label">
            <div class="name">{item.Name}</div>
            <div class="dim">
              <Icon name={item.Type === 'BoxSet' ? 'stacks' : 'movie'} size={11} />
              {item.Type === 'BoxSet' ? 'Collection' : item.Type}{item.ProductionYear ? ' · ' + item.ProductionYear : ''}
            </div>
          </div>
          {#if busyItemId === item.Id}<span class="busy">Adding…</span>{/if}
        </button>
      {/each}
    </div>
  {:else if searchTerm.trim()}
    <p class="dim small"><Icon name="search" size={13} /> No results.</p>
  {:else}
    <p class="dim small">Search your library and tap a title to add it to this poll.</p>
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
  .grid {
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(96px, 1fr));
    gap: 0.5rem; margin-top: 0.6rem;
  }
  .item {
    display: flex;
    flex-direction: column;
    gap: 0.3rem;
    background: var(--jp-surface-2);
    border: 1px solid var(--jp-border);
    padding: 0.4rem;
    text-align: left;
    position: relative;
    transition: border-color 0.12s ease, transform 0.12s ease;
  }
  .item:hover:not(:disabled) { border-color: var(--jp-accent); transform: translateY(-1px); }
  .label { padding: 0 0.15rem; }
  .name {
    overflow: hidden; text-overflow: ellipsis; white-space: nowrap; font-size: 0.8rem;
    display: -webkit-box; -webkit-line-clamp: 2; -webkit-box-orient: vertical;
  }
  .label .dim {
    font-size: 0.72rem; margin-top: 0.15rem;
    display: flex; align-items: center; gap: 0.2rem;
  }
  .busy {
    position: absolute; inset: 0; display: grid; place-items: center;
    background: rgba(0, 0, 0, 0.45); color: #fff; font-size: 0.75rem;
    border-radius: var(--jp-radius-sm);
  }
</style>
