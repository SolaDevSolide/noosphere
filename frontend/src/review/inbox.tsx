import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';
import { api } from '../api/client';
import type { Inbox } from '../api/types';
import { useAuth } from '../auth/auth';

interface InboxState {
  inbox: Inbox | null;
  refresh: () => void;
}
const Ctx = createContext<InboxState>({ inbox: null, refresh: () => {} });
const EVERY_MS = 60_000;

/** What waits for the signed-in user: proposals to validate and their own open proposals. Polled every minute. */
export function InboxProvider({ children }: { children: ReactNode }) {
  const { me } = useAuth();
  const [inbox, setInbox] = useState<Inbox | null>(null);
  const [tick, setTick] = useState(0);
  const refresh = useCallback(() => setTick((x) => x + 1), []);
  const active = !!me && me.role !== 'guest';
  useEffect(() => {
    if (!active) {
      setInbox(null);
      return;
    }
    let alive = true;
    api
      .inbox()
      .then((i) => alive && setInbox(i))
      .catch(() => {});
    return () => {
      alive = false;
    };
  }, [active, tick]);
  useEffect(() => {
    if (!active) return;
    const id = window.setInterval(() => {
      if (document.visibilityState === 'visible') refresh();
    }, EVERY_MS);
    const onFocus = () => refresh();
    window.addEventListener('focus', onFocus);
    return () => {
      window.clearInterval(id);
      window.removeEventListener('focus', onFocus);
    };
  }, [active, refresh]);
  const value = useMemo(() => ({ inbox, refresh }), [inbox, refresh]);
  return <Ctx.Provider value={value}>{children}</Ctx.Provider>;
}

export const useInbox = () => useContext(Ctx);
