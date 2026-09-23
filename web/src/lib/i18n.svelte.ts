// Lightweight i18n for the SPA (docs/localization-plan.md §4.3).
// Dictionaries are bundled at build time. Language resolution mirrors
// jellyfin-web: admin override -> localStorage["{userId}-language"]
// -> navigator.language -> base-language match -> "en".

import en from '../../../localization/en.json';
import uk from '../../../localization/uk.json';

type Params = Record<string, string | number>;
type Entry = string | Record<string, string>;
type Dict = Record<string, Record<string, Entry>>;

const dictionaries: Record<string, Dict> = { en: en as Dict, uk: uk as Dict };

let lang = $state('en');

let pluralRules: Intl.PluralRules | null = null;
let pluralRulesLang = '';

function pluralCategory(count: number): string {
  if (!pluralRules || pluralRulesLang !== lang) {
    pluralRules = new Intl.PluralRules(lang);
    pluralRulesLang = lang;
  }
  return pluralRules.select(count);
}

function normalize(value: string): string {
  return value.replace(/_/g, '-').toLowerCase();
}

function resolveLanguage(userId: string | null | undefined, override?: string): string {
  const candidates: string[] = [];
  if (override) candidates.push(override);
  if (userId) {
    try {
      const stored = localStorage.getItem(`${userId}-language`);
      if (stored) candidates.push(stored);
    } catch {
      /* storage unavailable */
    }
  }
  if (typeof navigator !== 'undefined' && navigator.language) candidates.push(navigator.language);
  for (const candidate of candidates) {
    const base = normalize(candidate).split('-')[0];
    if (dictionaries[base]) return base;
  }
  return 'en';
}

/** Runs the resolution chain and sets <html lang>. Safe to call repeatedly. */
export function initI18n(userId: string | null, override?: string): void {
  lang = resolveLanguage(userId, override);
  if (typeof document !== 'undefined') {
    document.documentElement.lang = lang;
  }
}

// Keys are two-level: "namespace.key" -> entry (string or plural map).
function lookup(key: string): Entry | undefined {
  const dot = key.indexOf('.');
  if (dot === -1) return undefined;
  const namespace = key.slice(0, dot);
  const name = key.slice(dot + 1);
  for (const dict of [dictionaries[lang] ?? dictionaries.en, dictionaries.en]) {
    const entry = dict[namespace]?.[name];
    if (entry !== undefined) return entry;
  }
  return undefined;
}

function fill(template: string, params: Params): string {
  return template.replace(/\{(\w+)\}/g, (match, name: string) =>
    params[name] !== undefined ? String(params[name]) : match
  );
}

/** Translate a key. Object values are plural maps selected via Intl.PluralRules. */
export function t(key: string, params?: Params): string {
  const entry = lookup(key);
  if (entry === undefined) return key;
  if (typeof entry === 'string') return fill(entry, params ?? {});
  const count = Number(params?.count ?? 0);
  const category = pluralCategory(count);
  const template = entry[category] ?? entry.other ?? Object.values(entry)[0] ?? key;
  return fill(template, { count, ...params });
}

/** Localized label for a Jellyfin item type; unknown types pass through. */
export function tType(type: string): string {
  const entry = lookup(`types.${type}`);
  return typeof entry === 'string' ? entry : type;
}

/** Localized message for an API error (by code), falling back to the server message. */
export function errorMessage(e: unknown): string {
  if (e instanceof Error) {
    const code = (e as { code?: string }).code;
    if (code) {
      const entry = lookup(`errors.${code}`);
      if (typeof entry === 'string') return entry;
    }
    return e.message;
  }
  return String(e);
}
