<script lang="ts">
  import { onMount } from 'svelte';
  import { jellypoll, ApiError } from '../lib/api';
  import type { PollDetail } from '../lib/types';
  import SuggestPicker from '../components/SuggestPicker.svelte';
  import SuggestionBoard from '../components/SuggestionBoard.svelte';
  import MyRanking from '../components/MyRanking.svelte';
  import Standings from '../components/Standings.svelte';

  let { pollId }: { pollId: string } = $props();

  let detail = $state<PollDetail | null>(null);
  let myBallot = $state<string[]>([]);
  let error = $state('');
  let toast = $state('');
  let toastIsError = $state(false);
  let saved = $state(false);
  let saveTimer: ReturnType<typeof setTimeout> | undefined;
  let toastTimer: ReturnType<typeof setTimeout> | undefined;

  function showToast(msg: string, isError = false) {
    toast = msg;
    toastIsError = isError;
    clearTimeout(toastTimer);
    toastTimer = setTimeout(() => (toast = ''), 4000);
  }

  async function load() {
    try {
      detail = await jellypoll.getPoll(pollId);
      myBallot = [...detail.myBallot];
      error = '';
    } catch (e) {
      error = e instanceof Error ? e.message : String(e);
    }
  }

  onMount(() => {
    load();
    const timer = setInterval(pollState, 4000);
    const visibility = () => {
      if (!document.hidden) pollState();
    };
    document.addEventListener('visibilitychange', visibility);
    return () => {
      clearInterval(timer);
      document.removeEventListener('visibilitychange', visibility);
    };
  });

  async function pollState() {
    if (document.hidden || !detail) return;
    try {
      // 204 (unchanged) resolves to undefined; 200 resolves to { stateVersion }.
      const res = await jellypoll.state(detail.poll.id, detail.poll.stateVersion);
      if (res) await load();
    } catch {
      /* network error — next tick retries */
    }
  }

  // ---------- ballot editing + auto-save (doc 05 §2.2) ----------

  function reorder(newOrder: string[]) {
    myBallot = newOrder;
    scheduleSave();
  }

  function removeFromBallot(suggestionId: string) {
    myBallot = myBallot.filter((id) => id !== suggestionId);
    scheduleSave();
  }

  function scheduleSave() {
    saved = false;
    clearTimeout(saveTimer);
    saveTimer = setTimeout(saveBallot, 1500);
  }

  async function saveBallot() {
    try {
      await jellypoll.saveBallot(pollId, myBallot);
      saved = true;
      await load();
    } catch (e) {
      if (e instanceof ApiError && e.code === 'poll_closed') {
        await load();
        return;
      }
      showToast(e instanceof Error ? e.message : String(e), true);
    }
  }

  async function closePoll() {
    if (!confirm('Close this poll and finalize the podium?')) return;
    try {
      await jellypoll.closePoll(pollId);
      await load();
    } catch (e) {
      showToast(e instanceof Error ? e.message : String(e), true);
    }
  }

  async function reopenPoll() {
    try {
      await jellypoll.reopenPoll(pollId);
      await load();
    } catch (e) {
      showToast(e instanceof Error ? e.message : String(e), true);
    }
  }

  function onChanged() {
    load();
  }

  function onAdded(msg: string, isError: boolean) {
    showToast(msg, isError);
    load();
  }

  const gold = $derived(detail?.standings.find((s) => s.rank === 1 && !s.itemMissing) ?? null);
  const detailLink = $derived(
    gold ? `${location.origin}/web/index.html#!/details?id=${gold.itemId}` : ''
  );
</script>

{#if detail}
  <div class="page">
    <div class="header">
      <a class="back" href="#/polls">← Polls</a>
      <h1>{detail.poll.title} <span class="badge" class:closed={detail.poll.status === 'closed'}>{detail.poll.status}</span></h1>
      <div class="manage">
        {#if detail.isCreator || detail.isAdmin}
          {#if detail.poll.status === 'open'}
            <button class="primary" onclick={closePoll}>Close poll</button>
          {:else}
            <button onclick={reopenPoll}>Reopen</button>
          {/if}
        {/if}
      </div>
    </div>
    <p class="dim">by {detail.poll.createdByName}</p>

    {#if error}<div class="error-box">{error}</div>{/if}

    {#if detail.poll.status === 'closed'}
      <h2>Final results</h2>
      <Standings standings={detail.standings} closed={true} />
      {#if gold}
        <a class="card watchnext" href={detailLink} target="_blank" rel="noreferrer">
          ▶ Watch "{gold.name}" now
        </a>
      {/if}
    {:else}
      <div class="columns">
        <div class="col">
          <SuggestPicker poll={detail.poll} onAdded={onAdded} />
          <SuggestionBoard {detail} onChanged={onChanged} />
        </div>
        <div class="col">
          <MyRanking {detail} {myBallot} onReorder={reorder} onRemove={removeFromBallot} />
          <p class="dim savehint">{saved ? '✓ ranking saved' : 'ranking saves automatically'}</p>
          <h3>Standings <span class="live">live ●</span></h3>
          <Standings standings={detail.standings} />
        </div>
      </div>
    {/if}

    {#if toast}
      <div class="toast" class:err={toastIsError}>{toast}</div>
    {/if}
  </div>
{:else}
  <p class="dim">Loading…</p>
  {#if error}<div class="error-box">{error}</div>{/if}
{/if}

<style>
  .page { max-width: 1000px; margin: 0 auto; padding: 1rem; }
  .header { display: flex; align-items: center; gap: 0.8rem; flex-wrap: wrap; }
  .header h1 { margin: 0; font-size: 1.3rem; flex: 1; }
  .back { color: var(--jp-accent); text-decoration: none; }
  .badge {
    font-size: 0.7rem; text-transform: uppercase; padding: 0.15rem 0.5rem;
    border-radius: 999px; background: var(--jp-accent); color: #fff; vertical-align: middle;
  }
  .badge.closed { background: var(--jp-text-dim); }
  .columns { display: grid; grid-template-columns: 1fr 1fr; gap: 1.2rem; }
  @media (max-width: 900px) { .columns { grid-template-columns: 1fr; } }
  .col { display: flex; flex-direction: column; gap: 1rem; min-width: 0; }
  .live { color: #4caf50; font-size: 0.75rem; }
  .savehint { font-size: 0.8rem; margin: -0.6rem 0 0; }
  .toast {
    position: fixed; bottom: 1rem; left: 50%; transform: translateX(-50%);
    background: var(--jp-surface-2); padding: 0.6rem 1rem; border-radius: var(--jp-radius);
    box-shadow: 0 4px 14px rgba(0,0,0,0.4); z-index: 20;
  }
  .toast.err { background: var(--jp-danger); color: #fff; }
  .watchnext {
    display: inline-block; margin-top: 0.8rem; text-decoration: none; color: inherit;
    border: 1px solid var(--jp-gold);
  }
</style>
