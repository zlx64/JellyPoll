<script lang="ts">
  import { login } from '../lib/auth.svelte';

  let username = $state('');
  let password = $state('');
  let busy = $state(false);
  let error = $state('');

  async function submit() {
    busy = true;
    error = '';
    try {
      await login(username, password);
    } catch (e) {
      error = e instanceof Error ? e.message : String(e);
    } finally {
      busy = false;
    }
  }
</script>

<div class="wrap">
  <div class="card login">
    <div class="medals">🥇🥈🥉</div>
    <h1>JellyPoll</h1>
    <p class="dim">Sign in with your Jellyfin account</p>
    {#if error}<div class="error-box">{error}</div>{/if}
    <form onsubmit={(e) => { e.preventDefault(); submit(); }}>
      <input type="text" placeholder="Username" bind:value={username} autocomplete="username" />
      <input type="password" placeholder="Password" bind:value={password} autocomplete="current-password" />
      <button class="primary" type="submit" disabled={busy || !username || !password}>
        {busy ? 'Signing in…' : 'Sign in'}
      </button>
    </form>
  </div>
</div>

<style>
  .wrap { min-height: 100vh; display: grid; place-items: center; padding: 1rem; }
  .login { width: 100%; max-width: 340px; text-align: center; display: flex; flex-direction: column; gap: 0.8rem; }
  h1 { margin: 0; }
  .medals { font-size: 2rem; letter-spacing: 0.5rem; }
  form { display: flex; flex-direction: column; gap: 0.7rem; }
</style>
