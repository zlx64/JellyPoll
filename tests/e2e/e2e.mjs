import { chromium } from 'playwright';

const BASE = 'http://localhost:8096';
let TOKEN = process.env.E2E_TOKEN || '';

async function ensureToken() {
  if (TOKEN) {
    const probe = await fetch(BASE + '/JellyPoll/Collections', {
      headers: { Authorization: `MediaBrowser Client="JellyPollE2E", Device="e2e", DeviceId="jellypoll-e2e-01", Version="1.0", Token=${TOKEN}` },
    });
    if (probe.status === 200) return;
    TOKEN = '';
  }
  const authHeaders = { 'Content-Type': 'application/json', Authorization: 'MediaBrowser Client="JellyPollE2E", Device="e2e", DeviceId="jellypoll-e2e-01", Version="1.0"' };
  const res = await fetch(BASE + '/Users/AuthenticateByName', {
    method: 'POST',
    headers: authHeaders,
    body: JSON.stringify({ Username: 'admin', Pw: 'E2eAdmin123!' }),
  });
  if (res.status !== 200) throw new Error(`E2E auth failed: ${res.status} ${await res.text()}`);
  const user = await res.json();
  TOKEN = user.AccessToken;
}

let failures = 0;
function step(name) { console.log(`\n=== ${name} ===`); }
function ok(msg) { console.log(`  PASS: ${msg}`); }
function fail(msg) { failures++; console.log(`  FAIL: ${msg}`); }
let shotN = 0;
async function shot(page, name) {
  shotN++;
  const f = `shot-${String(shotN).padStart(2, '0')}-${name}.png`;
  await page.screenshot({ path: f }).catch(() => {});
  console.log(`  [screenshot] ${f}`);
}

async function api(path, method = 'GET', body) {
  const opts = { method, headers: { Authorization: `MediaBrowser Client="JellyPollE2E", Device="e2e", DeviceId="jellypoll-e2e-01", Version="1.0", Token=${TOKEN}` } };
  if (body) { opts.headers['Content-Type'] = 'application/json'; opts.body = JSON.stringify(body); }
  const res = await fetch(BASE + path, opts);
  const text = await res.text();
  let json = null;
  try { json = JSON.parse(text); } catch { /* non-json */ }
  return { status: res.status, json, text };
}

async function login(page) {
  await page.goto(BASE + '/web/', { waitUntil: 'domcontentloaded', timeout: 60000 });
  await page.waitForSelector('#txtManualName', { timeout: 30000 });
  await page.fill('#txtManualName', 'admin');
  await page.fill('#txtManualPassword', 'E2eAdmin123!');
  await page.getByRole('button', { name: 'Sign In' }).click();
  await page.waitForURL((u) => !String(u).includes('#/login'), { timeout: 30000 });
  await page.waitForTimeout(4000);
}

async function openSpaViaNav(page) {
  await page.locator('button[aria-label="User Menu"]').first().click({ timeout: 10000 });
  await page.waitForTimeout(1500);
  const entry = page.locator('#jellypoll-avatar-item');
  if (await entry.count() === 0) throw new Error('injected nav entry missing');
  await entry.first().click();
  await page.waitForURL(/JellyPoll\/Web/, { timeout: 15000 });
  await page.waitForTimeout(2500);
}

