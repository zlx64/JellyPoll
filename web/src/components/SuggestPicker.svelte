<script lang="ts">
  import { jellyfin, jellypoll } from '../lib/api';
  import type { JellyfinSearchItem, PollMeta } from '../lib/types';
  import Poster from './Poster.svelte';

  let { poll, onAdded }: { poll: PollMeta; onAdded: (msg: string, isError: boolean) => void } = $props();

  let searchTerm = $state('');
  let results = $state<JellyfinSearchItem[]>([]);
  let searching = $state(false);
  let busyItemId = $state<string | null>(null);

  const includeTypes = [
    'Movie',
    ...(poll.AllowEpisodes ? ['Episode'] : []),
    ...(poll.AllowSeries ? ['Series'] : [])
  ].join(',');

  let debounceTimer: ReturnType<typeof setTimeout> | undefined;

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

  async function suggest(item: JellyfinSearchItem) {
    busyItemId = item.Id;
    try {
      const res = await jellypoll.addSuggestion(poll.Id, item.Id);
      onAdded(`"${item.Name}" added to the poll.`, false, res.Suggestion.Id);
      results = results.filter((r) => r.Id !== item.Id);
    } catch (e) {
      onAdded(e instanceof Error ? e.message : String(e), true);
    } finally {
      busyItemId = null;
    }
  }
</script>

<div class="picker">
  <h3>Suggest a title</h3>
  <input
    type="search"
    placeholder="Search the library…"
    bind:value={searchTerm}
    oninput={onInput}
  />
  {#if searching}
    <p class="dim">Searching…</p>
  {:else if results.length > 0}
    <div class="grid">
      {#each results as item (item.Id)}
        <button class="item" onclick={() => suggest(item)} disabled={busyItemId === item.Id}>
          <Poster itemId={item.Id} name={item.Name} size={96} />
          <div class="label">
            <div class="name">{item.Name}</div>
            <div class="dim">{item.Type}{item.ProductionYear ? ' · ' + item.ProductionYear : ''}</div>
          </div>
        </button>
      {/each}
    </div>
  {:else if searchTerm.trim()}
    <p class="dim">No results.</p>
  {:else}
    <p class="dim">Search your library and tap a title to add it to this poll.</p>
  {/if}
</div>

<style>
  .picker { display: flex; flex-direction: column; gap: 0.7rem; }
  .grid {
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(120px, 1fr));
    gap: 0.7rem;
  }
  .item {
    display: flex;
    flex-direction: column;
    gap: 0.3rem;
    background: var(--jp-surface);
    border: none;
    padding: 0;
    text-align: left;
  }
  .label { padding: 0.2rem 0.3rem; }
  .Name { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; font-size: 0.85rem; }
  .label .dim { font-size: 0.75rem; }
</style>
