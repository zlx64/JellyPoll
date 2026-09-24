<script lang="ts">
  import { onMount } from 'svelte';
  import { jellypoll, ApiError } from '../lib/api';
  import { t, errorMessage } from '../lib/i18n.svelte';
  import type { PollDetail, RankingBreakdown } from '../lib/types';
  import SuggestPicker from '../components/SuggestPicker.svelte';
  import SuggestionBoard from '../components/SuggestionBoard.svelte';
  import MyRanking from '../components/MyRanking.svelte';
  import Standings from '../components/Standings.svelte';
  import Icon from '../components/Icon.svelte';

  let { pollId }: { pollId: string } = $props();

  let detail = $state<PollDetail | null>(null);
  let myBallot = $state<string[]>([]);
  let error = $state('');
  let toast = $state('');
  let toastIsError = $state(false);
  let saved = $state(false);
  let shareRanking = $state(false);
  let breakdown = $state<RankingBreakdown>({});
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
      // Don't clobber local unranked edits while a debounced save is pending.
      if (!saveTimer) {
        myBallot = [...detail.MyBallot];
      }
      error = '';
    } catch (e) {
      error = errorMessage(e);
      return;
    }
    // The vote breakdown is only available when the viewer opted in (mutual opt-in).
    if (shareRanking) {
      try {
        breakdown = await jellypoll.rankingBreakdown(pollId);
      } catch {
        breakdown = {};
      }
    } else {
      breakdown = {};
    }
  }

  onMount(() => {
    init();
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

  async function init() {
    try {
      shareRanking = (await jellypoll.getPreferences()).shareRanking;
    } catch {
      /* default off */
    }
    await load();
  }

  async function toggleShareRanking() {
    const next = !shareRanking;
    shareRanking = next; // optimistic
    try {
      await jellypoll.setShareRanking(next);
      if (next) {
        breakdown = await jellypoll.rankingBreakdown(pollId);
      } else {
        breakdown = {};
      }
    } catch (e) {
      shareRanking = !next; // revert
      showToast(errorMessage(e), true);
    }
  }

  async function pollState() {
    if (document.hidden || !detail) return;
    try {
      // 204 (unchanged) resolves to undefined; 200 resolves to { stateVersion }.
      const res = await jellypoll.state(detail.Poll.Id, detail.Poll.StateVersion);
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

  function addToOrder(suggestionId: string) {
    if (!myBallot.includes(suggestionId)) {
      myBallot = [...myBallot, suggestionId];
      scheduleSave();
    }
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
      saveTimer = undefined;
      await load();
    } catch (e) {
      if (e instanceof ApiError && e.code === 'poll_closed') {
        await load();
        return;
      }
      showToast(errorMessage(e), true);
    }
  }

  async function closePoll() {
    if (!confirm(t('room.closePollConfirm'))) return;
    try {
      await jellypoll.closePoll(pollId);
      await load();
    } catch (e) {
      showToast(errorMessage(e), true);
    }
  }

  async function reopenPoll() {
    try {
      await jellypoll.reopenPoll(pollId);
      await load();
    } catch (e) {
      showToast(errorMessage(e), true);
    }
  }

  function onChanged() {
    load();
  }

  async function onAdded(msg: string, isError: boolean, suggestionIds?: string[]) {
    showToast(msg, isError);
    const fresh = (suggestionIds ?? []).filter((id) => !myBallot.includes(id));
    if (fresh.length > 0) {
      // Auto-append the suggester's own picks to their watch order and save immediately
      // (no debounce — avoids races with the detail refetch).
      myBallot = [...myBallot, ...fresh];
      clearTimeout(saveTimer);
      saveTimer = undefined;
      try {
        await jellypoll.saveBallot(pollId, myBallot);
        saved = true;
      } catch (e) {
        showToast(errorMessage(e), true);
      }
    }
    await load();
  }

  const gold = $derived(detail?.Standings.find((s) => s.Rank === 1 && !s.ItemMissing) ?? null);
  const detailLink = $derived(
    gold ? `${location.origin}/web/index.html#!/details?id=${gold.ItemId}` : ''
  );
  const homeUrl = `${location.origin}/web/index.html#!/home`;
  // Item ids already suggested in this poll — used to hide them from search.
  // Normalized (hyphens stripped, lowercased) because /Items returns hyphen-less
  // ids while stored suggestion ItemIds are hyphenated GUIDs.
  const existingItemIds = $derived(
    new Set((detail?.Suggestions ?? []).map((s) => s.ItemId.replace(/-/g, '').toLowerCase()))
  );
</script>

{#if detail}
  <div class="page">
    <div class="header">
      <a class="home" href={homeUrl} title={t('common.backToHome')}>
        <Icon name="home" size={18} /> <span>{t('common.jellyfinHome')}</span>
      </a>
      <a class="home" href="#/polls" title={t('common.backToPolls')}>
        <Icon name="format_list_numbered" size={18} /> <span>{t('common.allPolls')}</span>
      </a>
      <h1>
        <Icon name="how_to_vote" size={22} class="titleicon" />
        <span class="titletext">{detail.Poll.Title}</span>
        {#if detail.Poll.Status === 'open'}
          <span class="chip open"><Icon name="how_to_vote" size={12} /> {t('status.open')}</span>
        {:else}
          <span class="chip"><Icon name="lock" size={12} /> {t('status.closed')}</span>
        {/if}
      </h1>
      <div class="manage">
        {#if detail.IsCreator || detail.IsAdmin}
          {#if detail.Poll.Status === 'open'}
            <button class="primary" onclick={closePoll}><Icon name="lock" size={16} /> {t('room.closePoll')}</button>
          {:else}
            <button onclick={reopenPoll}><Icon name="refresh" size={16} /> {t('room.reopen')}</button>
          {/if}
        {/if}
      </div>
    </div>

    {#if error}<div class="error-box"><Icon name="error" size={16} /> {error}</div>{/if}

    {#if detail.Poll.Status === 'closed'}
      <h2 class="finalhead"><Icon name="trophy" size={22} class="goldicon" /> {t('room.finalResults')}</h2>
      <Standings standings={detail.Standings} closed={true} breakdown={breakdown} canSee={shareRanking} />
      {#if gold}
        <a class="card watchnext" href={detailLink} target="_blank" rel="noreferrer">
          <Icon name="play_arrow" size={20} class="playicon" />
          <span>{t('room.watchNow', { name: gold.Name })}</span>
          <Icon name="open_in_new" size={15} class="exticon" />
        </a>
      {/if}
    {:else}
      <div class="columns">
        <div class="col">
          <SuggestPicker poll={detail.Poll} existingItemIds={existingItemIds} onAdded={onAdded} />
          <SuggestionBoard {detail} {myBallot} onChanged={onChanged} onAddToOrder={addToOrder} />
        </div>
        <div class="col">
          <MyRanking {detail} {myBallot} {saved} onReorder={reorder} onRemove={removeFromBallot}
            shareRanking={shareRanking} onToggleShare={toggleShareRanking} />
        </div>
        <div class="col">
          <div class="card">
            <h3 class="sechead"><Icon name="trophy" size={18} /><span>{t('room.standings')} <span class="live">{t('room.live')}</span></span></h3>
            <Standings standings={detail.Standings} breakdown={breakdown} canSee={shareRanking} />
          </div>
        </div>
      </div>
    {/if}

    {#if toast}
      <div class="toast" class:err={toastIsError}>
        <Icon name={toastIsError ? 'error' : 'check_circle'} size={17} />
        <span>{toast}</span>
      </div>
    {/if}
  </div>
{:else}
  <p class="dim">{t('common.loading')}</p>
  {#if error}<div class="error-box">{error}</div>{/if}
{/if}

<style>
  .page { max-width: 1280px; margin: 0 auto; padding: 1rem; }
  .header { display: flex; align-items: center; gap: 0.8rem; flex-wrap: wrap; }
  .header h1 {
    margin: 0; font-size: 1.25rem; flex: 1; min-width: 0;
    display: flex; align-items: center; gap: 0.5rem;
  }
  :global(.titleicon) { color: var(--jp-accent); }
  .titletext { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .home {
    display: inline-flex; align-items: center; gap: 0.3rem;
    color: var(--jp-text-dim); text-decoration: none;
    font-size: 0.88rem; font-weight: 500; white-space: nowrap;
  }
  .home:hover { color: var(--jp-accent); }
  .finalhead {
    display: flex; align-items: center; gap: 0.5rem;
    font-size: 1.2rem; margin: 1rem 0 0;
  }
  :global(.goldicon) { color: var(--jp-gold); }
  .columns { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 1rem; align-items: start; margin-top: 0.8rem; }
  @media (max-width: 1100px) { .columns { grid-template-columns: 1fr 1fr; } }
  @media (max-width: 700px) { .columns { grid-template-columns: 1fr; } }
  .col { display: flex; flex-direction: column; gap: 1rem; min-width: 0; }
  .live {
    font-size: 0.7rem; font-weight: 600;
    color: var(--jp-success);
    display: inline-flex; align-items: center; gap: 0.3rem;
    position: relative; top: -2px;
  }
  .live::before {
    content: ''; width: 7px; height: 7px; border-radius: 50%;
    background: var(--jp-success);
    animation: pulse 1.6s ease-in-out infinite;
  }
  @keyframes pulse { 0%, 100% { opacity: 1; } 50% { opacity: 0.35; } }
  .toast {
    position: fixed; bottom: 1rem; left: 50%; transform: translateX(-50%);
    display: flex; align-items: center; gap: 0.5rem;
    background: var(--jp-surface-2); color: var(--jp-text);
    border: 1px solid var(--jp-border);
    padding: 0.6rem 1rem; border-radius: var(--jp-radius);
    box-shadow: var(--jp-shadow); z-index: 20;
  }
  .toast :global(.icon) { color: var(--jp-success); }
  .toast.err { background: rgba(211, 47, 47, 0.15); border-color: rgba(211, 47, 47, 0.4); color: var(--jp-danger); }
  .toast.err :global(.icon) { color: var(--jp-danger); }
  .watchnext {
    display: inline-flex; align-items: center; gap: 0.5rem;
    margin-top: 0.8rem; text-decoration: none; color: inherit;
    border: 1px solid var(--jp-gold); font-weight: 500;
    transition: background 0.15s ease, transform 0.15s ease;
  }
  .watchnext:hover { background: rgba(212, 175, 55, 0.1); transform: translateY(-1px); }
  :global(.playicon) { color: var(--jp-gold); }
  :global(.exticon) { color: var(--jp-text-dim); }
</style>
