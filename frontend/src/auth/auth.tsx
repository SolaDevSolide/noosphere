import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';
import { Navigate, useLocation } from 'react-router-dom';
import { api, ApiError, setUnauthorizedHandler } from '../api/client';
import type { Language, MeM2 as Me } from '../api/types';
import { useI18n } from '../i18n';

type Status = 'loading' | 'anonymous' | 'signed-in';

interface Auth {
  status: Status;
  me: Me | null;
  canEdit: boolean;
  isAdmin: boolean;
  /** Ids of the teams the user belongs to. */
  teamIds: string[];
  login: (username: string, password: string) => Promise<void>;
  logout: () => Promise<void>;
  changeLanguage: (l: Language) => void;
  /** Takes over another account's profile (the demo toolbox signs in as another persona). */
  adopt: (m: Me) => void;
}

const Ctx = createContext<Auth | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const { setLang } = useI18n();
  const [me, setMe] = useState<Me | null>(null);
  const [status, setStatus] = useState<Status>('loading');

  useEffect(() => {
    setUnauthorizedHandler(() => {
      setMe(null);
      setStatus('anonymous');
    });
    let alive = true;
    api
      .me()
      .then((m) => {
        if (!alive) return;
        setMe(m);
        setLang(m.language);
        setStatus('signed-in');
      })
      .catch(() => {
        if (alive) setStatus('anonymous');
      });
    return () => {
      alive = false;
    };
  }, [setLang]);

  const login = useCallback(
    async (username: string, password: string) => {
      const m = await api.login(username, password);
      // The login answer may be minimal: read the profile to get the language.
      const full = m?.language ? m : await api.me();
      setMe(full);
      setLang(full.language);
      setStatus('signed-in');
    },
    [setLang],
  );

  const logout = useCallback(async () => {
    try {
      await api.logout();
    } catch (e) {
      if (!(e instanceof ApiError)) throw e;
    }
    setMe(null);
    setStatus('anonymous');
  }, []);

  const changeLanguage = useCallback(
    (l: Language) => {
      setLang(l);
      setMe((m) => (m ? { ...m, language: l } : m));
      if (me) api.setLanguage(l).catch(() => {});
    },
    [me, setLang],
  );

  const adopt = useCallback(
    (m: Me) => {
      setMe(m);
      setLang(m.language);
      setStatus('signed-in');
    },
    [setLang],
  );

  const value = useMemo<Auth>(
    () => ({
      status,
      me,
      canEdit: me?.role === 'admin' || me?.role === 'member',
      isAdmin: me?.role === 'admin',
      teamIds: me?.teams?.map((x) => x.id) ?? [],
      login,
      logout,
      changeLanguage,
      adopt,
    }),
    [status, me, login, logout, changeLanguage, adopt],
  );
  return <Ctx.Provider value={value}>{children}</Ctx.Provider>;
}

export function useAuth(): Auth {
  const v = useContext(Ctx);
  if (!v) throw new Error('useAuth outside AuthProvider');
  return v;
}

/** Renders its children once signed in; sends anonymous visitors to the login page. */
export function RequireAuth({ children, fallback }: { children: ReactNode; fallback: ReactNode }) {
  const { status } = useAuth();
  const loc = useLocation();
  if (status === 'loading') return <>{fallback}</>;
  if (status === 'anonymous') {
    const next = loc.pathname + loc.search;
    return <Navigate to={next === '/' ? '/login' : `/login?next=${encodeURIComponent(next)}`} replace />;
  }
  return <>{children}</>;
}
