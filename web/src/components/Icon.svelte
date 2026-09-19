<script lang="ts">
  import { iconPaths, type IconName } from './icons';

  let {
    name,
    size = 18,
    class: className = ''
  }: {
    name: IconName;
    size?: number;
    class?: string;
  } = $props();
</script>

<svg
  class="icon {className}"
  width={size}
  height={size}
  viewBox="0 -960 960 960"
  fill="currentColor"
  aria-hidden="true"
  focusable="false"
><path d={iconPaths[name]} /></svg>

<style>
  .icon {
    flex-shrink: 0;
    display: inline-block;
    vertical-align: middle;
  }
</style>
