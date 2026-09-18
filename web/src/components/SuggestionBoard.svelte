<script lang="ts">
  import Poster from './Poster.svelte';
  import { auth } from '../lib/auth.svelte';
  import { jellypoll } from '../lib/api';
  import type { PollDetail, Suggestion } from '../lib/types';

  let {
    detail,
    onChanged
  }: {
    detail: PollDetail;
    onChanged: () => void;
  } = $props();

  function canRemove(s: Suggestion): boolean {
    if (detail.poll.status !== 'open') return false;
    return detail.isAdmin || detail.isCreator || s.suggestedById === auth.userId;
  }

  async function remove(s: Suggestion) {
    if (!confirm(`Remove "${s.name}" from the poll?`)) return;
    try {
      await jellypoll.removeSuggestion(detail.poll.id, s.id);
      onChanged();
    } catch (e) {
      alert(e instanceof Error ? e.message : String(e));
    }
  }
</script>

<div>
  <h3>Suggested ({detail.suggestions.length})</h3>
  <div class="board">
    {#each detail.suggestions as s (s.id)}
      <div class="card item" class:missing={s.itemMissing}>
        <Poster itemId={s.itemId} name={s.name} size={64} />
        <div class="info">
          <div class="name">{s.name} <span class="dim">{s.year ?? ''}</span></div>
          <div class="dim">by {s.suggestedByName}{s.itemMissing ? ' · no longer in library' : ''}</div>
        </div>
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
</style>