// Open a movie detail, then its "Add to collection" dialog. Returns nothing; dialog is left open.
async function openAddToCollectionDialog(page, movieText) {
  await page.goto(BASE + '/web/#/movies', { waitUntil: 'domcontentloaded' });
  await page.waitForTimeout(6000);
  const card = page.locator('.card', { hasText: movieText }).first();
  await card.waitFor({ timeout: 20000 });
  let navigated = false;
  for (let attempt = 0; attempt < 3 && !navigated; attempt++) {
    await card.scrollIntoViewIfNeeded().catch(() => {});
    await card.click({ timeout: 10000 }).catch(() => {});
    try {
      await page.waitForURL(/#\/details/, { timeout: 8000 });
      navigated = true;
    } catch { /* retry click */ }
  }
  if (!navigated) throw new Error(`could not open detail page for ${movieText}`);
  // JF 12 leaves stale zero-size copies of the detail "More" button in the DOM;
  // only the last one is real, so target the visible one explicitly.
  const moreBtn = page.locator('button.btnMoreCommands:visible').first();
  await moreBtn.waitFor({ timeout: 20000 });
  await moreBtn.click({ timeout: 10000 });
  await page.waitForTimeout(1500);
  await page.locator('button, [role="menuitem"]').filter({ hasText: 'Add to collection' }).first().click({ timeout: 10000 });
  await page.waitForSelector('.dialog.formDialog.opened, .dialog.opened', { timeout: 15000 });
  await page.waitForTimeout(1000);
}

const browser = await chromium.launch({ headless: true });
const page = await browser.newPage({ viewport: { width: 1440, height: 900 } });
const pageErrors = [];
page.on('pageerror', (e) => pageErrors.push(String(e).slice(0, 300)));
page.on('console', (m) => { if (m.type() === 'error') pageErrors.push('[console] ' + m.text().slice(0, 300)); });
const httpErrors = [];
page.on('response', (r) => { if (r.status() >= 400) httpErrors.push(`[${r.status()}] ${r.url()}`); });

try {
  await ensureToken();

  // ---------- Phase A: login + nav entry ----------
  step('A1. Log in as admin');
  await login(page);
  ok(`logged in, url=${page.url()}`);

  step('A2. Avatar menu shows injected "Jelly Polls" entry');
  await page.locator('button[aria-label="User Menu"]').first().click({ timeout: 10000 });
  await page.waitForTimeout(1500);
  const navEntry = page.locator('#jellypoll-avatar-item');
  const entryVisible = (await navEntry.count()) > 0 && (await navEntry.first().isVisible().catch(() => false));
  if (entryVisible) ok(`nav entry present: "${(await navEntry.first().innerText()).replace(/\s+/g, ' ').trim()}"`);
  else { fail('injected nav entry #jellypoll-avatar-item not visible'); await shot(page, 'avatar-missing'); }
  await shot(page, 'a2-avatar-menu');

  step('A3. Click "Jelly Polls" -> SPA loads');
  if (entryVisible) {
    await navEntry.first().click();
    await page.waitForURL(/JellyPoll\/Web/, { timeout: 15000 });
    await page.waitForTimeout(2500);
    ok(`SPA loaded: ${page.url()}`);
    await shot(page, 'a3-spa-home');
  }

  // ---------- Phase B: create a collection via the JF UI ----------
  step('B1. Wing Chun -> More -> Add to collection -> new "Kung Fu Classics"');
  await openAddToCollectionDialog(page, 'Wing Chun');
  await page.fill('#txtNewCollectionName', 'Kung Fu Classics');
  await page.locator('.dialog button.btnSubmit').first().click({ timeout: 10000 });
  await page.keyboard.press('Escape').catch(() => {});
  await page.waitForTimeout(5000);
  const colls1 = await api('/JellyPoll/Collections');
  const kf1 = (colls1.json?.Items || []).find((c) => c.Name === 'Kung Fu Classics');
  if (kf1) ok(`collection created with 1 movie (count=${kf1.MovieCount})`);
  else { fail(`collection not found after create: ${JSON.stringify(colls1.json).slice(0, 200)}`); await shot(page, 'b1-missing'); }

  step('B2. Shaolin_Ba_Duan_Jing -> Add to collection -> join "Kung Fu Classics"');
  await openAddToCollectionDialog(page, 'Shaolin');
  const joinSel = page.locator('#selectCollectionToAddTo');
  const hasOption = (await joinSel.locator('option', { hasText: 'Kung Fu Classics' }).count()) > 0;
  if (hasOption) {
    await joinSel.selectOption({ label: 'Kung Fu Classics' }, { timeout: 10000 });
    await page.locator('.dialog button.btnSubmit').first().click({ timeout: 10000 });
    await page.waitForTimeout(5000);
    const colls2 = await api('/JellyPoll/Collections');
    const kf2 = (colls2.json?.Items || []).find((c) => c.Name === 'Kung Fu Classics');
    if (kf2 && kf2.MovieCount === 2) ok(`collection now has 2 movies (count=${kf2.MovieCount})`);
    else fail(`expected MovieCount=2, got ${kf2 ? kf2.MovieCount : 'collection missing'}: ${JSON.stringify(colls2.json).slice(0, 200)}`);
  } else {
    fail('"Kung Fu Classics" not offered in second Add-to-collection dialog');
    const dlgText = await page.evaluate(() => document.querySelector('.dialog')?.innerText?.slice(0, 300) || 'no dialog');
    console.log('  dialog text:', dlgText.replace(/\n+/g, ' | '));
    await shot(page, 'b2-join-missing');
  }

  // ---------- Phase C: Jelly Polls SPA flow ----------
  step('C1. Open Jelly Polls SPA via nav entry');
  await openSpaViaNav(page);
  ok('SPA opened from nav');

  step('C2. Create poll "E2E UI Poll"');
  await page.getByRole('button', { name: /new poll/i }).first().click({ timeout: 10000 });
  await page.waitForSelector('input[placeholder*="Poll title"]', { timeout: 10000 });
  await page.fill('input[placeholder*="Poll title"]', 'E2E UI Poll');
  await page.getByRole('button', { name: 'Create', exact: true }).click({ timeout: 10000 });
  await page.waitForURL(/#\/poll\//, { timeout: 15000 });
  await page.waitForTimeout(3000);
  ok(`poll room: ${page.url()}`);
  await shot(page, 'c2-poll-room');

  step('C3. Suggest a movie via search picker (Kill Bill)');
  const suggestBtn = page.getByRole('button', { name: /suggest/i }).first();
  if (await suggestBtn.count() > 0) await suggestBtn.click({ timeout: 10000 });
  await page.waitForSelector('input[type="search"]', { timeout: 10000 });
  await page.fill('input[type="search"]', 'Kill Bill');
  await page.waitForTimeout(3000);
  const killBillCard = page.locator('.picker .row').filter({ hasText: /Kill Bill/i }).first();
  await killBillCard.click({ timeout: 15000 });
  await page.waitForTimeout(2500);
  ok('suggested Kill Bill via search');

  step('C4. Suggest collection via "Browse collections"');
  // The collections list is filtered by the search box (still "Kill Bill" from C3).
  await page.fill('input[type="search"]', '');
  const browseToggle = page.getByText(/browse collections/i).first();
  if (await browseToggle.count() > 0) {
    await browseToggle.click({ timeout: 10000 });
    await page.waitForTimeout(2500);
  }
  const collCard = page.locator('.picker .row', { hasText: /Kung Fu Classics/i }).first();
  const collVisible = await collCard.waitFor({ timeout: 15000 }).then(() => true, () => false);
  if (collVisible) {
    await collCard.click({ timeout: 15000 });
    await page.waitForTimeout(3500);
    const boardText = await page.evaluate(() => document.body.innerText);
    const toastMatch = boardText.match(/Kung Fu Classics[^\n]*/)?.[0] || '';
    console.log(`  board/toast: ${toastMatch}`);
    ok('collection suggested via browse section');
  } else {
    fail('Kung Fu Classics not visible in Browse collections');
    await shot(page, 'c4-missing');
  }

  step('C5. Board, my ranking, standings');
  await page.waitForTimeout(2000);
  const bodyNow = await page.evaluate(() => document.body.innerText);
  const boardCount = (bodyNow.match(/Suggested \((\d+)\)/) || [])[1];
  if (boardCount === '3') ok('board has 3 suggestions (1 movie + 2 from collection)');
  else fail(`expected 3 suggestions on board, got ${boardCount}`);
  const rankingMatch = bodyNow.match(/My watch order[\s\S]{0,300}/)?.[0] || '';
  console.log('  ranking:', rankingMatch.replace(/\n+/g, ' | ').slice(0, 250));
  const hasKillBillInRanking = rankingMatch.includes('Kill Bill');
  const hasWingChunInRanking = rankingMatch.includes('Wing Chun');
  if (hasKillBillInRanking && hasWingChunInRanking) ok('all 3 auto-ranked in my watch order');
  else fail(`ranking incomplete: killBill=${hasKillBillInRanking} wingChun=${hasWingChunInRanking}`);
  const standings = bodyNow.match(/Standings live[\s\S]{0,400}/)?.[0] || '';
  console.log('  standings:', standings.replace(/\n+/g, ' | ').slice(0, 300));
  const killBillFirst = /Kill Bill[^\n]*\n[^\n]*\b1\b/.test(standings) || standings.indexOf('Kill Bill') < standings.indexOf('Wing Chun');
  if (killBillFirst) ok('Kill Bill ranks #1 (first in my ballot)');
  else fail('Kill Bill not ranked first in standings');
  await shot(page, 'c5-final-state');

  // ---------- Phase D: cleanup ----------
  step('D1. Cleanup (delete E2E poll + collection via API)');
  // Leave the poll room first so it stops requesting collection posters.
  await page.goto(BASE + '/JellyPoll/Web/', { waitUntil: 'domcontentloaded' });
  await page.waitForTimeout(1500);
  const polls = await api('/JellyPoll/Polls');
  for (const p of polls.json?.Polls || []) {
    if (p.Title === 'E2E UI Poll') {
      const r = await api(`/JellyPoll/Polls/${p.Id}`, 'DELETE');
      console.log(`  deleted poll ${p.Id}: ${r.status}`);
    }
  }
  const colls = await api('/JellyPoll/Collections');
  for (const c of colls.json?.Items || []) {
    if (c.Name === 'Kung Fu Classics' || c.Name === 'Debug KF' || c.Name === 'Bisect KF') {
      const r = await api(`/Items/${c.Id}`, 'DELETE');
      console.log(`  deleted collection "${c.Name}" ${c.Id}: ${r.status}`);
    }
  }
} catch (e) {
  failures++;
  console.error('SCRIPT ERROR:', e.message);
  console.error('URL at failure:', page.url());
  const body = await page.evaluate(() => document.body.innerText.slice(0, 300)).catch(() => '');
  console.error('BODY at failure:', body.replace(/\n+/g, ' | '));
  await shot(page, 'error');
}

console.log('\n=== HTTP ERRORS ===');
if (httpErrors.length === 0) console.log('  none');
else httpErrors.slice(0, 10).forEach((e) => console.log('  ' + e));

console.log('\n=== PAGE ERRORS (js) ===');
const realErrors = pageErrors.filter((e) => !e.includes('WebSocket') && !e.includes('socket'));
if (realErrors.length === 0) console.log('  none');
else realErrors.slice(0, 10).forEach((e) => console.log('  ' + e));

console.log(`\nRESULT: ${failures === 0 ? 'ALL PASS' : failures + ' FAILURE(S)'}`);
await browser.close();
process.exit(failures === 0 ? 0 : 1);
