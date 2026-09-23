// Build-time i18n key-parity check (docs/localization-plan.md, phase 2).
// - every key in en.json must exist in uk.json (and vice versa) with the same value type
// - plural entries must define every CLDR category for their language
// - placeholders used by a key must match between languages
// Run automatically before `npm run build` in web/ (prebuild).

import { readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const load = (file) => JSON.parse(readFileSync(path.join(root, 'localization', file), 'utf8'));

const en = load('en.json');
const uk = load('uk.json');

const REQUIRED_CATEGORIES = {
  en: ['one', 'other'],
  uk: ['one', 'few', 'many', 'other']
};

const problems = [];

function isPluralMap(value) {
  return (
    value !== null &&
    typeof value === 'object' &&
    Object.keys(value).length > 0 &&
    Object.values(value).every((v) => typeof v === 'string')
  );
}

// Dictionaries are exactly two levels deep: namespace.key -> string | plural map.
function collect(dict, lang) {
  const out = {};
  for (const [ns, entries] of Object.entries(dict)) {
    if (entries === null || typeof entries !== 'object') {
      problems.push(`${lang}.json: "${ns}" is not a namespace object`);
      continue;
    }
    for (const [key, value] of Object.entries(entries)) {
      const full = `${ns}.${key}`;
      if (isPluralMap(value)) out[full] = 'plural';
      else if (typeof value === 'string') out[full] = 'string';
      else problems.push(`${lang}.json: ${full} has an unexpected value type`);
    }
  }
  return out;
}

const enKeys = collect(en, 'en');
const ukKeys = collect(uk, 'uk');

for (const [key, type] of Object.entries(enKeys)) {
  if (!(key in ukKeys)) problems.push(`missing in uk.json: ${key}`);
  else if (ukKeys[key] !== type) problems.push(`type mismatch for ${key}: en=${type}, uk=${ukKeys[key]}`);
}
for (const key of Object.keys(ukKeys)) {
  if (!(key in enKeys)) problems.push(`missing in en.json: ${key}`);
}

for (const [lang, dict] of Object.entries({ en, uk })) {
  const keys = Object.entries(collect(dict, lang)).filter(([, type]) => type === 'plural');
  for (const [key] of keys) {
    const map = key.split('.').reduce((o, p) => o[p], dict);
    for (const cat of REQUIRED_CATEGORIES[lang]) {
      if (!(cat in map)) problems.push(`${lang}.json ${key}: missing plural category "${cat}"`);
    }
  }
}

function placeholdersFor(key, dict) {
  const value = key.split('.').reduce((o, p) => o[p], dict);
  const templates = typeof value === 'string' ? [value] : Object.values(value);
  const found = new Set();
  for (const template of templates) {
    for (const m of template.matchAll(/\{(\w+)\}/g)) found.add(m[1]);
  }
  return [...found].sort();
}

for (const key of Object.keys(enKeys)) {
  if (!(key in ukKeys)) continue;
  const a = placeholdersFor(key, en);
  const b = placeholdersFor(key, uk);
  if (a.join(',') !== b.join(',')) {
    problems.push(`placeholder mismatch for ${key}: en=[${a}], uk=[${b}]`);
  }
}

if (problems.length > 0) {
  console.error('i18n parity check FAILED:');
  for (const p of problems) console.error('  - ' + p);
  process.exit(1);
}

console.log(`i18n parity OK: ${Object.keys(enKeys).length} keys present in en.json and uk.json`);
