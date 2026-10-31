import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';
import { api, ApiError } from '../api/client';
import type { DemoNotice, DemoState, Me } from '../api/types';
import { useAuth } from '../auth/auth';
import { useI18n } from '../i18n';

/** The look of a notice: its icon and colour. */
export type NoticeTone = 'ok' | 'wr' | 'dg' | 'ag' | '';
export type NoticeIcon = 'user' | 'users' | 'review' | 'check' | 'x' | 'eye' | 'bot' | 'lock' | 'gauge' | 'reset';

export interface Notice extends DemoNotice {
  id: number;
  tone: NoticeTone;
  icon: NoticeIcon;
}

interface Demo {
  /** Demo mode is on (the server answers /api/demo/state). */
  on: boolean;
  state: DemoState | null;
  /** Bumps after every demo action, so pages that list agents or proposals load again. */
  tick: number;
  /** Bumps when the signed-in pages must load again from scratch (another persona, a reset). */
  epoch: number;
  open: boolean;
  setOpen: (open: boolean) => void;
  stay: boolean;
  setStay: (stay: boolean) => void;
  notices: Notice[];
  notify: (n: DemoNotice, tone: NoticeTone, icon: NoticeIcon) => void;
  dismiss: (id: number) => void;
  /** Called after an action changed data; `remount` reloads every page. */
  changed: (remount?: boolean) => void;
  switchTo: (username: string) => Promise<void>;
  refresh: () => void;
}

const NOOP_DEMO: Demo = {
  on: false,
  state: null,
  tick: 0,
  epoch: 0,
  open: false,
  setOpen: () => {},
  stay: true,
  setStay: () => {},
  notices: [],
  notify: () => {},
  dismiss: () => {},
  changed: () => {},
  switchTo: async () => {},
  refresh: () => {},
};

const Ctx = createContext<Demo>(NOOP_DEMO);
export const useDemo = () => useContext(Ctx);

const STAY_KEY = 'noos-demo-stay';
function storedStay(): boolean {
  try {
    return localStorage.getItem(STAY_KEY) !== '0';
  } catch {
    return true;
  }
}

/**
 * Whether `me` may open the page at `path`. Records are checked against the API as that person: a restricted record
 * they cannot see sends them home.
 */
export async function canSee(path: string, me: Me): Promise<boolean> {
  const guest = me.role === 'guest';
  if (path.startsWith('/admin')) return me.role === 'admin';
  if (path === '/my-agents') return !guest;
  if (path === '/review') return !guest;
  const m = /^\/(review|applications|flows|pages|teams|domains|data-objects)\/([^/]+)/.exec(path);
  if (!m) return true;
  const id = decodeURIComponent(m[2]);
  if (m[1] === 'review' && guest) return false;
  if (id === 'new') return !guest;
  try {
    const rec: unknown =
      m[1] === 'review'
        ? await api.proposal(id)
        : m[1] === 'applications'
          ? await api.application(id)
          : m[1] === 'flows'
            ? await api.flow(id)
            : m[1] === 'pages'
              ? await api.page(id)
              : m[1] === 'teams'
                ? await api.team(id)
                : m[1] === 'domains'
                  ? await api.domain(id)
                  : await api.dataObject(id);
    return !(rec && typeof rec === 'object' && (rec as { restricted?: boolean }).restricted === true);
  } catch (e) {
    if (e instanceof ApiError && e.status === 0) return true;
    return false;
  }
}

let seq = 0;

/** The demo toolbox state: loaded once signed in; absent (everything hidden) when the server is not in demo mode. */
export function DemoProvider({ children }: { children: ReactNode }) {
  const { t } = useI18n();
  const { status, me, adopt } = useAuth();
  const navigate = useNavigate();
  const loc = useLocation();
  const [state, setState] = useState<DemoState | null>(null);
  const [on, setOn] = useState(false);
  const [tick, setTick] = useState(0);
  const [epoch, setEpoch] = useState(0);
  const [open, setOpen] = useState(false);
  const [stay, setStayState] = useState(storedStay);
  const [notices, setNotices] = useState<Notice[]>([]);
  const timers = useRef(new Map<number, ReturnType<typeof setTimeout>>());
  const [reload, setReload] = useState(0);

  useEffect(() => {
    if (status !== 'signed-in') return;
    let alive = true;
    api
      .demoState()
      .then((s) => {
        if (!alive) return;
        setState(s);
        setOn(true);
      })
      .catch(() => alive && setOn(false));
    return () => {
      alive = false;
    };
  }, [status, me?.username, reload]);

  useEffect(() => {
    const all = timers.current;
    return () => all.forEach((x) => clearTimeout(x));
  }, []);

  const dismiss = useCallback((id: number) => {
    setNotices((ns) => ns.filter((n) => n.id !== id));
    clearTimeout(timers.current.get(id));
    timers.current.delete(id);
  }, []);

  const notify = useCallback(
    (n: DemoNotice, tone: NoticeTone, icon: NoticeIcon) => {
      const id = ++seq;
      setNotices((ns) => [{ ...n, id, tone, icon }, ...ns].slice(0, 3));
      timers.current.set(
        id,
        setTimeout(() => dismiss(id), 9000),
      );
    },
    [dismiss],
  );

  const setStay = useCallback((v: boolean) => {
    setStayState(v);
    try {
      localStorage.setItem(STAY_KEY, v ? '1' : '0');
    } catch {
      /* private mode */
    }
  }, []);

  const changed = useCallback((remount?: boolean) => {
    setTick((x) => x + 1);
    if (remount) setEpoch((x) => x + 1);
  }, []);

  const switchTo = useCallback(
    async (username: string) => {
      const r = await api.demoSwitch(username);
      adopt(r.me);
      const here = loc.pathname;
      let back = false;
      if (!stay) void navigate('/');
      else if (!(await canSee(here, r.me))) {
        back = true;
        void navigate('/');
      }
      setEpoch((x) => x + 1);
      setTick((x) => x + 1);
      notify(
        back
          ? {
              title: t.agents.demo.switched(r.me.displayName),
              detail: t.agents.demo.cannotSee(r.me.displayName),
              link: null,
            }
          : { ...r.notice, title: t.agents.demo.switched(r.me.displayName) },
        'ok',
        'user',
      );
    },
    [adopt, loc.pathname, stay, navigate, notify, t],
  );

  const value = useMemo<Demo>(
    () => ({
      on: on && status === 'signed-in',
      state,
      tick,
      epoch,
      open,
      setOpen,
      stay,
      setStay,
      notices,
      notify,
      dismiss,
      changed,
      switchTo,
      refresh: () => setReload((x) => x + 1),
    }),
    [on, status, state, tick, epoch, open, stay, setStay, notices, notify, dismiss, changed, switchTo],
  );
  return <Ctx.Provider value={value}>{children}</Ctx.Provider>;
}
