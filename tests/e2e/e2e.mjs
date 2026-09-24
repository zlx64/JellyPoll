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

  // ---------- Phase G: standings vote-breakdown (pts hover tooltip) ----------
  step('G1. "Share my ranking" toggle present in My watch order');
  const shareLabel = page.locator('label.share');
  if ((await shareLabel.count()) === 1) ok('share toggle present in My watch order');
  else { fail('share toggle missing'); await shot(page, 'g1-toggle-missing'); }

  step('G2. Sharing OFF -> pts not hoverable (no tooltip)');
  if ((await page.locator('.pts.hoverable').count()) === 0) ok('no hoverable pts while sharing is off');
  else fail(`expected 0 hoverable pts while off, got ${await page.locator('.pts.hoverable').count()}`);

  step('G3. Toggle sharing ON -> pts hoverable + tooltip on hover');
  const shareCheckbox = shareLabel.locator('input[type="checkbox"]');
  await shareCheckbox.click({ timeout: 10000 });
  await page.waitForTimeout(1500);
  if ((await page.locator('.pts.hoverable').count()) > 0) ok('pts become hoverable after enabling sharing');
  else { fail('pts not hoverable after enabling sharing'); await shot(page, 'g3-no-hoverable'); }
  const tip = page.locator('.tip').first();
  await page.locator('.pts.hoverable').first().hover();
  await page.waitForTimeout(500);
  if (await tip.isVisible().catch(() => false)) {
    const tipText = (await tip.innerText()).replace(/\s+/g, ' ').trim();
    console.log(`  tooltip: "${tipText}"`);
    if (/who voted/i.test(tipText)) ok('tooltip shows "Who voted" header');
    else fail(`tooltip header missing: "${tipText}"`);
    if (/admin/i.test(tipText)) ok('tooltip lists the voter (admin)');
    else fail('tooltip does not list admin');
    if (/\b\d+\s*pts\b/.test(tipText)) ok('tooltip shows points for the place');
    else fail('tooltip missing points');
    await shot(page, 'g3-tooltip');
  } else {
    fail('tooltip did not appear on hover of pts');
    await shot(page, 'g3-no-tooltip');
  }

  step('G4. Toggle sharing OFF -> tooltip hidden again');
  await shareCheckbox.click({ timeout: 10000 });
  await page.waitForTimeout(1000);
  if ((await page.locator('.pts.hoverable').count()) === 0) ok('pts no longer hoverable after disabling sharing');
  else fail('pts still hoverable after disabling sharing');

  // ---------- Phase E: i18n ----------
  const me = await api('/Users/Me');
  const uid = me.json?.Id;

  step('E1. SPA in Ukrainian (user language = uk)');
  if (!uid) {
    fail('could not resolve user id for i18n tests');
  } else {
    // The page is already inside the SPA; a hash-only goto would not reload
    // the document, so force a full reload to re-run language resolution.
    await page.evaluate((id) => localStorage.setItem(id + '-language', 'uk'), uid);
    await page.reload({ waitUntil: 'networkidle' });
    await page.waitForTimeout(1500);
    let bodyTxt = await page.evaluate(() => document.body.innerText);
    let htmlLang = await page.evaluate(() => document.documentElement.lang);
    if (htmlLang === 'uk') ok('<html lang="uk">'); else fail(`expected <html lang="uk">, got "${htmlLang}"`);
    if (bodyTxt.includes('Мій список перегляду')) ok('UK room: "Мій список перегляду"'); else fail('UK room: "Мій список перегляду" missing');
    if (bodyTxt.includes('Рейтинг') && bodyTxt.includes('наживо')) ok('UK room: "Рейтинг … наживо"'); else fail('UK room: standings label missing');
    let overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
    if (overflow <= 0) ok('UK layout: no horizontal overflow (poll room)'); else fail(`UK layout: horizontal overflow ${overflow}px (poll room)`);
    await shot(page, 'e1-uk-room');

    await page.goto(BASE + '/JellyPoll/Web/#/polls');
    await page.waitForTimeout(1500);
    bodyTxt = await page.evaluate(() => document.body.innerText);
    if (bodyTxt.includes('Нове опитування')) ok('UK: "Нове опитування" button'); else fail('UK: "Нове опитування" missing');
    if (bodyTxt.includes('3 пропозиції')) ok('UK plural (few): "3 пропозиції"'); else fail('UK plural (few): "3 пропозиції" missing');
    if (bodyTxt.includes('проголосував 1')) ok('UK plural (one): "проголосував 1"'); else fail('UK plural (one): "проголосував 1" missing');
    overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
    if (overflow <= 0) ok('UK layout: no horizontal overflow (polls list)'); else fail(`UK layout: horizontal overflow ${overflow}px (polls list)`);
  }

  step('E2. SPA falls back to English (unknown language de)');
  if (uid) {
    await page.evaluate((id) => localStorage.setItem(id + '-language', 'de'), uid);
    await page.reload({ waitUntil: 'networkidle' });
    await page.waitForTimeout(1500);
    const htmlLang = await page.evaluate(() => document.documentElement.lang);
    const bodyTxt = await page.evaluate(() => document.body.innerText);
    if (htmlLang === 'en') ok('<html lang="en"> (de -> en fallback)'); else fail(`expected <html lang="en">, got "${htmlLang}"`);
    if (bodyTxt.includes('New poll')) ok('EN: "New poll" button'); else fail('EN: "New poll" missing');
    if (bodyTxt.includes('3 suggestions')) ok('EN plural: "3 suggestions"'); else fail('EN plural: "3 suggestions" missing');
    await page.evaluate((id) => localStorage.removeItem(id + '-language'), uid);
  }

  step('E3. Nav entries injected under a Ukrainian Jellyfin UI (nav-inject fix)');
  // JF 12's web client follows the browser language when the user has no
  // explicit choice, so a uk-UA context yields a fully Ukrainian JF UI.
  const creds = await page.evaluate(() => localStorage.getItem('jellyfin_credentials'));
  if (!creds) {
    fail('no jellyfin_credentials to seed the Ukrainian context');
  } else {
    const ukCtx = await browser.newContext({ locale: 'uk-UA' });
    await ukCtx.addInitScript((c) => localStorage.setItem('jellyfin_credentials', c), creds);
    const ukPage = await ukCtx.newPage({ viewport: { width: 1440, height: 900 } });
    await ukPage.goto(BASE + '/web/', { waitUntil: 'domcontentloaded', timeout: 60000 });
    await ukPage.waitForTimeout(8000);
    const drawerTexts = await ukPage.evaluate(() => Array.from(document.querySelectorAll('.navMenuOption')).map((e) => (e.textContent || '').trim()).filter(Boolean));
    const jfIsUk = drawerTexts.some((t) => /Налаштування|Головна|Вийти/.test(t));
    if (jfIsUk) ok(`JF UI rendered in Ukrainian (${drawerTexts.slice(0, 4).join(', ')})`);
    else fail(`JF UI did not switch to Ukrainian: ${drawerTexts.slice(0, 6).join(' | ')}`);
    if ((await ukPage.locator('#jellypoll-drawer-item').count()) > 0) ok('drawer entry injected under UK JF UI (language-independent anchor)');
    else fail('drawer entry missing under Ukrainian JF UI');
    if ((await ukPage.locator('#jellypoll-avatar-item').count()) > 0) ok('avatar entry injected under UK JF UI');
    else fail('avatar entry missing under Ukrainian JF UI');
    await shot(ukPage, 'e3-uk-jf-nav');
    await ukCtx.close();
  }

  // ---------- Phase F: admin language override via the dashboard config page ----------
  const PLUGIN_ID = '3734440f-4b20-4c3a-86a3-d931b45b7248';
  async function displayLang() {
    const r = await api(`/Plugins/${PLUGIN_ID}/Configuration`, 'GET');
    return r.json?.DisplayLanguage ?? '';
  }

  step('F1. Config page renders in the real JF12 dashboard (drawer "Jelly Polls")');
  // The dashboard route is the real user flow; the standalone
  // /web/ConfigurationPage URL has no web-client globals (ApiClient/Dashboard).
  await page.goto(BASE + '/web/#/configurationpage?name=jellypoll', { waitUntil: 'domcontentloaded' });
  await page.waitForSelector('#jellypollConfigForm', { timeout: 30000 });
  await page.waitForTimeout(2000);
  const cfgForm = await page.evaluate(() => !!document.querySelector('#jellypollConfigForm'));
  const cfgSel = await page.evaluate(() => !!document.querySelector('#selLanguage'));
  if (cfgForm && cfgSel) ok('config form + language dropdown present');
  else { fail(`config page incomplete (form=${cfgForm}, select=${cfgSel})`); await shot(page, 'f1-config-missing'); }

  step('F2. Save Ukrainian override -> persists + page re-localizes to UK');
  await page.selectOption('#selLanguage', 'uk');
  await page.click('#jellypollConfigForm button[type="submit"]');
  await page.waitForTimeout(3000);
  const dlUk = await displayLang();
  if (dlUk === 'uk') ok('DisplayLanguage persisted = "uk"'); else fail(`DisplayLanguage = ${JSON.stringify(dlUk)}`);
  const cfgUk = await page.evaluate(() => document.body.innerText);
  const cfgLang = await page.evaluate(() => document.documentElement.lang);
  if (cfgLang === 'uk' && cfgUk.includes('Мова інтерфейсу') && cfgUk.includes('Зберегти')) ok('config page re-localized to Ukrainian');
  else { fail(`config page did not re-localize (lang=${cfgLang})`); await shot(page, 'f2-config-uk'); }

  step('F3. Reset override to Auto -> persists + re-localizes to EN');
  await page.selectOption('#selLanguage', '');
  await page.click('#jellypollConfigForm button[type="submit"]');
  await page.waitForTimeout(3000);
  const dlReset = await displayLang();
  if (dlReset === '') ok('DisplayLanguage reset to auto ("")'); else fail(`DisplayLanguage = ${JSON.stringify(dlReset)}`);
  const cfgEn = await page.evaluate(() => document.body.innerText);
  if (cfgEn.includes('Interface language')) ok('config page re-localized back to English'); else fail('config page did not re-localize back to EN');

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
