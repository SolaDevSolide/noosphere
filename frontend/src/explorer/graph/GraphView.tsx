import { useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { createPortal } from 'react-dom';
import { isRestrictedApp, isRestrictedFlow } from '../../api/types';
import { EMPTY_FILTERS, filterCount, fullApp, matches, type Filters, type Model } from '../../domain/model';
import { useI18n } from '../../i18n';
import type { Dict } from '../../i18n/en';
import { IconFit, IconInfo, IconLock, IconMinus, IconPlus, IconReset, IconSliders } from '../../ui/icons';
import { useToast } from '../../ui/Toast';
import { teamName } from '../bits';
import { useLandscape } from '../landscape';
import { searchBridge } from '../paths';
import {
  createModel,
  DEFAULTS,
  GraphEngine,
  isExt,
  type GData,
  type GFilter,
  type GModel,
  type GSys,
  type Params,
  type RunInfo,
} from './engine';
import { domainColor } from './palette';

/** Layout state (positions, pins, settings, zoom) survives switching views: one per loaded landscape. */
const models = new WeakMap<Model, GModel>();

function toData(m: Model): GData {
  return {
    domains: m.domains.map((d) => ({ id: d.id, name: d.name })),
    systems: m.appIds.map((id): GSys => {
      const a = m.apps.get(id)!;
      if (isRestrictedApp(a)) return { id, name: null, domain: a.domainId, kind: 'app' };
      return {
        id,
        name: a.name,
        domain: a.domainId,
        kind: a.kind,
        team: teamName(m, a.teamId) || undefined,
        tech: a.tech ?? undefined,
      };
    }),
    flows: m.flows
      .filter((f) => m.apps.has(f.fromId) && m.apps.has(f.toId))
      .map((f) => ({
        id: f.id,
        from: f.fromId,
        to: f.toId,
        type: f.type,
        name: isRestrictedFlow(f) ? null : f.name,
        objects: isRestrictedFlow(f) ? [] : f.dataObjectIds,
      })),
  };
}

function toFilter(m: Model, fl: Filters): GFilter {
  return { flowOK: (g) => matches(m.flow.get(g.id)!, fl), hidden: new Set(fl.hidden), isolated: fl.isolated };
}

const isBlockNow = () => window.matchMedia?.('(max-width: 1100px)').matches === true;
function useBlock() {
  const [b, setB] = useState(isBlockNow);
  useEffect(() => {
    const mq = window.matchMedia?.('(max-width: 1100px)');
    if (!mq) return;
    const on = () => setB(mq.matches);
    mq.addEventListener('change', on);
    return () => mq.removeEventListener('change', on);
  }, []);
  return b;
}

function Dot({ m, s, sm }: { m: Model; s: GSys; sm?: boolean }) {
  return (
    <span
      className={'dot' + (isExt(s) ? ' ext' : '') + (sm ? ' sm' : '')}
      style={{ '--dc': domainColor(m, s.domain) } as React.CSSProperties}
    />
  );
}

const SLIDERS: [keyof Params, number, number, number][] = [
  ['spacing', 0.4, 2, 0.05],
  ['tension', 0.2, 2.5, 0.05],
  ['group', 0, 2.5, 0.05],
  ['stability', 0, 1, 0.05],
];

function Settings({
  params,
  onChange,
  onDefaults,
  onClose,
  anchor,
}: {
  params: Params;
  onChange: (p: Params, relayout: boolean) => void;
  onDefaults: () => void;
  onClose: () => void;
  anchor: HTMLElement | null;
}) {
  const { t } = useI18n();
  const ref = useRef<HTMLDivElement>(null);
  const [pos, setPos] = useState<{ left: number; top: number } | null>(null);
  useLayoutEffect(() => {
    const p = ref.current;
    if (!p) return;
    if (window.innerWidth > 720 && anchor) {
      const r = anchor.getBoundingClientRect();
      const w = p.offsetWidth;
      setPos({ left: Math.max(12, Math.min(window.innerWidth - w - 12, r.right - w)), top: r.bottom + 8 });
    } else setPos(null);
    p.querySelector('input')?.focus({ preventScroll: true });
  }, [anchor]);
  useEffect(() => {
    const k = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        e.stopPropagation();
        onClose();
      }
    };
    document.addEventListener('keydown', k, true);
    return () => document.removeEventListener('keydown', k, true);
  }, [onClose]);
  const fmt = (k: keyof Params) =>
    k === 'stability' ? Math.round(params[k] * 100) + ' %' : '×' + params[k].toFixed(2);
  return (
    <>
      <div className="fback" onClick={onClose} aria-hidden="true" />
      <div
        ref={ref}
        id="g-spop"
        className="fpop spop"
        role="dialog"
        aria-label={t.graph.settingsTitle}
        style={pos ? { position: 'fixed', left: pos.left, top: pos.top } : undefined}
      >
        <span className="fp-grip" aria-hidden="true" />
        <div className="fp-h">
          <b>{t.graph.settingsTitle}</b>
          <button type="button" className="iconbtn" aria-label={t.graph.closeSettings} onClick={onClose}>
            ✕
          </button>
        </div>
        {SLIDERS.map(([k, mn, mx, st]) => {
          const [l, d, lo, hi] = t.graph.sliders[k];
          return (
            <div key={k} className="sl">
              <div className="sl-h">
                <label htmlFor={'g-sl-' + k}>{l}</label>
                <output htmlFor={'g-sl-' + k}>{fmt(k)}</output>
              </div>
              <p>{d}</p>
              <input
                id={'g-sl-' + k}
                type="range"
                min={mn}
                max={mx}
                step={st}
                value={params[k]}
                onChange={(e) => onChange({ ...params, [k]: +e.target.value }, k !== 'stability')}
              />
              <div className="ends">
                <span>{lo}</span>
                <span>{hi}</span>
              </div>
            </div>
          );
        })}
        <div className="fp-f">
          <span className="fcount">{t.graph.settingsFoot}</span>
          <button type="button" className="aclear" onClick={onDefaults}>
            {t.graph.defaults}
          </button>
        </div>
      </div>
    </>
  );
}

