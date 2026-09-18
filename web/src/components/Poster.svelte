<script lang="ts">
  import { onMount } from 'svelte';
  import { posterUrl } from '../lib/api';

  let { itemId, name, size = 120 }: { itemId: string; name: string; size?: number } = $props();

  let url = $state<string | null>(null);

  onMount(async () => {
    url = await posterUrl(itemId);
  });
</script>

<div class="poster" style="width: {size}px">
  {#if url}
    <img src={url} alt={name} loading="lazy" />
  {:else}
    <div class="fallback" style="height: {size * 1.5}px">
      <span>{name.slice(0, 1)}</span>
    </div>
  {/if}
</div>

<style>
  .poster img,
  .fallback {
    width: 100%;
    aspect-ratio: 2 / 3;
    object-fit: cover;
    border-radius: var(--jp-radius);
    display: block;
  }
  .fallback {
    background: linear-gradient(135deg, var(--jp-surface-2), var(--jp-surface));
    display: grid;
    place-items: center;
    font-size: 2rem;
    color: var(--jp-text-dim);
  }
</style>
