<script lang="ts">
  import Poster from './Poster.svelte';
  import { auth } from '../lib/auth.svelte';
  import { t, errorMessage } from '../lib/i18n.svelte';
  import { jellypoll, detailUrl } from '../lib/api';
  import type { PollDetail, Suggestion } from '../lib/types';
  import Icon from './Icon.svelte';

  let {
    detail,
    myBallot = [],
    onChanged,
    onAddToOrder
  }: {
    detail: PollDetail;
    myBallot?: string[];
    onChanged: () => void;
    onAddToOrder: (suggestionId: string) => void;
  } = $props();

  function canRemove(s: Suggestion): boolean {
    if (detail.Poll.Status !== 'open') return false;
    return detail.IsAdmin || detail.IsCreator || s.SuggestedById === auth.userId;
  }

  function inMyOrder(s: Suggestion): boolean {
    return myBallot.includes(s.Id);
  }

  async function remove(s: Suggestion) {
    if (!confirm(t('board.removeConfirm', { name: s.Name }))) return;
    try {
      await jellypoll.removeSuggestion(detail.Poll.Id, s.Id);
      onChanged();
    } catch (e) {
      alert(errorMessage(e));
    }
  }

  // "I don't want to watch this" — a social signal, never affects points.
  async function toggleThumbs(s: Suggestion) {
    if (detail.Poll.Status !== 'open') return;
    try {
      if (s.IHaveThumbsDown) await jellypoll.removeThumbsDown(detail.Poll.Id, s.Id);
      else await jellypoll.thumbsDown(detail.Poll.Id, s.Id);
      onChanged();
    } catch (e) {
      alert(errorMessage(e));
    }
  }
</script>

<div class="board">
  <h3 class="sechead"><Icon name="how_to_vote" size={18} /><span>{t('board.suggested')} <span class="count">({detail.Suggestions.length})</span></span></h3>

  {#if detail.Suggestions.length === 0}
    <p class="empty dim"><Icon name="movie" size={16} /> {t('board.empty')}</p>
  {:else}
    <ul class="list">
      {#each detail.Suggestions as s (s.Id)}
        <li class="row" class:missing={s.ItemMissing}>
          <Poster itemId={s.ItemId} name={s.Name} size={40} missing={s.ItemMissing} />
          <div class="info">
            <div class="nameline">
              {#if s.ItemMissing}
                <span class="name dim">{s.Name}</span>
              {:else}
                <a class="name titlelink" href={detailUrl(s.ItemId)} target="_blank" rel="noreferrer">{s.Name}</a>
              {/if}
              {#if s.Year}<span class="year dim">{s.Year}</span>{/if}
            </div>
            <div class="by dim">
              <Icon name="group" size={11} /> {t('board.by', { name: s.SuggestedByName })}
              {#if s.ItemMissing}<Icon name="visibility_off" size={12} /> {t('board.missing')}{/if}
            </div>
          </div>
          {#if detail.Poll.Status === 'open' || s.ThumbsDownNames.length > 0}
            <button
              class="thumbbtn"
              class:active={s.IHaveThumbsDown}
              disabled={detail.Poll.Status !== 'open'}
              title={s.IHaveThumbsDown ? t('board.thumbDownMine') : t('board.thumbDown')}
              onclick={() => toggleThumbs(s)}
            >
              <span class="thumbicon"><Icon name="thumb_down" size={14} /></span>
              {#if s.ThumbsDownNames.length > 0}
                <span class="thumbcount" title={t('board.thumbDownBy', { names: s.ThumbsDownNames.join(', ') })}>{s.ThumbsDownNames.length}</span>
              {/if}
            </button>
          {/if}
          {#if detail.Poll.Status === 'open' && !inMyOrder(s) && !s.ItemMissing}
            <button class="addbtn" title={t('board.addTitle')} onclick={() => onAddToOrder(s.Id)}>
              <Icon name="add" size={14} /> {t('board.order')}
            </button>
          {:else if inMyOrder(s)}
            <span class="chip success" title={t('board.inOrderTitle')}><Icon name="check" size={12} /> {t('board.inOrder')}</span>
          {/if}
          {#if canRemove(s)}
            <button class="iconbtn danger" title={t('board.removeTitle')} onclick={() => remove(s)}>
              <Icon name="close" size={16} />
            </button>
          {/if}
        </li>
      {/each}
    </ul>
  {/if}
</div>

<style>
  .count { color: var(--jp-text-dim); font-weight: 500; }
  .empty { display: flex; align-items: center; gap: 0.4rem; font-size: 0.85rem; margin: 0.4rem 0 0; }
  .list { list-style: none; margin: 0.4rem 0 0; padding: 0; display: flex; flex-direction: column; gap: 0.3rem; }
  .row {
    display: flex; align-items: center; gap: 0.5rem;
    padding: 0.3rem; border-radius: var(--jp-radius-sm);
    background: var(--jp-surface-2); border: 1px solid transparent;
    transition: border-color 0.12s ease;
  }
  .row:hover { border-color: var(--jp-border); }
  .row.missing { opacity: 0.6; }
  .info { flex: 1; min-width: 0; display: flex; flex-direction: column; gap: 0.1rem; }
  .nameline { display: flex; align-items: baseline; gap: 0.35rem; min-width: 0; }
  .name {
    min-width: 0;
    font-size: 0.88rem; font-weight: 500;
    overflow: hidden; text-overflow: ellipsis; white-space: nowrap;
  }
  .year { font-size: 0.72rem; flex-shrink: 0; }
  .titlelink { color: inherit; text-decoration: none; }
  .titlelink:hover { color: var(--jp-accent); text-decoration: underline; }
  .by { font-size: 0.72rem; display: flex; align-items: center; gap: 0.25rem; }
  .addbtn {
    background: transparent; border: 1px solid var(--jp-accent);
    color: var(--jp-accent); font-size: 0.72rem; font-weight: 600;
    padding: 0.22rem 0.5rem; border-radius: 999px;
    display: inline-flex; align-items: center; gap: 0.25rem; white-space: nowrap;
  }
  .addbtn:hover { background: rgba(77, 163, 255, 0.12); }
  .thumbbtn {
    display: inline-flex; align-items: center; gap: 0.25rem; flex-shrink: 0;
    background: transparent; border: 1px solid var(--jp-border);
    color: var(--jp-text-dim); font-size: 0.72rem; font-weight: 600;
    padding: 0.22rem 0.4rem; border-radius: 999px; white-space: nowrap;
    transition: background 0.12s ease, color 0.12s ease, border-color 0.12s ease;
  }
  .thumbbtn:hover:not(:disabled) { background: rgba(211, 47, 47, 0.1); color: var(--jp-danger); border-color: var(--jp-danger); }
  .thumbbtn.active { background: rgba(211, 47, 47, 0.15); color: var(--jp-danger); border-color: var(--jp-danger); }
  .thumbbtn:disabled { opacity: 0.5; cursor: default; }
  .thumbicon { display: inline-flex; transform: scaleY(-1); }
  .thumbcount {
    font-size: 0.68rem; font-weight: 700; line-height: 1;
    background: var(--jp-surface-3); padding: 0.1rem 0.35rem; border-radius: 999px;
  }
  .thumbbtn.active .thumbcount { background: rgba(211, 47, 47, 0.28); }
</style>
