<script lang="ts">
  import Poster from './Poster.svelte';
  import type { PollDetail } from '../lib/types';

  let {
    detail,
    myBallot,
    onReorder,
    onRemove
  }: {
    detail: PollDetail;
    myBallot: string[];
    onReorder: (newOrder: string[]) => void;
    onRemove: (suggestionId: string) => void;
  } = $props();

  let dragIndex = $state<number | null>(null);

  const byId = $derived(new Map(detail.Suggestions.map((s) => [s.Id, s])));

  function move(index: number, delta: number) {
    const target = index + delta;
    if (target < 0 || target >= myBallot.length) return;
    const copy = [...myBallot];
    [copy[index], copy[target]] = [copy[target], copy[index]];
    onReorder(copy);
  }

  function onDragStart(index: number) {
    dragIndex = index;
  }

  function onDragOver(event: DragEvent, index: number) {
    event.preventDefault();
    if (dragIndex === null || dragIndex === index) return;
    const copy = [...myBallot];
    const [moved] = copy.splice(dragIndex, 1);
    copy.splice(index, 0, moved);
    dragIndex = index;
    onReorder(copy);
  }

  function onDragEnd() {
    dragIndex = null;
  }
</script>

<div>
  <h3>My watch order</h3>
  {#if myBallot.length === 0}
    <p class="dim">Rank the suggested titles — position 1 is your top pick. Drag cards or use the arrows.</p>
  {:else}
    <div class="list" role="list">
      {#each myBallot as sid, index (sid)}
        {@const s = byId.get(sid)}
        {#if s}
          <div
            class="card row"
            role="listitem"
            class:dragging={dragIndex === index}
            draggable="true"
            ondragstart={() => onDragStart(index)}
            ondragover={(e) => onDragOver(e, index)}
            ondragend={onDragEnd}
          >
            <span class="rank">{index + 1}</span>
            <span class="handle" title="Drag to reorder">⋮⋮</span>
            <Poster itemId={s.ItemId} name={s.Name} size={40} />
            <div class="info">
              <div class="name">{s.Name}</div>
            </div>
            <button title="Up" onclick={() => move(index, -1)} disabled={index === 0}>↑</button>
            <button title="Down" onclick={() => move(index, 1)} disabled={index === myBallot.length - 1}>↓</button>
            <button class="danger" title="Remove from my ranking" onclick={() => onRemove(sid)}>✕</button>
          </div>
        {/if}
      {/each}
    </div>
  {/if}
</div>

<style>
  .list { display: flex; flex-direction: column; gap: 0.5rem; }
  .row {
    display: flex;
    align-items: center;
    gap: 0.5rem;
    padding: 0.4rem;
    cursor: grab;
  }
  .row.dragging { opacity: 0.5; }
  .Rank {
    width: 1.8rem;
    height: 1.8rem;
    border-radius: 50%;
    background: var(--jp-accent);
    color: #fff;
    display: grid;
    place-items: center;
    font-weight: 700;
    flex-shrink: 0;
  }
  .handle { color: var(--jp-text-dim); letter-spacing: -2px; }
  .info { flex: 1; min-width: 0; }
  .Name { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .row button { padding: 0.25rem 0.55rem; }
</style>
