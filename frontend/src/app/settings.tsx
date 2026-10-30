import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';
import { api } from '../api/client';
import type { Settings } from '../api/types';

export const DEFAULT_SETTINGS: Settings = { instanceName: 'Noosphere', organisationName: '', defaultLanguage: 'en' };

interface SettingsState {
  settings: Settings;
  /** False until the first answer (or failure); the header keeps the default name meanwhile. */
  loaded: boolean;
  setSettings: (s: Settings) => void;
}

const Ctx = createContext<SettingsState>({ settings: DEFAULT_SETTINGS, loaded: false, setSettings: () => {} });

/** Loads the instance settings once; they are readable without signing in, so the login page shows the name too. */
export function SettingsProvider({ children }: { children: ReactNode }) {
  const [settings, set] = useState<Settings>(DEFAULT_SETTINGS);
  const [loaded, setLoaded] = useState(false);
  useEffect(() => {
    let alive = true;
    api
      .settings()
      .then((s) => alive && set({ ...DEFAULT_SETTINGS, ...s }))
      .catch(() => {})
      .finally(() => alive && setLoaded(true));
    return () => {
      alive = false;
    };
  }, []);
  const setSettings = useCallback((s: Settings) => set({ ...DEFAULT_SETTINGS, ...s }), []);
  const value = useMemo(() => ({ settings, loaded, setSettings }), [settings, loaded, setSettings]);
  return <Ctx.Provider value={value}>{children}</Ctx.Provider>;
}

export const useSettings = () => useContext(Ctx);

/** The browser tab title: "<page> · <instance name>", or the instance name alone. */
export function documentTitle(instanceName: string, page?: string) {
  const name = instanceName.trim() || DEFAULT_SETTINGS.instanceName;
  return page ? `${page} · ${name}` : name;
}

export function useDocumentTitle(page?: string) {
  const { settings } = useSettings();
  useEffect(() => {
    document.title = documentTitle(settings.instanceName, page);
  }, [settings.instanceName, page]);
}
