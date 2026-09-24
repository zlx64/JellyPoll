// Self-contained E2E runner.
//
// One command to go from a bare clone to a passing test run:
//   node run-e2e.mjs            # build plugin, (re)start Jellyfin, configure it, run tests
//   node run-e2e.mjs --fresh    # same, but wipe the stack + volume first (clean state)
//   node run-e2e.mjs --no-tests # set everything up but skip the test run
//   node run-e2e.mjs --skip-build  # reuse the existing ../publish output
//   node run-e2e.mjs --down     # tear the stack down (keeps the volume)
//
// All configuration comes from .env (see .env.example). docker compose reads the
// same file for the image, host port and the movies-folder bind mount.

import { spawnSync } from 'node:child_process';
import { readFileSync, existsSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const REPO_ROOT = path.resolve(__dirname, '..', '..');
const SRC_DIR = path.join(REPO_ROOT, 'src', 'Jellyfin.Plugin.JellyPoll');
const PUBLISH_DIR = path.join(REPO_ROOT, 'publish');
const CONTAINER = 'jellypoll-e2e';

// ---------- args ----------
const args = process.argv.slice(2);
const FRESH = args.includes('--fresh');
const SKIP_BUILD = args.includes('--skip-build');
const NO_TESTS = args.includes('--no-tests');
const DOWN_ONLY = args.includes('--down');

// ---------- env ----------
function loadEnv(file) {
  const out = {};
  if (!existsSync(file)) return out;
  for (const line of readFileSync(file, 'utf8').split(/\r?\n/)) {
    const m = line.match(/^\s*([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(.*)$/);
    if (!m) continue;
    let v = m[2].trim();
    if ((v.startsWith('"') && v.endsWith('"')) || (v.startsWith("'") && v.endsWith("'"))) v = v.slice(1, -1);
    out[m[1]] = v;
  }
  return out;
}
const env = { ...loadEnv(path.join(__dirname, '.env')), ...process.env };
const cfg = {
  image: env.JELLYFIN_IMAGE || 'jellyfin/jellyfin:12.1',
  port: env.JELLYFIN_HOST_PORT || '8096',
  serverName: env.SERVER_NAME || 'JellyPoll E2E',
  moviesFolder: env.MOVIES_FOLDER || '',
  adminUser: env.JELLYFIN_ADMIN_USER || 'admin',
  adminPassword: env.JELLYFIN_ADMIN_PASSWORD || '',
  pluginVersion: env.PLUGIN_VERSION || '',
};
const BASE = `http://localhost:${cfg.port}`;

const log = (m) => console.log(`\n\x1b[35m●\x1b[0m ${m}`);
const sub = (m) => console.log(`    ${m}`);

function run(cmd, a, opts = {}) {
  sub(`$ ${cmd} ${a.join(' ')}`);
  const r = spawnSync(cmd, a, { stdio: 'inherit', ...opts });
  if (r.error) throw r.error;
  if (r.status !== 0) throw new Error(`exit ${r.status}: ${cmd} ${a.join(' ')}`);
  return r;
}
const compose = (a) => run('docker', ['compose', '-f', 'docker-compose.yml', ...a], { cwd: __dirname });

// ---------- Jellyfin API ----------
const AUTH = 'MediaBrowser Client="JellyPollE2E", Device="e2e", DeviceId="jellypoll-e2e-01", Version="1.0"';
let TOKEN = '';
async function api(p, method = 'GET', body, useToken = true) {
  const headers = { Authorization: useToken && TOKEN ? `${AUTH}, Token=${TOKEN}` : AUTH };
  const opts = { method, headers };
  if (body) { headers['Content-Type'] = 'application/json'; opts.body = JSON.stringify(body); }
  const res = await fetch(BASE + p, opts);
  const text = await res.text();
  let json = null; try { json = JSON.parse(text); } catch {}
  return { status: res.status, json, text };
}
async function login() {
  const r = await api('/Users/AuthenticateByName', 'POST', { Username: cfg.adminUser, Pw: cfg.adminPassword }, false);
  if (r.status !== 200) throw new Error(`login failed (${r.status}): ${r.text.slice(0, 200)}`);
  TOKEN = r.json.AccessToken;
}
async function waitForHealth(timeoutMs = 120000) {
  const start = Date.now();
  while (Date.now() - start < timeoutMs) {
    try { if ((await fetch(BASE + '/health')).status === 200) return; } catch {}
    await new Promise((r) => setTimeout(r, 2500));
  }
  throw new Error('Jellyfin did not become healthy in time');
}
async function waitFor(desc, fn, timeoutMs, intervalMs = 4000) {
  const start = Date.now();
  while (Date.now() - start < timeoutMs) {
    try { if (await fn()) return; } catch {}
    await new Promise((r) => setTimeout(r, intervalMs));
  }
  throw new Error(`timed out: ${desc}`);
}
// The /health endpoint answers before the full API is ready, so retry transient
// 5xx/429 responses (e.g. right after a fresh container starts).
async function retryApi(desc, fn, tries = 20, delayMs = 2000) {
  let last;
  for (let i = 0; i < tries; i++) {
    last = await fn();
    if (last.status < 500 && last.status !== 429) return last;
    await new Promise((r) => setTimeout(r, delayMs));
  }
  throw new Error(`${desc} failed after ${tries} tries (${last.status}): ${last.text.slice(0, 120)}`);
}
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

if (DOWN_ONLY) {
  log('Tearing down the E2E stack (volume kept)...');
  compose(['down']);
  process.exit(0);
}

// ---------- validate ----------
if (!cfg.moviesFolder) { console.error('\n✗ MOVIES_FOLDER is not set. Copy .env.example to .env and set it to a folder of movies.'); process.exit(1); }
if (!existsSync(cfg.moviesFolder)) { console.error(`\n✗ MOVIES_FOLDER does not exist: ${cfg.moviesFolder}`); process.exit(1); }
if (!cfg.adminPassword || cfg.adminPassword === 'change-me') { console.error('\n✗ JELLYFIN_ADMIN_PASSWORD is not set (or still "change-me"). Set it in .env.'); process.exit(1); }

// ---------- plugin meta ----------
const meta = JSON.parse(readFileSync(path.join(SRC_DIR, 'meta.json'), 'utf8'));
const pluginVersion = cfg.pluginVersion || meta.version;
const pluginDir = `${meta.name}_${pluginVersion}`;

try {
  // ---------- 1. build ----------
  if (SKIP_BUILD) log('Skipping plugin build (--skip-build).');
  else {
    log('Building the plugin (dotnet publish)...');
    run('dotnet', ['publish', SRC_DIR, '-c', 'Release', '-o', PUBLISH_DIR, `-p:Version=${pluginVersion}`]);
  }
  if (!existsSync(path.join(PUBLISH_DIR, 'Jellyfin.Plugin.JellyPoll.dll'))) throw new Error('plugin DLL missing from publish/ — build failed?');

  // ---------- 2. (fresh) ----------
  if (FRESH) { log('Wiping stack + volume (--fresh)...'); compose(['down', '-v']); }

  // ---------- 3. up ----------
  log('Starting Jellyfin (docker compose up -d)...');
  compose(['up', '-d']);
  log('Waiting for the server to become healthy...');
  await waitForHealth(180000);
  await sleep(3000); // let the API settle before the first-time setup

  // ---------- 4. first-time setup ----------
  log('Checking first-time setup state...');
  const info = await api('/System/Info', 'GET', null, false);
  const wizardDone = info.status === 401 || info.json?.StartupWizardCompleted === true;
  if (wizardDone) {
    sub('Wizard already complete — verifying credentials...');
    try { await login(); }
    catch {
      console.error('\n✗ Login failed against the existing volume.');
      console.error('  The saved Jellyfin credentials differ from .env. Re-run with --fresh to wipe and re-create them, or fix JELLYFIN_ADMIN_USER / JELLYFIN_ADMIN_PASSWORD in .env.');
      process.exit(1);
    }
  } else {
    log('Completing the startup wizard (creating the admin account)...');
    const wiz = (desc, p, method, body) => retryApi(desc, () => api(p, method, body, false));
    await wiz('get first user', '/Startup/User', 'GET', null); // initialize + fetch the auto-created first user
    const su = await wiz('set first user', '/Startup/User', 'POST', { Name: cfg.adminUser, Password: cfg.adminPassword });
    if (su.status !== 204) throw new Error(`set first user failed (${su.status}): ${su.text.slice(0, 200)}`);
    const sc = await wiz('set configuration', '/Startup/Configuration', 'POST', { ServerName: cfg.serverName, UICulture: 'en', MetadataCountryCode: 'us', PreferredMetadataLanguage: 'en' });
    if (sc.status !== 204) throw new Error(`set configuration failed (${sc.status}): ${sc.text.slice(0, 200)}`);
    const comp = await wiz('complete wizard', '/Startup/Complete', 'POST', null);
    if (comp.status !== 204) throw new Error(`complete wizard failed (${comp.status}): ${comp.text.slice(0, 200)}`);
    await waitForHealth(60000);
    sub(`Admin "${cfg.adminUser}" created.`);
  }
  await login();

  // ---------- 5. movies library ----------
  log('Ensuring a "Movies" library on /media/movies...');
  const vfs = (await api('/Library/VirtualFolders')).json || [];
  const hasMovies = vfs.some((v) => v.CollectionType === 'movies' && (v.Locations || []).includes('/media/movies'));
  if (!hasMovies) {
    const qs = `?name=${encodeURIComponent('Movies')}&collectionType=movies&paths=${encodeURIComponent('/media/movies')}&refreshLibrary=true`;
    const add = await api('/Library/VirtualFolders' + qs, 'POST', {});
    if (add.status !== 204) throw new Error(`add library failed (${add.status}): ${add.text.slice(0, 200)}`);
    sub('Library created.');
  } else {
    sub('Library already present.');
  }
  await api('/Library/Refresh', 'POST', {});

  log('Waiting for at least 3 movies to be indexed...');
  let found = 0;
  await waitFor('3 movies in the library', async () => {
    const r = (await api('/Items?IncludeItemTypes=Movie&limit=3&fields=Name')).json;
    found = r?.Items?.length || 0;
    if (found >= 3) sub(`${found} movies available.`);
    return found >= 3;
  }, 300000, 5000);
  if (found < 3) throw new Error('fewer than 3 movies were indexed — the tests need at least 3 distinct movies in MOVIES_FOLDER.');

  // ---------- 6. install plugin ----------
  log(`Installing "${meta.name}" v${pluginVersion} into the container...`);
  run('docker', ['exec', CONTAINER, 'mkdir', '-p', `/config/plugins/${pluginDir}`]);
  run('docker', ['cp', `${PUBLISH_DIR}${path.sep}.`, `${CONTAINER}:/config/plugins/${pluginDir}/`]);
  // The publish output ships platform-native SQLite libs under runtimes/. Jellyfin's
  // PluginManager loads every *.dll in the plugin folder, so those native libs throw
  // BadImageFormatException and disable the plugin. Strip them (the provider bundles
  // its own native lib).
  run('docker', ['exec', CONTAINER, 'rm', '-rf', `/config/plugins/${pluginDir}/runtimes`]);
  log('Restarting Jellyfin to load the plugin...');
  run('docker', ['restart', CONTAINER]);
  await waitForHealth(180000);
  await login();
  await waitFor('the plugin to load', async () => (await api('/JellyPoll/Collections')).status === 200, 120000);
  sub('Plugin is loaded.');

  // ---------- 7. tests ----------
  if (NO_TESTS) { log('Setup complete (--no-tests). Not running the test suite.'); process.exit(0); }
  log('Running the E2E test suite...');
  const testEnv = { ...process.env, E2E_BASE: BASE, E2E_ADMIN_USER: cfg.adminUser, E2E_ADMIN_PASSWORD: cfg.adminPassword };
  const r = spawnSync('node', ['e2e.mjs'], { stdio: 'inherit', cwd: __dirname, env: testEnv });
  process.exit(r.status ?? 1);
} catch (e) {
  console.error(`\n\x1b[31m✗ Setup failed: ${e.message}\x1b[0m`);
  process.exit(1);
}
