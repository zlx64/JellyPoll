<script lang="ts">
  import Poster from './Poster.svelte';
  import { detailUrl } from '../lib/api';
  import type { PollDetail } from '../lib/types';
  import Icon from './Icon.svelte';

  let {
    detail,
    myBallot,
    saved,
    onReorder,
    onRemove
  }: {
    detail: PollDetail;
    myBallot: string[];
    saved: boolean;
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

<div class="card">
  <h3 class="sechead"><Icon name="star" size={18} /> My watch order</h3>
  <p class="savehint" class:done={saved}>
    {#if saved}<Icon name="check" size={14} />{/if}
    {saved ? 'Ranking saved' : 'Ranking saves automatically'}
  </p>

  {#if myBallot.length === 0}
    <p class="empty dim"><Icon name="format_list_numbered" size={16} /> Rank the suggested titles — position 1 is your top pick. Drag cards or use the arrows.</p>
  {:else}
    <div class="list" role="list">
      {#each myBallot as sid, index (sid)}
        {@const s = byId.get(sid)}
        {#if s}
          <div
            class="row"
            role="listitem"
            class:dragging={dragIndex === index}
            draggable="true"
            ondragstart={() => onDragStart(index)}
            ondragover={(e) => onDragOver(e, index)}
            ondragend={onDragEnd}
          >
            <span class="rank" class:gold={index === 0} class:silver={index === 1} class:bronze={index === 2}>{index + 1}</span>
            <span class="handle" title="Drag to reorder"><Icon name="drag_indicator" size={16} /></span>
            <Poster itemId={s.ItemId} name={s.Name} size={32} missing={s.ItemMissing} />
            <div class="info">
              {#if s.ItemMissing}
                <span class="name dim">{s.Name}</span>
                <Icon name="visibility_off" size={13} class="missingicon" />
              {:else}
                <a class="name titlelink" href={detailUrl(s.ItemId)} target="_blank" rel="noreferrer">{s.Name}</a>
              {/if}
            </div>
            <span class="controls">
              <button class="iconbtn" title="Move up" onclick={() => move(index, -1)} disabled={index === 0}><Icon name="arrow_upward" size={16} /></button>
              <button class="iconbtn" title="Move down" onclick={() => move(index, 1)} disabled={index === myBallot.length - 1}><Icon name="arrow_downward" size={16} /></button>
              <button class="iconbtn danger" title="Remove from my ranking" onclick={() => onRemove(sid)}><Icon name="close" size={16} /></button>
            </span>
          </div>
        {/if}
      {/each}
    </div>
  {/if}
</div>

<style>
  .savehint {
    display: flex; align-items: center; gap: 0.3rem;
    margin: 0.15rem 0 0.6rem; font-size: 0.78rem; color: var(--jp-text-dim);
  }
  .savehint.done { color: var(--jp-success); }
  .empty {
    display: flex; align-items: flex-start; gap: 0.4rem;
    font-size: 0.82rem; margin: 0.2rem 0 0; line-height: 1.35;
  }
  .list { display: flex; flex-direction: column; gap: 0.3rem; }
  .row {
    display: flex;
    align-items: center;
    gap: 0.4rem;
    padding: 0.25rem;
    cursor: grab;
    border-radius: var(--jp-radius-sm);
    background: var(--jp-surface-2);
    border: 1px solid transparent;
    transition: border-color 0.12s ease, opacity 0.12s ease;
  }
  .row:hover { border-color: var(--jp-border); }
  .row.dragging { opacity: 0.5; }
  .rank {
    width: 1.4rem;
    height: 1.4rem;
    border-radius: 50%;
    background: var(--jp-surface-3);
    color: var(--jp-text-dim);
    display: grid;
    place-items: center;
    font-weight: 700;
    font-size: 0.75rem;
    flex-shrink: 0;
  }
  .rank.gold { background: var(--jp-gold); color: #3a2c00; }
  .rank.silver { background: var(--jp-silver); color: #26282c; }
  .rank.bronze { background: var(--jp-bronze); color: #2e1a08; }
  .handle { color: var(--jp-text-dim); display: inline-flex; }
  .info {
    flex: 1; min-width: 0;
    display: flex; align-items: center; gap: 0.25rem;
  }
  .name {
    min-width: 0;
    overflow: hidden; text-overflow: ellipsis; white-space: nowrap; font-size: 0.86rem;
  }
  .titlelink { color: inherit; text-decoration: none; }
  .titlelink:hover { color: var(--jp-accent); text-decoration: underline; }
  .info :global(.missingicon) { color: var(--jp-text-dim); flex-shrink: 0; }
  .controls { display: inline-flex; gap: 0.1rem; }
  .controls button:disabled { opacity: 0.25; }
</style>
