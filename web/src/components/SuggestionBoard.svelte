<script lang="ts">
  import Poster from './Poster.svelte';
  import { auth } from '../lib/auth.svelte';
  import { jellypoll } from '../lib/api';
  import type { PollDetail, Suggestion } from '../lib/types';

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
    if (!confirm(`Remove "${s.Name}" from the poll?`)) return;
    try {
      await jellypoll.removeSuggestion(detail.Poll.Id, s.Id);
      onChanged();
    } catch (e) {
      alert(e instanceof Error ? e.message : String(e));
    }
  }
</script>

<div>
  <h3>Suggested ({detail.Suggestions.length})</h3>
  <div class="board">
    {#each detail.Suggestions as s (s.Id)}
      <div class="card item" class:missing={s.ItemMissing}>
        <Poster itemId={s.ItemId} name={s.Name} size={64} />
        <div class="info">
          <div class="name">{s.Name} <span class="dim">{s.Year ?? ''}</span></div>
          <div class="dim">by {s.SuggestedByName}{s.ItemMissing ? ' · no longer in library' : ''}</div>
        </div>
        {#if detail.Poll.Status === 'open' && !inMyOrder(s) && !s.ItemMissing}
          <button class="primary" title="Add to my watch order" onclick={() => onAddToOrder(s.Id)}>＋ order</button>
        {:else if inMyOrder(s)}
          <span class="dim ordered" title="In your watch order">✓ in my order</span>
        {/if}
        {#if canRemove(s)}
          <button class="danger" title="Remove from poll" onclick={() => remove(s)}>✕</button>
        {/if}
      </div>
    {:else}
      <p class="dim">Nothing suggested yet — be the first!</p>
    {/each}
  </div>
</div>

<style>
  .board { display: flex; flex-direction: column; gap: 0.5rem; }
  .item { display: flex; align-items: center; gap: 0.7rem; padding: 0.5rem; }
  .item.missing { opacity: 0.45; }
  .info { flex: 1; min-width: 0; }
  .name { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .ordered { color: var(--jp-success, #5cbd5c); font-size: 0.8rem; white-space: nowrap; }
  .board button.primary { padding: 0.35rem 0.6rem; font-size: 0.85rem; white-space: nowrap; }
</style>
