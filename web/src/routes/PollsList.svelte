<script lang="ts">
  import { onMount } from 'svelte';
  import { jellypoll } from '../lib/api';
  import { t, errorMessage } from '../lib/i18n.svelte';
  import type { PollSummary } from '../lib/types';
  import Icon from '../components/Icon.svelte';

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
      error = errorMessage(e);
    }
  }

  async function deleteClosedPolls() {
    if (!confirm(t('polls.deleteAllClosedConfirm', { count: closedPolls.length }))) return;
    try {
      await jellypoll.deleteAllClosedPolls();
      await load();
    } catch (e) {
      error = errorMessage(e);
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
      error = errorMessage(e);
      creating = false;
    }
  }

  const fmtDate = (iso: string) => new Date(iso).toLocaleDateString();
  const homeUrl = `${location.origin}/web/index.html#!/home`;
</script>

<div class="page">
  <div class="header">
    <a class="home" href={homeUrl} title={t('common.backToHome')}>
      <Icon name="home" size={18} /> <span>{t('common.jellyfinHome')}</span>
    </a>
    <h1><Icon name="how_to_vote" size={26} /> Jelly Polls</h1>
    <button class="primary" onclick={() => (showNewDialog = true)}>
      <Icon name="add" size={18} /> {t('polls.newPoll')}
    </button>
  </div>

  {#if error}<div class="error-box"><Icon name="error" size={16} /> {error}</div>{/if}

  {#if showNewDialog}
    <div class="overlay" role="dialog" onclick={(e) => e.target === e.currentTarget && (showNewDialog = false)}>
      <div class="card dialog">
        <div class="titlebar">
          <h3>{t('polls.newPoll')}</h3>
          <button class="iconbtn" title={t('common.close')} onclick={() => (showNewDialog = false)}><Icon name="close" size={18} /></button>
        </div>
        <input type="text" placeholder={t('polls.titlePlaceholder')} bind:value={newTitle} maxlength="100" />
        <label><input type="checkbox" checked disabled /> {t('polls.allowMovies')}</label>
        <label><input type="checkbox" bind:checked={newEpisodes} /> {t('polls.allowEpisodes')}</label>
        <label><input type="checkbox" bind:checked={newSeries} /> {t('polls.allowSeries')}</label>
        <div class="actions">
          <button onclick={() => (showNewDialog = false)}>{t('common.cancel')}</button>
          <button class="primary" onclick={createPoll} disabled={creating || !newTitle.trim()}>
            {creating ? t('polls.creating') : t('polls.create')}
          </button>
        </div>
      </div>
    </div>
  {/if}

  {#if polls === null}
    <p class="dim">{t('common.loading')}</p>
  {:else}
    {#if openPolls.length > 0}
      <h2 class="section">{t('polls.active')}</h2>
      {#each openPolls as poll (poll.Id)}
        <a class="card pollcard" href={'#/poll/' + poll.Id}>
          <div class="row1">
            <div class="title">{poll.Title}</div>
            <span class="chip open">
              <Icon name="how_to_vote" size={12} /> {t('status.open')}
            </span>
          </div>
          <div class="meta dim">
            <span><Icon name="add" size={13} /> {t('polls.suggestions', { count: poll.SuggestionCount })}</span>
            <span><Icon name="group" size={13} /> {t('polls.voted', { count: poll.VoterCount })}</span>
            <span>{t('polls.by', { name: poll.CreatedByName, date: fmtDate(poll.CreatedAt) })}</span>
          </div>
          <div class="go">{t('polls.openPoll')} <Icon name="chevron_right" size={16} /></div>
        </a>
      {/each}
    {/if}

    {#if closedPolls.length > 0}
      <div class="pastheader">
        <h2 class="section">{t('polls.past')}</h2>
        {#if isAdmin}
          <button class="warning" onclick={deleteClosedPolls}>
            <Icon name="delete" size={15} /> {t('polls.deleteAllClosed')}
          </button>
        {/if}
      </div>
      {#each closedPolls as poll (poll.Id)}
        <a class="card pollcard closed" href={'#/poll/' + poll.Id}>
          <div class="row1">
            <div class="title">{poll.Title}</div>
            <span class="chip">
              <Icon name="lock" size={12} /> {t('status.closed')}
            </span>
          </div>
          <div class="meta dim">
            <span>{t('polls.closedOn', { date: poll.ClosedAt ? fmtDate(poll.ClosedAt) : '' })}</span>
            <span>{t('board.by', { name: poll.CreatedByName })}</span>
          </div>
          <div class="go"><Icon name="trophy" size={15} /> {t('polls.results')} <Icon name="chevron_right" size={16} /></div>
        </a>
      {/each}
    {/if}

    {#if polls.length === 0}
      <div class="empty">
        <Icon name="how_to_vote" size={40} />
        <p class="dim">{t('polls.empty')}</p>
      </div>
    {/if}
  {/if}
</div>

<style>
  .page { max-width: 640px; margin: 0 auto; padding: 1rem; display: flex; flex-direction: column; gap: 0.7rem; }
  .header { display: flex; justify-content: space-between; align-items: center; gap: 0.7rem; flex-wrap: wrap; }
  .home {
    display: inline-flex; align-items: center; gap: 0.3rem;
    color: var(--jp-text-dim); text-decoration: none;
    font-size: 0.88rem; font-weight: 500; white-space: nowrap;
  }
  .home:hover { color: var(--jp-accent); }
  h1 {
    margin: 0; font-size: 1.35rem;
    display: flex; align-items: center; gap: 0.5rem;
  }
  h1 :global(.icon) { color: var(--jp-accent); }
  .section {
    font-size: 0.78rem; text-transform: uppercase; letter-spacing: 0.08em;
    color: var(--jp-text-dim); margin: 0.9rem 0 0;
  }
  .pastheader { display: flex; justify-content: space-between; align-items: center; gap: 0.5rem; }
  .pastheader button.warning {
    padding: 0.3rem 0.6rem; font-size: 0.8rem;
    background: transparent; border-color: transparent; color: var(--jp-warning);
  }
  .pastheader button.warning:hover { background: rgba(242, 176, 30, 0.12); }
  .pollcard {
    display: block; text-decoration: none; color: inherit;
    transition: border-color 0.15s ease, transform 0.15s ease;
  }
  .pollcard:hover { border-color: var(--jp-accent); transform: translateY(-1px); }
  .row1 { display: flex; align-items: center; justify-content: space-between; gap: 0.6rem; }
  .pollcard .title { font-weight: 600; font-size: 1.02rem; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .meta { display: flex; flex-wrap: wrap; gap: 0.35rem 0.9rem; margin-top: 0.4rem; font-size: 0.82rem; }
  .meta span { display: inline-flex; align-items: center; gap: 0.3rem; }
  .meta :global(.icon) { color: var(--jp-text-dim); }
  .go {
    display: inline-flex; align-items: center; gap: 0.15rem;
    color: var(--jp-accent); margin-top: 0.55rem; font-size: 0.88rem; font-weight: 500;
  }
  .pollcard.closed { opacity: 0.85; }
  .empty { display: flex; flex-direction: column; align-items: center; gap: 0.6rem; padding: 2.5rem 0; color: var(--jp-text-dim); }
  .overlay {
    position: fixed; inset: 0; background: rgba(0,0,0,0.6);
    display: grid; place-items: center; z-index: 10;
  }
  .dialog { display: flex; flex-direction: column; gap: 0.7rem; min-width: 320px; box-shadow: var(--jp-shadow); }
  .titlebar { display: flex; align-items: center; justify-content: space-between; }
  .titlebar h3 { margin: 0; }
  .dialog label { display: flex; gap: 0.4rem; align-items: center; font-size: 0.92rem; }
  .actions { display: flex; justify-content: flex-end; gap: 0.5rem; margin-top: 0.2rem; }
</style>