interface Props {
  seg: ReactNode;
  panelEl: HTMLElement | null;
  onOpenLens: (id: string) => void;
}

export function GraphView({ seg, panelEl, onOpenLens }: Props) {
  const { t } = useI18n();
  const toast = useToast();
  const { model: m, filters, setFilters } = useLandscape();
  const cv = useRef<HTMLCanvasElement>(null);
  const gview = useRef<HTMLDivElement>(null);
  const tipRef = useRef<HTMLDivElement>(null);
  const setBtn = useRef<HTMLButtonElement>(null);
  const eng = useRef<GraphEngine | null>(null);
  const [, setTick] = useState(0);
  const [run, setRun] = useState<RunInfo | null>(null);
  const [busy, setBusy] = useState(false);
  const [tip, setTip] = useState<{ id: string; x: number; y: number } | null>(null);
  const [settings, setSettings] = useState(false);
  const block = useBlock();
  const paramT = useRef<number | undefined>(undefined);
  const latest = useRef({ t, onOpenLens, toast, m });
  latest.current = { t, onOpenLens, toast, m };

  const gm = useMemo(() => {
    if (!m) return null;
    let g = models.get(m);
    if (!g) models.set(m, (g = createModel(toData(m))));
    return g;
  }, [m]);

  // size the frame to the window when the page scrolls (tablets and phones)
  const sizeFrame = useCallback(() => {
    const gv = gview.current;
    if (!gv) return;
    if (isBlockNow()) {
      const top = gv.getBoundingClientRect().top + window.scrollY;
      gv.style.height =
        Math.max(380, Math.round(window.innerHeight - top - (window.innerWidth <= 720 ? 12 : 20))) + 'px';
    } else gv.style.height = '';
  }, []);
  useLayoutEffect(() => {
    sizeFrame();
    window.addEventListener('resize', sizeFrame);
    return () => window.removeEventListener('resize', sizeFrame);
  }, [sizeFrame, block]);

  useEffect(() => {
    if (!m || !gm || !cv.current) return;
    const e = new GraphEngine(cv.current, gm, toFilter(m, filters), {
      onChange: () => setTick((x) => x + 1),
      onRun: setRun,
      onBusy: setBusy,
      onOpenLens: (id) => {
        const L = latest.current;
        if (L.m && fullApp(L.m, id)) L.onOpenLens(id);
      },
      onHover: (id, x, y) => setTip(id ? { id, x, y } : null),
      toast: (kind, id) => {
        const L = latest.current;
        const name = L.m ? (fullApp(L.m, id)?.name ?? L.t.common.restrictedApp) : id;
        L.toast(kind === 'pinned' ? L.t.graph.pinnedToast(name) : L.t.graph.unpinnedToast(name));
      },
    });
    eng.current = e;
    // development aid for browser checks
    if (import.meta.env.DEV) (window as unknown as { __noosGraph?: GraphEngine }).__noosGraph = e;
    return () => {
      e.dispose();
      eng.current = null;
    };
    // the engine is created once per landscape; filter changes go through setFilter below
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [m, gm]);

  const first = useRef(true);
  useEffect(() => {
    if (first.current) {
      first.current = false;
      return;
    }
    if (m) eng.current?.setFilter(toFilter(m, filters));
  }, [m, filters]);

  // search picks an application: select and zoom to it instead of opening its lens
  useEffect(() => {
    searchBridge.pickApp = (id) => {
      const e = eng.current;
      const L = latest.current;
      if (!e?.G || !L.m) return;
      if (!e.G.idset.has(id)) {
        L.toast(L.t.graph.hiddenByFilters(fullApp(L.m, id)?.name ?? id));
        return;
      }
      e.select(id);
      e.focusOn(id);
    };
    return () => {
      searchBridge.pickApp = null;
    };
  }, []);

  // tooltip placement inside the stage
  useLayoutEffect(() => {
    const el = tipRef.current;
    const st = el?.parentElement;
    if (!el || !st || !tip) return;
    const W = st.clientWidth;
    const H = st.clientHeight;
    const tw = el.offsetWidth || 260;
    const th = el.offsetHeight || 100;
    let tx = tip.x + 16;
    let ty = tip.y + 14;
    if (tx + tw > W - 8) tx = tip.x - tw - 16;
    if (ty + th > H - 36) ty = Math.max(8, tip.y - th - 14);
    el.style.left = Math.max(8, tx) + 'px';
    el.style.top = ty + 'px';
  }, [tip]);

  // the selection card appears or goes inside the frame: keep the canvas sized
  const e = eng.current;
  const G = e?.G ?? null;
  const S = e?.S;
  const hasSel = !!S?.sel;
  useLayoutEffect(() => {
    eng.current?.resize();
  }, [hasSel, block]);

  if (!m || !gm) return null;
  const nm = (id: string) => fullApp(m, id)?.name ?? t.common.restrictedApp;
  const sys = (id: string) => gm.sys.get(id)!;
  const total = gm.data.systems.length;
  const isDefault = !e || e.isDefaultNow();

  const reset = () => {
    if (!e || e.isDefaultNow()) return;
    e.resetLayout();
    setTip(null);
    toast(filterCount(filters) ? t.graph.resetToastFilters : t.graph.resetToast);
  };
  const setParams = (p: Params, relayout: boolean) => {
    if (!e) return;
    e.M.params = { ...p };
    setTick((x) => x + 1);
    window.clearTimeout(paramT.current);
    if (relayout) paramT.current = window.setTimeout(() => e.setParams(p, true), 140);
  };

  const onKey = (ev: React.KeyboardEvent) => {
    if (!e) return;
    if (ev.key === 'ArrowRight' || ev.key === 'ArrowDown') e.stepSelection(1);
    else if (ev.key === 'ArrowLeft' || ev.key === 'ArrowUp') e.stepSelection(-1);
    else if (ev.key === 'Enter' && e.S.sel) e.clickNode(e.S.sel);
    else if (ev.key === 'Escape' && e.S.sel) e.select(null);
    else if (ev.key === '+' || ev.key === '=') e.zoomBy(1.4);
    else if (ev.key === '-') e.zoomBy(1 / 1.4);
    else return;
    ev.preventDefault();
    ev.stopPropagation();
  };

  const partnerRows = (id: string) => {
    if (!G) return null;
    const adj = G.adj.get(id);
    const ps = e!
      .nbrs(id)
      .sort((a, b) => adj!.get(b)!.flows.length - adj!.get(a)!.flows.length || nm(a).localeCompare(nm(b)));
    return (
      <div className="rows">
        {ps.map((p) => {
          const c = adj!.get(p)!.flows.length;
          return (
            <button
              key={p}
              type="button"
              className="row"
              onClick={() => e!.select(p)}
              onPointerEnter={(ev) => ev.pointerType === 'mouse' && e!.setHover(p)}
              onPointerLeave={() => e!.setHover(null)}
            >
              <span className="ri">
                <Dot m={m} s={sys(p)} />
              </span>
              <span className="rn">
                {nm(p)}
                <small>{m.domain.get(sys(p).domain)?.name}</small>
              </span>
              <span className="rr">{t.graph.flowsN(c)}</span>
            </button>
          );
        })}
      </div>
    );
  };

  const title = (id: string) =>
    fullApp(m, id) ? (
      nm(id)
    ) : (
      <>
        <IconLock size={16} />
        <span>{t.common.restrictedApp}</span>
      </>
    );

  const selection = (card: boolean): ReactNode => {
    if (!e || !G || !S?.sel) return null;
    const close = (
      <button type="button" className="iconbtn pn-close" aria-label={t.graph.clearSel} onClick={() => e.select(null)}>
        ✕
      </button>
    );
    const grip = card ? <span className="ccgrip" aria-hidden="true" /> : null;
    if (S.sel2) {
      const a = S.sel;
      const b = S.sel2;
      const direct = G.adj.get(a)?.get(b);
      let ans: ReactNode;
      if (direct)
        ans = (
          <>
            <span className="yes">{t.common.yes}</span>
            <small>{t.graph.directly(direct.flows.length)}</small>
          </>
        );
      else if (S.path)
        ans = (
          <>
            <span className="via">{t.conn.indirectly}</span>
            <small>{t.graph.through(S.path.length - 2)}</small>
          </>
        );
      else
        ans = (
          <>
            <span className="no">{t.common.no}</span>
            <small>{t.graph.notEven}</small>
          </>
        );
      const shown = direct ? direct.flows.slice(0, card ? 3 : 6) : [];
      const lensBtn = (id: string) =>
        fullApp(m, id) ? (
          <button type="button" className="btn" onClick={() => onOpenLens(id)}>
            {t.conn.openLens(nm(id))}
          </button>
        ) : null;
      return (
        <>
          {grip}
          {close}
          <span className="fp-l">{t.graph.connectedQ}</span>
          <h2 style={{ fontSize: 17 }}>
            {title(a)} <span style={{ color: 'var(--fg-subtle)', fontWeight: 400 }}>{t.conn.and}</span> {title(b)}
          </h2>
          <div className="pcc">
            <div className="cc-ans">{ans}</div>
            {S.path && (
              <div className="cc-path">
                {S.path.map((id, i) => (
                  <span key={id} style={{ display: 'contents' }}>
                    {i > 0 && <span className="arr">→</span>}
                    <button type="button" className="och path" onClick={() => e.select(id)}>
                      {fullApp(m, id) ? nm(id) : t.common.restrictedShort}
                    </button>
                  </span>
                ))}
              </div>
            )}
            {direct && (
              <div className="flist">
                {shown.map((f) => (
                  <div key={f.id} className="flrow">
                    <span className="ty">{f.type.toUpperCase()}</span>
                    <span>
                      {fullApp(m, f.from) ? nm(f.from) : t.common.restrictedShort} →{' '}
                      {fullApp(m, f.to) ? nm(f.to) : t.common.restrictedShort}
                    </span>
                    <small>
                      {f.name
                        ? `${f.name} · ${f.objects.map((o) => m.objects.get(o)?.name ?? o).join(', ')}`
                        : t.common.restrictedFlow}
                    </small>
                  </div>
                ))}
                {direct.flows.length > shown.length && (
                  <small style={{ color: 'var(--fg-subtle)' }}>
                    {t.graph.more(direct.flows.length - shown.length)}
                  </small>
                )}
              </div>
            )}
          </div>
          <div className="pn-actions">
            {lensBtn(a)}
            {lensBtn(b)}
            <button type="button" className="aclear" onClick={() => e.select(null)}>
              {t.common.clear}
            </button>
          </div>
          {!card && <p className="hint">{t.graph.newSelection}</p>}
        </>
      );
    }
    const id = S.sel;
    const s = sys(id);
    const app = fullApp(m, id);
    const deg = G.deg.get(id) ?? 0;
    const fl = e.nbrs(id).reduce((acc, p) => acc + G.adj.get(id)!.get(p)!.flows.length, 0);
    const pinned = e.isPinned(id);
    return (
      <>
        {grip}
        {close}
        <div className="pn-head">
          <span className="dchip">
            <Dot m={m} s={s} />
            {m.domain.get(s.domain)?.name ?? s.domain}
          </span>
          {app && <span className="kind">{t.kind[app.kind]}</span>}
          {pinned && <span className="pinb">{t.graph.pinned}</span>}
        </div>
        <h2>{title(id)}</h2>
        <div className="pn-sub">
          {app ? [teamName(m, app.teamId), app.tech].filter(Boolean).join(' · ') : t.graph.noAccessDetails}
        </div>
        <p className="conn">
          {t.graph.connectedLead} <b>{t.graph.appsN(deg)}</b>
          {fl > 0 && <span style={{ color: 'var(--fg-subtle)' }}> ({t.graph.flowsN(fl)})</span>}
        </p>
        <div className="pn-actions">
          {app && (
            <button type="button" className="btn primary" onClick={() => onOpenLens(id)}>
              {t.graph.openLens}
            </button>
          )}
          {pinned && (
            <button type="button" className="btn" onClick={() => e.unpin(id)}>
              {t.graph.unpin}
            </button>
          )}
          <button type="button" className="btn" onClick={() => e.focusOn(id)}>
            {t.graph.zoomTo}
          </button>
        </div>
        <p className="hint">
          <IconInfo />
          <span>{t.graph.clickAnother(nm(id))}</span>
        </p>
        {deg ? (
          <>
            <h3>{t.graph.partners(deg)}</h3>
            {partnerRows(id)}
          </>
        ) : (
          <p>{t.graph.noConnections}</p>
        )}
      </>
    );
  };

  const overview = (): ReactNode => {
    if (!G)
      return (
        <>
          <h2>{t.graph.title}</h2>
          <p>{t.graph.computing}</p>
        </>
      );
    const counts: Record<string, number> = {};
    for (const id of G.ids) counts[sys(id).domain] = (counts[sys(id).domain] || 0) + 1;
    const totals: Record<string, number> = {};
    for (const s of gm.data.systems) totals[s.domain] = (totals[s.domain] || 0) + 1;
    const hid = new Set(filters.hidden);
    const toggle = (d: string) =>
      setFilters((f) => ({ ...f, hidden: f.hidden.includes(d) ? f.hidden.filter((x) => x !== d) : [...f.hidden, d] }));
    const firstDom = m.domains.find((d) => d.id !== 'external')?.id ?? m.domains[0]?.id ?? '';
    const lastDom = m.domains[Math.min(3, m.domains.length - 1)]?.id ?? firstDom;
    const ext = m.domains.find((d) => d.id === 'external')?.id ?? firstDom;
    const edge = `rgb(${getComputedStyle(document.documentElement).getPropertyValue('--edge').trim().split(' ').join(',')})`;
    return (
      <>
        <h2>{t.graph.title}</h2>
        <p>{t.graph.intro}</p>
        <div className="stats">
          <div className="stat">
            <b>{G.ids.length}</b>
            <span>{t.graph.statApps}</span>
          </div>
          <div className="stat">
            <b>{G.links.length}</b>
            <span>{t.graph.statLinks}</span>
          </div>
          <div className="stat">
            <b>{Object.keys(counts).length}</b>
            <span>{t.graph.statDomains}</span>
          </div>
        </div>
        <h3>{t.graph.domainsH}</h3>
        <div className="rows">
          {m.domains.map((d) => {
            const off = hid.has(d.id);
            return (
              <div key={d.id} className={'row domrow' + (off ? ' off' : '')}>
                <span className="ri">
                  <span
                    className={'dot' + (d.id === 'external' ? ' ext' : '')}
                    style={{ '--dc': domainColor(m, d.id) } as React.CSSProperties}
                  />
                </span>
                <span className="rn">{d.name}</span>
                <span className="rr">
                  {off ? '—' : counts[d.id] || 0} / {totals[d.id] || 0}
                </span>
                <button
                  type="button"
                  className="eye"
                  aria-pressed={!off}
                  aria-label={(off ? t.graph.show : t.graph.hide) + ' ' + d.name}
                  onClick={() => toggle(d.id)}
                >
                  {off ? t.graph.show : t.graph.hide}
                </button>
              </div>
            );
          })}
        </div>
        <h3>{t.graph.howToReadGraph}</h3>
        <div className="legend-list">
          <div>
            <svg width="34" height="20" aria-hidden="true">
              <circle cx="8" cy="10" r="4" fill={domainColor(m, firstDom)} />
              <circle cx="24" cy="10" r="8" fill={domainColor(m, firstDom)} />
            </svg>
            <span>{t.graph.legendSize}</span>
          </div>
          <div>
            <svg width="34" height="20" aria-hidden="true">
              <circle cx="17" cy="10" r="7" fill="none" stroke={domainColor(m, ext)} strokeWidth="2.2" />
            </svg>
            <span>{t.graph.legendOutline(t.graph.outside)}</span>
          </div>
          <div>
            <svg width="34" height="20" aria-hidden="true">
              <line x1="2" y1="6" x2="32" y2="6" stroke={edge} strokeWidth="1" />
              <line x1="2" y1="14" x2="32" y2="14" stroke={edge} strokeWidth="3.4" />
            </svg>
            <span>{t.graph.legendWidth}</span>
          </div>
          <div>
            <svg width="34" height="20" aria-hidden="true">
              <circle cx="15" cy="11" r="7" fill={domainColor(m, lastDom)} />
              <circle cx="21" cy="5" r="4" fill="var(--fg)" stroke="var(--surface)" strokeWidth="1.5" />
            </svg>
            <span>{t.graph.legendPinned}</span>
          </div>
        </div>
      </>
    );
  };

  const tipNode = (() => {
    if (!tip || !G || !e) return null;
    const id = tip.id;
    const adj = G.adj.get(id);
    const ps = e.nbrs(id).sort((a, b) => adj!.get(b)!.flows.length - adj!.get(a)!.flows.length);
    const key =
      S?.sel === id && !S.sel2
        ? fullApp(m, id)
          ? t.graph.clickAgain
          : t.graph.selectedDrag
        : S?.sel && !S.sel2
          ? t.graph.tipCheck(nm(S.sel))
          : t.graph.tipClick;
    return (
      <>
        <div className="tt-h">
          <Dot m={m} s={sys(id)} />
          {!fullApp(m, id) && <IconLock size={12} />}
          {nm(id)}
        </div>
        <div className="tt-s">{t.graph.tipConnected(m.domain.get(sys(id).domain)?.name ?? '', ps.length)}</div>
        {ps.length > 0 && (
          <div className="tt-l">
            {ps.slice(0, 8).map((p) => (
              <span key={p}>
                <Dot m={m} s={sys(p)} sm />
                {nm(p)}
              </span>
            ))}
            {ps.length > 8 && <span>{t.graph.more(ps.length - 8)}</span>}
          </div>
        )}
        <div className="tt-k">{key}</div>
      </>
    );
  })();

  const sub = G ? t.graph.sub(t.overview.ofTotal(G.ids.length, total), G.links.length) : t.graph.computing;
  const panelContent = !block && S?.sel ? selection(false) : overview();

  return (
    <>
      <div className="ov">
        <div className="ov-h">
          <div className="ov-t">
            <h1>{t.graph.heading}</h1>
            <p>{sub}</p>
          </div>
          <div className="ov-ctl">
            {seg}
            <div className="g-tools">
              <button
                type="button"
                className={'btn' + (isDefault ? '' : ' reset-on')}
                disabled={isDefault}
                aria-label={t.graph.reset + t.graph.resetLong}
                title={t.graph.resetTitle}
                onClick={reset}
              >
                <IconReset />
                <span className="g-bl">
                  {t.graph.reset}
                  <span className="lbl-l">{t.graph.resetLong}</span>
                </span>
              </button>
              <button
                ref={setBtn}
                type="button"
                className="btn"
                aria-expanded={settings}
                aria-controls="g-spop"
                aria-label={t.graph.settingsTitle}
                title={t.graph.settingsTitle}
                onClick={() => setSettings((s) => !s)}
              >
                <IconSliders />
                <span className="g-bl">
                  {t.graph.settings}
                  <span className="lbl-l">{t.graph.settingsLong}</span>
                </span>
              </button>
            </div>
          </div>
        </div>
      </div>
      <div className="g-view" ref={gview}>
        <div className="gstage">
          <canvas ref={cv} tabIndex={0} aria-label={t.graph.canvasLabel} onKeyDown={onKey} />
          <div ref={tipRef} className={'tip' + (tipNode ? ' on' : '')} role="tooltip">
            {tipNode}
          </div>
          {G && G.ids.length === 0 && (
            <div className="gempty">
              <div>
                {t.graph.empty}
                <br />
                <button
                  type="button"
                  className="btn"
                  style={{ marginTop: 10 }}
                  onClick={() => setFilters(EMPTY_FILTERS)}
                >
                  {t.graph.clearFilters}
                </button>
              </div>
            </div>
          )}
          <div className="zoomctl">
            <button type="button" aria-label={t.graph.zoomIn} title={t.graph.zoomIn} onClick={() => e?.zoomBy(1.4)}>
              <IconPlus />
            </button>
            <button
              type="button"
              aria-label={t.graph.zoomOut}
              title={t.graph.zoomOut}
              onClick={() => e?.zoomBy(1 / 1.4)}
            >
              <IconMinus />
            </button>
            <button type="button" aria-label={t.graph.fit} title={t.graph.fit} onClick={() => e?.fit(450)}>
              <IconFit />
            </button>
          </div>
          <div className="foot">
            <span className={'busy' + (busy ? ' on' : '')} />
            {run && <Footer t={t} run={run} />}
          </div>
        </div>
        {block && S?.sel && (
          <div className="g-card" aria-live="polite">
            <div className="pn-in">{selection(true)}</div>
          </div>
        )}
      </div>
      {panelEl &&
        createPortal(
          <div className="gpanel">
            <div className="pn-in">{panelContent}</div>
          </div>,
          panelEl,
        )}
      {settings && e && (
        <Settings
          params={e.M.params}
          onChange={setParams}
          onDefaults={() => setParams({ ...DEFAULTS }, true)}
          onClose={() => {
            setSettings(false);
            setBtn.current?.focus({ preventScroll: true });
          }}
          anchor={setBtn.current}
        />
      )}
    </>
  );
}

function Footer({ t, run }: { t: Dict; run: RunInfo }) {
  const f = t.graph.footer(run.n, run.m, run.ms, run.ticks);
  return (
    <span>
      <span className="g-meth">
        {t.graph.layoutWord} <b>{t.graph.method}</b> <span className="fsep">·</span>{' '}
      </span>
      {f.n} <span className="fsep">·</span> {f.m} <span className="fsep">·</span> {f.ms}{' '}
      <span className="fsep">{f.ticks}</span>
    </span>
  );
}
