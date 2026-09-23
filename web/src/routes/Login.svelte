<script lang="ts">
  import { login } from '../lib/auth.svelte';
  import { t, errorMessage } from '../lib/i18n.svelte';
  import Icon from '../components/Icon.svelte';

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
      error = errorMessage(e);
    } finally {
      busy = false;
    }
  }
</script>

<div class="wrap">
  <div class="card login">
    <div class="logo"><Icon name="how_to_vote" size={44} /></div>
    <h1>JellyPoll</h1>
    <div class="medals">
      <Icon name="trophy" size={22} class="gold" />
      <Icon name="trophy" size={18} class="silver" />
      <Icon name="trophy" size={16} class="bronze" />
    </div>
    <p class="dim">{t('login.subtitle')}</p>
    {#if error}<div class="error-box">{error}</div>{/if}
    <form onsubmit={(e) => { e.preventDefault(); submit(); }}>
      <input type="text" placeholder={t('login.usernamePlaceholder')} bind:value={username} autocomplete="username" />
      <input type="password" placeholder={t('login.passwordPlaceholder')} bind:value={password} autocomplete="current-password" />
      <button class="primary" type="submit" disabled={busy || !username || !password}>
        {busy ? t('login.signingIn') : t('login.signIn')}
      </button>
    </form>
  </div>
</div>

<style>
  .wrap { min-height: 100vh; display: grid; place-items: center; padding: 1rem; }
  .login {
    width: 100%; max-width: 340px; text-align: center;
    display: flex; flex-direction: column; gap: 0.7rem;
    box-shadow: var(--jp-shadow);
  }
  .logo { color: var(--jp-accent); display: flex; justify-content: center; margin-top: 0.4rem; }
  h1 { margin: 0; font-size: 1.5rem; }
  .medals { display: flex; justify-content: center; align-items: flex-end; gap: 0.4rem; }
  .medals :global(.gold) { color: var(--jp-gold); }
  .medals :global(.silver) { color: var(--jp-silver); }
  .medals :global(.bronze) { color: var(--jp-bronze); }
  form { display: flex; flex-direction: column; gap: 0.7rem; }
</style>
