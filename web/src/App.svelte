<script lang="ts">
  import { onMount } from 'svelte';
  import { auth, initAuth, restoreSession } from './lib/auth.svelte';
  import Login from './routes/Login.svelte';
  import PollsList from './routes/PollsList.svelte';
  import PollRoom from './routes/PollRoom.svelte';

  let route = $state(parseHash());

  function parseHash(): { name: 'polls' | 'poll'; id?: string } {
    const hash = location.hash.replace(/^#\/?/, '');
    const match = hash.match(/^poll\/([0-9a-f-]{36})$/i);
    if (match) return { name: 'poll', id: match[1] };
    return { name: 'polls' };
  }

  onMount(() => {
    restoreSession();
    const onHash = () => (route = parseHash());
    window.addEventListener('hashchange', onHash);
    return () => window.removeEventListener('hashchange', onHash);
  });

  // Kick off auth bootstrap once.
  $effect(() => {
    if (auth.status === 'checking') initAuth();
  });
</script>

{#if auth.status === 'checking'}
  <p class="dim center">Loading…</p>
{:else if auth.status === 'logged-out'}
  <Login />
{:else if route.name === 'poll' && route.id}
  <PollRoom pollId={route.id} />
{:else}
  <PollsList />
{/if}

<style>
  .center { text-align: center; margin-top: 3rem; }
</style>
