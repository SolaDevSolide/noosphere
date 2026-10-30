import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';
import type { Language } from '../api/types';
import { en, type Dict } from './en';
import { fr } from './fr';

export const DICTS: Record<Language, Dict> = { en, fr };
const KEY = 'noos-lang';

/** The browser language when it is French, English otherwise. */
export function browserLanguage(): Language {
  const langs = typeof navigator === 'undefined' ? [] : (navigator.languages ?? [navigator.language]);
  for (const l of langs) {
    const p = l?.slice(0, 2).toLowerCase();
    if (p === 'fr') return 'fr';
    if (p === 'en') return 'en';
  }
  return 'en';
}

function storedLanguage(): Language | null {
  try {
    const v = localStorage.getItem(KEY);
    return v === 'en' || v === 'fr' ? v : null;
  } catch {
    return null;
  }
}

interface I18n {
  t: Dict;
  lang: Language;
  setLang: (l: Language) => void;
  /** Locale-aware date formatting. */
  date: (iso: string | null | undefined) => string;
}

const Ctx = createContext<I18n | null>(null);

export function I18nProvider({ children, initial }: { children: ReactNode; initial?: Language }) {
  const [lang, setLangState] = useState<Language>(() => initial ?? storedLanguage() ?? browserLanguage());
  const setLang = useCallback((l: Language) => {
    setLangState(l);
    try {
      localStorage.setItem(KEY, l);
    } catch {
      /* private mode */
    }
  }, []);
  useEffect(() => {
    document.documentElement.lang = lang;
  }, [lang]);
  const value = useMemo<I18n>(() => {
    const fmt = new Intl.DateTimeFormat(lang === 'fr' ? 'fr-FR' : 'en-GB', { dateStyle: 'medium', timeStyle: 'short' });
    return {
      t: DICTS[lang],
      lang,
      setLang,
      date: (iso) => {
        if (!iso) return '—';
        const d = new Date(iso);
        return Number.isNaN(d.getTime()) ? iso : fmt.format(d);
      },
    };
  }, [lang, setLang]);
  return <Ctx.Provider value={value}>{children}</Ctx.Provider>;
}

export function useI18n(): I18n {
  const v = useContext(Ctx);
  if (!v) throw new Error('useI18n outside I18nProvider');
  return v;
}
export const useT = () => useI18n().t;
