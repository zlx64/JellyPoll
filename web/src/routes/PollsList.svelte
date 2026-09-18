<script lang="ts">
  import { onMount } from 'svelte';
  import { jellypoll, ApiError } from '../lib/api';
  import type { PollSummary } from '../lib/types';

  let polls = $state<PollSummary[] | null>(null);
  let isAdmin = $state(false);
  let error = $state('');
  let showNewDialog = $state(false);
  let newTitle = $state('');
  let newEpisodes = $state(true);
  let newSeries = $state(true);
  let creating = $state(false);

  const openPolls = $derived(polls?.filter((p) => p.Status === 'open') ?? []);
  const closedPolls = $derived(polls?.filter((p) => p.Status === 'closed') ?? []);

  async function load() {
    try {
      const res = await jellypoll.listPolls();
      polls = res.Polls;
      isAdmin = res.IsAdmin;
      error = '';
    } catch (e) {
      error = e instanceof Error ? e.message : String(e);
    }
  }

  async function deleteClosedPolls() {
    if (!confirm(`Delete all closed polls (${closedPolls.length})? This cannot be undone.`)) return;
    try {
      await jellypoll.deleteAllClosedPolls();
      await load();
    } catch (e) {
      error = e instanceof Error ? e.message : String(e);
    }
  }

  onMount(() => {
    load();
    const timer = setInterval(() => {
      if (!document.hidden) load();
    }, 15000);
    return () => clearInterval(timer);
  });

  async function createPoll() {
    if (!newTitle.trim()) return;
    creating = true;
    try {
      const detail = await jellypoll.createPoll(newTitle.trim(), newEpisodes, newSeries);
      location.hash = '#/poll/' + detail.Poll.Id;
    } catch (e) {
      error = e instanceof ApiError || e instanceof Error ? e.message : String(e);
      creating = false;
    }
  }

  const fmtDate = (iso: string) => new Date(iso).toLocaleDateString();
</script>

<div class="page">
  <div class="header">
    <h1>🎬 Movie Polls</h1>
    <button class="primary" onclick={() => (showNewDialog = true)}>＋ New poll</button>
  </div>

  {#if error}<div class="error-box">{error}</div>{/if}

  {#if showNewDialog}
    <div class="overlay" role="dialog" onclick={(e) => e.target === e.currentTarget && (showNewDialog = false)}>
      <div class="card dialog">
        <h3>New poll</h3>
        <input type="text" placeholder="Poll title (e.g. Friday Night)" bind:value={newTitle} maxlength="100" />
        <label><input type="checkbox" checked disabled /> Allow movies (always)</label>
        <label><input type="checkbox" bind:checked={newEpisodes} /> Allow TV episodes</label>
        <label><input type="checkbox" bind:checked={newSeries} /> Allow TV series</label>
        <div class="actions">
          <button onclick={() => (showNewDialog = false)}>Cancel</button>
          <button class="primary" onclick={createPoll} disabled={creating || !newTitle.trim()}>
            {creating ? 'Creating…' : 'Create'}
          </button>
        </div>
      </div>
    </div>
  {/if}

  {#if polls === null}
    <p class="dim">Loading…</p>
  {:else}
    {#if openPolls.length > 0}
      <h2 class="section">Active</h2>
      {#each openPolls as poll (poll.Id)}
        <a class="card pollcard" href={'#/poll/' + poll.Id}>
          <div class="title">{poll.Title}</div>
          <div class="dim">
            open · {poll.SuggestionCount} suggestion{poll.SuggestionCount === 1 ? '' : 's'} ·
            {poll.VoterCount} voted · by {poll.CreatedByName} · {fmtDate(poll.CreatedAt)}
          </div>
          <div class="go">Open poll →</div>
        </a>
      {/each}
    {/if}

    {#if closedPolls.length > 0}
      <div class="pastheader">
        <h2 class="section">Past</h2>
        {#if isAdmin}
          <button class="warning" onclick={deleteClosedPolls}>Delete all closed</button>
        {/if}
      </div>
      {#each closedPolls as poll (poll.Id)}
        <a class="card pollcard closed" href={'#/poll/' + poll.Id}>
          <div class="title">{poll.Title}</div>
          <div class="dim">closed {poll.ClosedAt ? fmtDate(poll.ClosedAt) : ''} · by {poll.CreatedByName}</div>
          <div class="go">Results →</div>
        </a>
      {/each}
    {/if}

    {#if polls.length === 0}
      <p class="dim">No polls yet. Create the first one!</p>
    {/if}
  {/if}
</div>

<style>
  .page { max-width: 640px; margin: 0 auto; padding: 1rem; display: flex; flex-direction: column; gap: 0.7rem; }
  .header { display: flex; justify-content: space-between; align-items: center; }
  h1 { margin: 0; font-size: 1.4rem; }
  .section { font-size: 0.9rem; text-transform: uppercase; letter-spacing: 1px; color: var(--jp-text-dim); margin: 0.8rem 0 0; }
  .pastheader { display: flex; justify-content: space-between; align-items: center; }
  .pastheader button.warning { padding: 0.3rem 0.6rem; font-size: 0.8rem; background: #f2b01e; color: #222; }
  .pastheader button.warning:hover { background: #d99e18; }
  .pollcard { display: block; text-decoration: none; color: inherit; }
  .pollcard .Title { font-weight: 600; font-size: 1.05rem; }
  .pollcard .go { color: var(--jp-accent); margin-top: 0.3rem; font-size: 0.9rem; }
  .pollcard.closed { opacity: 0.8; }
  .overlay {
    position: fixed; inset: 0; background: rgba(0,0,0,0.6);
    display: grid; place-items: center; z-index: 10;
  }
  .dialog { display: flex; flex-direction: column; gap: 0.7rem; min-width: 320px; }
  .dialog label { display: flex; gap: 0.4rem; align-items: center; }
  .actions { display: flex; justify-content: flex-end; gap: 0.5rem; }
</style>
