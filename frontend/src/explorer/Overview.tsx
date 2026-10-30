import { useEffect, useLayoutEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { useGo } from '../ui/useGo';
import { Link } from 'react-router-dom';
import { isRestrictedApp } from '../api/types';
import {
  adjacency,
  anyFilter,
  appOrder,
  appStats,
  domainTeams,
  fullApp,
  isExternal,
  makeMatcher,
  pairs,
  shortestPath,
  visibleApps,
  type Adjacency,
  type Model,
} from '../domain/model';
import { useI18n } from '../i18n';
import type { Dict } from '../i18n/en';
import { IconLock, IconWarn } from '../ui/icons';
import { nameOf, pct, Sdot, teamName } from './bits';
import { useLandscape } from './landscape';
import { FlowRow } from './Panel';
import { paths } from './paths';
import { flip, type OverviewMode } from './view';

const canHover = () => window.matchMedia?.('(hover: hover)').matches === true;
export const isBlock = () => window.matchMedia?.('(max-width: 1100px)').matches === true;

export function OverviewSeg({ mode, onMode }: { mode: OverviewMode; onMode: (m: OverviewMode) => void }) {
  const { t } = useI18n();
  const opts: [OverviewMode, string][] = [
    ['cards', t.overview.cards],
    ['connections', t.overview.connections],
    ['graph', t.overview.graph],
  ];
  return (
    <div className="seg" role="group" aria-label={t.overview.mode}>
      {opts.map(([k, l]) => (
        <button key={k} type="button" aria-pressed={mode === k} onClick={() => onMode(k)}>
          {l}
        </button>
      ))}
    </div>
  );
}

export function OvHead({ title, sub, children }: { title: string; sub: ReactNode; children?: ReactNode }) {
  return (
    <div className="ov-h">
      <div className="ov-t">
        <h1>{title}</h1>
        <p>{sub}</p>
      </div>
      {children}
    </div>
  );
}

/* ---------------- cards ---------------- */

function Card({
  m,
  id,
  team,
  rel,
  onRestricted,
}: {
  m: Model;
  id: string;
  team: string | null;
  rel: 'me' | 'up' | 'down' | 'two' | null;
  onRestricted: () => void;
}) {
  const { t } = useI18n();
  const { filters } = useLandscape();
  const a = m.apps.get(id)!;
  const nav = useGo();
  if (isRestrictedApp(a))
    return (
      <button
        type="button"
        className="card restricted"
        data-app={id}
        aria-label={t.overview.restrictedCard}
        onClick={onRestricted}
      >
        <div className="c-top">
          <span className="kind">{t.kind.external}</span>
          <span className="rel" />
        </div>
        <div className="c-name">
          <IconLock />
          {t.common.restrictedApp}
        </div>
        <div className="c-team">{t.common.noAccess}</div>
      </button>
    );
  const st = appStats(m, id, filters);
  const p = pct(a.completeness);
  const filtered = anyFilter(filters) && st.total === 0;
  const cls = [
    'card',
    filtered && 'filtered',
    team && a.teamId === team && 'teamon',
    rel === 'me' && 'me',
    rel && rel !== 'me' && 'related',
  ]
    .filter(Boolean)
    .join(' ');
  const open = (e: React.MouseEvent) => {
    const cols = (e.currentTarget as HTMLElement).closest('.cols');
    const rects: Record<string, DOMRect> = {};
    cols
      ?.querySelectorAll<HTMLElement>('.card[data-app]')
      .forEach((c) => (rects[c.dataset.app!] = c.getBoundingClientRect()));
    flip.rects = rects;
    nav(paths.appLens(id));
  };
  return (
    <button
      type="button"
      className={cls}
      data-app={id}
      aria-label={t.overview.cardLabel(a.name, st.ins, st.outs)}
      onClick={open}
    >
      <div className="c-top">
        <span className="kind">{t.kind[a.kind]}</span>
        <span className={'cmp' + (a.completeness < 0.65 ? ' low' : '')} title={t.overview.recordComplete(p)}>
          <i style={{ '--p': p + '%' } as React.CSSProperties} />
          {p} %
        </span>
        <span className={'rel' + (rel && rel !== 'me' ? ' ' + rel : '')}>
          {rel === 'two'
            ? t.overview.relTwo
            : rel === 'up'
              ? t.overview.relUp
              : rel === 'down'
                ? t.overview.relDown
                : ''}
        </span>
      </div>
      <div className="c-name">{a.name}</div>
      <div className="c-team">{teamName(m, a.teamId)}</div>
      <div className="c-io">
        <span>
          <b>{st.ins}</b> {t.overview.in}
        </span>
        <span>
          <b>{st.outs}</b> {t.overview.out}
        </span>
      </div>
      {(st.sens > 0 || st.pers > 0) && (
        <div className="c-sens">
          {st.sens > 0 && (
            <span>
              <Sdot s="sensitive" />
              {t.overview.sensitiveCount(st.sens)}
            </span>
          )}
          {st.pers > 0 && (
            <span>
              <Sdot s="personal" />
              {t.overview.personalCount(st.pers)}
            </span>
          )}
        </div>
      )}
      <div className="badges">
        {st.change > 0 && (
          <span className="b warn">
            <IconWarn />
            {t.overview.plannedChanges(st.change)}
          </span>
        )}
        {st.dep > 0 && <span className="b danger">{t.overview.deprecatedFlows(st.dep)}</span>}
        {a.sourceOf.map((o) => (
          <span key={o} className="b src">
            {t.overview.sourceOf(m.objects.get(o)?.name ?? o)}
          </span>
        ))}
      </div>
    </button>
  );
}

export function CardsView({
  team,
  head,
  onRestricted,
}: {
  team: string | null;
  head: ReactNode;
  onRestricted: () => void;
}) {
  const { t } = useI18n();
  const { model: m, filters } = useLandscape();
  const [hover, setHover] = useState<string | null>(null);
  const ok = useMemo(() => (m ? makeMatcher(m, filters) : null), [m, filters]);
  if (!m || !ok) return null;
  const vis = new Set(visibleApps(m, filters));
  let up = new Set<string>();
  let down = new Set<string>();
  const h = hover && fullApp(m, hover) ? hover : null;
  if (h) {
    up = new Set((m.flowsIn.get(h) ?? []).filter(ok).map((f) => f.fromId));
    down = new Set((m.flowsOut.get(h) ?? []).filter(ok).map((f) => f.toId));
  }
  const relOf = (id: string) =>
    !h ? null : id === h ? 'me' : up.has(id) && down.has(id) ? 'two' : up.has(id) ? 'up' : down.has(id) ? 'down' : null;
  const onOver = (e: React.SyntheticEvent) => {
    if (e.type === 'mouseover' && !canHover()) return;
    const c = (e.target as HTMLElement).closest<HTMLElement>('.card[data-app]');
    setHover(c ? c.dataset.app! : null);
  };
  return (
    <div
      className="ov fade-in"
      onMouseOver={onOver}
      onMouseLeave={() => setHover(null)}
      onFocus={onOver}
      onBlur={() => setHover(null)}
    >
      {head}
      <div className={'cols' + (team ? ' teamfocus' : '') + (h ? ' hovering' : '')}>
        {m.domains.map((d) => {
          const ids = m.appIds.filter((id) => vis.has(id) && m.apps.get(id)!.domainId === d.id);
          if (!ids.length) return null;
          const teams = domainTeams(m, d.id);
          const partners = ids.every((id) => isExternal(m, m.apps.get(id)));
          return (
            <section key={d.id} className="dom" aria-label={d.name}>
              <div className="dom-h">
                <b>{d.name}</b>
                <span>{partners || !teams.length ? t.overview.partners : teams.map((x) => x.name).join(', ')}</span>
              </div>
              {ids.map((id) => (
                <Card key={id} m={m} id={id} team={team} rel={relOf(id)} onRestricted={onRestricted} />
              ))}
            </section>
          );
        })}
      </div>
    </div>
  );
}

/* ---------------- connections ---------------- */

export interface ConnPick {
  a: string | null;
  b: string | null;
  hover: string | null;
}

let measureCtx: CanvasRenderingContext2D | null = null;
function textW(s: string) {
  if (!measureCtx) measureCtx = document.createElement('canvas').getContext('2d');
  if (!measureCtx) return s.length * 7;
  measureCtx.font = '600 13px Inter Variable, Inter, ui-sans-serif, system-ui, sans-serif';
  return measureCtx.measureText(s).width;
}

export function useConnData() {
  const { model: m, filters } = useLandscape();
  return useMemo(() => {
    if (!m) return null;
    const ps = pairs(m, filters);
    const adj = adjacency(m, ps);
    const hidden = new Set(filters.hidden);
    const ids = m.appIds.filter(
      (id) => !hidden.has(m.apps.get(id)!.domainId) && (!filters.isolated || (adj.get(id)?.size ?? 0) > 0),
    );
    return { ps, adj, ids };
  }, [m, filters]);
}

function LockGlyph({ x, y }: { x: number; y: number }) {
  return (
    <g transform={`translate(${x},${y})`} className="lockic" fill="none" strokeWidth="1.7" strokeLinecap="round">
      <rect x="0" y="-5" width="11" height="8.5" rx="1.6" />
      <path d="M2.6 -5V-7.2a2.9 2.9 0 0 1 5.8 0V-5" />
    </g>
  );
}

function pathKeys(path: string[] | null) {
  const s = new Set<string>();
  if (path) for (let i = 0; i < path.length - 1; i++) s.add([path[i], path[i + 1]].sort().join('|'));
  return s;
}

interface Focus {
  X: string | null;
  Y: string | null;
  path: string[] | null;
  pk: Set<string>;
}
function focusOf(adj: Adjacency, pick: ConnPick): Focus {
  const { a, b, hover: h } = pick;
  const X = a || h;
  const Y = a ? b || (h && h !== a ? h : null) : null;
  let path: string[] | null = null;
  if (X && Y) path = adj.get(X)?.has(Y) ? [X, Y] : shortestPath(adj, X, Y);
  return { X, Y, path, pk: pathKeys(path) };
}

function elClass(adj: Adjacency, F: Focus, pick: ConnPick, id: string, filtered: boolean) {
  return [
    (id === F.X || id === F.Y) && 'self',
    (id === pick.a || id === pick.b) && 'pinned',
    !!F.X && !F.Y && adj.get(F.X)?.has(id) && 'on',
    !!F.path && F.path.includes(id) && id !== F.X && id !== F.Y && 'onpath',
    filtered && 'filtered',
  ]
    .filter(Boolean)
    .join(' ');
}
function edgeClass(F: Focus, a: string, b: string, w: number) {
  const k = [a, b].sort().join('|');
  const on = !!F.X && !F.Y && (a === F.X || b === F.X) && w > 0;
  return {
    cls: ['ce', !w && 'filtered', on && 'on', F.pk.has(k) && 'path'].filter(Boolean).join(' '),
    top: on || F.pk.has(k),
  };
}

function Circle({
  width,
  pick,
  onPick,
  onHover,
}: {
  width: number;
  pick: ConnPick;
  onPick: (id: string) => void;
  onHover: (id: string | null) => void;
}) {
  const { t } = useI18n();
  const { model: m, filters } = useLandscape();
  const data = useConnData();
  const sc = useRef<HTMLDivElement>(null);
  const geo = useMemo(() => {
    if (!m || !data || !width) return null;
    const order = appOrder(m);
    const ids = [...data.ids].sort(order);
    const W = width;
    const narrow = W < 560;
    const short = (n: string) => (narrow && n.length > 11 ? n.slice(0, 10).trimEnd() + '…' : n);
    const restr = (id: string) => isRestrictedApp(m.apps.get(id)!);
    const lw = (id: string) => (restr(id) ? 14 : textW(short(nameOf(t, m, id))));
    const maxL = Math.max(40, ...ids.map(lw));
    const availH = isBlock()
      ? Math.min(W, 900)
      : Math.max(440, (sc.current?.closest('.scroll')?.clientHeight ?? 700) - 110);
    const doms = m.domains.filter((d) => ids.some((i) => m.apps.get(i)!.domainId === d.id));
    const slots = ids.length + doms.length * 1.4;
    const minR = (slots * (narrow ? 17 : 19)) / (2 * Math.PI);
    const ring = maxL + (narrow ? 18 : 46);
    const r = Math.max(minR, Math.min(W, availH) / 2 - ring);
    const R = r + ring;
    const size = 2 * R;
    const svgW = Math.max(W, size);
    const cx = svgW / 2;
    const cy = R;
    const step = (2 * Math.PI) / slots;
    let slot = 0;
    const ang: Record<string, number> = {};
    const domSpan: Record<string, [number, number]> = {};
    for (const d of doms) {
      slot += 0.7;
      const mine = ids.filter((i) => m.apps.get(i)!.domainId === d.id);
      domSpan[d.id] = [slot * step, (slot + mine.length) * step];
      for (const i of mine) {
        ang[i] = (slot + 0.5) * step - Math.PI / 2;
        slot++;
      }
      slot += 0.7;
    }
    const P = (a: number, rad: number): [number, number] => [cx + rad * Math.cos(a), cy + rad * Math.sin(a)];
    const hub = (d: string, k: number) => {
      const sp = domSpan[d];
      return P((sp[0] + sp[1]) / 2 - Math.PI / 2, r * k);
    };
    const edges = data.ps
      .filter((p) => p.a in ang && p.b in ang)
      .map((p) => {
        const [x1, y1] = P(ang[p.a], r - 6);
        const [x2, y2] = P(ang[p.b], r - 6);
        const da = m.apps.get(p.a)!.domainId;
        const db = m.apps.get(p.b)!.domainId;
        let d: string;
        if (da === db) {
          const [hx, hy] = hub(da, 0.62);
          d = `M${x1},${y1} Q${hx},${hy} ${x2},${y2}`;
        } else {
          const [ax, ay] = hub(da, 0.42);
          const [bx, by] = hub(db, 0.42);
          d = `M${x1},${y1} C${ax},${ay} ${bx},${by} ${x2},${y2}`;
        }
        return { p, d };
      });
    const rA = r + maxL + (narrow ? 12 : 26);
    const arcs = doms.map((d) => {
      const [s0, s1] = domSpan[d.id].map((v) => v - Math.PI / 2);
      const mid = (s0 + s1) / 2;
      const bottom = Math.sin(mid) > 0;
      const arc = (rad: number, rev: boolean) => {
        const [x0, y0] = P(rev ? s1 : s0, rad);
        const [x1, y1] = P(rev ? s0 : s1, rad);
        const large = s1 - s0 > Math.PI ? 1 : 0;
        return `M${x0.toFixed(1)},${y0.toFixed(1)} A${rad},${rad} 0 ${large} ${rev ? 0 : 1} ${x1.toFixed(1)},${y1.toFixed(1)}`;
      };
      return { d, arc: arc(rA, false), text: narrow ? null : arc(bottom ? rA + 15 : rA + 7, bottom) };
    });
    return { ids, ang, r, svgW, size, cx, cy, edges, arcs, short, lw, restr };
  }, [m, data, width, t]);

  useLayoutEffect(() => {
    if (geo && sc.current && geo.svgW > width) sc.current.scrollLeft = (geo.svgW - width) / 2;
  }, [geo, width]);

  if (!m || !data || !geo) return <div ref={sc} className="cscroll" />;
  const F = focusOf(data.adj, pick);
  const af = anyFilter(filters);
  const edges = geo.edges.map((e) => ({ ...e, ...edgeClass(F, e.p.a, e.p.b, e.p.w) }));
  const ordered = [...edges.filter((e) => !e.top), ...edges.filter((e) => e.top)];
  const { r, cx, cy } = geo;
  return (
    <div ref={sc} className={'cscroll' + (F.X ? ' cfocus' : '')}>
      <svg width={geo.svgW} height={geo.size} role="img" aria-label={t.conn.circleLabel}>
        {geo.arcs.map(({ d, arc, text }) => (
          <g key={d.id}>
            <path className="darc" d={arc}>
              {!text && <title>{d.name}</title>}
            </path>
            {text && (
              <>
                <path id={'dp-' + d.id} d={text} fill="none" />
                <text className="dname">
                  <textPath href={'#dp-' + d.id} startOffset="50%" textAnchor="middle">
                    {d.name}
                  </textPath>
                </text>
              </>
            )}
          </g>
        ))}
        <g>
          {ordered.map(({ p, d, cls }) => (
            <path key={p.key} className={cls} d={d} strokeWidth={(1.1 + 0.9 * Math.max(p.w, 1)).toFixed(1)}>
              <title>{`${nameOf(t, m, p.a)} ↔ ${nameOf(t, m, p.b)}: ${t.conn.flowsW(p.w)}`}</title>
            </path>
          ))}
        </g>
        <g>
          {geo.ids.map((id) => {
            const a = geo.ang[id];
            const dg = (a * 180) / Math.PI;
            const flipL = Math.cos(a) < 0;
            const w = geo.lw(id);
            const n = data.adj.get(id)?.size ?? 0;
            const nm = nameOf(t, m, id);
            const rs = geo.restr(id);
            return (
              <g
                key={id}
                className={'cl' + (rs ? ' restricted ' : ' ') + elClass(data.adj, F, pick, id, af && n === 0)}
                data-conn={id}
                tabIndex={0}
                role="button"
                aria-label={`${nm}, ${t.conn.connectedTo(n)}`}
                transform={`translate(${cx},${cy}) rotate(${dg.toFixed(2)})`}
                onClick={() => onPick(id)}
                onKeyDown={(e) => {
                  if (e.key === 'Enter' || e.key === ' ') {
                    e.preventDefault();
                    onPick(id);
                  }
                }}
                onMouseEnter={() => canHover() && onHover(id)}
                onFocus={() => onHover(id)}
              >
                <circle className="cdot" cx={r} cy="0" r="4.5" />
                <g transform={`translate(${r + 10},0)${flipL ? ' rotate(180)' : ''}`}>
                  <rect className="clhit" rx="5" x={flipL ? -w - 8 : -10} y={-11} width={w + 18} height={22} />
                  {rs ? (
                    <LockGlyph x={flipL ? -14 : 0} y={0} />
                  ) : (
                    <text x="0" y="4.5" textAnchor={flipL ? 'end' : 'start'}>
                      {geo.short(nm)}
                    </text>
                  )}
                </g>
                <title>{`${nm} · ${t.conn.connections(n)}`}</title>
              </g>
            );
          })}
        </g>
      </svg>
    </div>
  );
}

function Clusters({
  width,
  pick,
  onPick,
  onHover,
}: {
  width: number;
  pick: ConnPick;
  onPick: (id: string) => void;
  onHover: (id: string | null) => void;
}) {
  const { t } = useI18n();
  const { model: m, filters } = useLandscape();
  const data = useConnData();
  const ref = useRef<HTMLDivElement>(null);
  const [pos, setPos] = useState<{
    w: number;
    h: number;
    p: Record<string, { x: number; y: number; left: boolean }>;
  } | null>(null);
  const W = width;
  const narrow = W < 560;
  const colW = Math.round(narrow ? (W - 36) / 2 : Math.max(128, Math.min(290, (W - Math.max(64, W * 0.3)) / 2)));
  const corr = Math.max(36, W - 2 * colW);
  const split = useMemo(() => {
    if (!m || !data) return { left: [] as string[], right: [] as string[] };
    const keep = new Set(data.ids);
    const doms = m.domains
      .map((d) => d.id)
      .filter((d) => m.appIds.some((id) => keep.has(id) && m.apps.get(id)!.domainId === d));
    // balance the two columns by number of applications, keeping the domain order
    const count = (d: string) => m.appIds.filter((id) => keep.has(id) && m.apps.get(id)!.domainId === d).length;
    const total = doms.reduce((s, d) => s + count(d), 0);
    let acc = 0;
    let cut = doms.length;
    for (let i = 0; i < doms.length; i++) {
      if (acc + count(doms[i]) / 2 > total / 2) {
        cut = i;
        break;
      }
      acc += count(doms[i]);
    }
    cut = Math.max(1, Math.min(doms.length - 1, cut));
    return { left: doms.slice(0, cut), right: doms.slice(cut) };
  }, [m, data]);

  useLayoutEffect(() => {
    const el = ref.current;
    if (!el) return;
    const cr = el.getBoundingClientRect();
    const p: Record<string, { x: number; y: number; left: boolean }> = {};
    el.querySelectorAll<HTMLElement>('.cpill').forEach((b) => {
      const r = b.getBoundingClientRect();
      const left = b.closest('.clu-col')?.getAttribute('data-side') === 'L';
      p[b.dataset.conn!] = { x: (left ? r.right : r.left) - cr.left, y: r.top + r.height / 2 - cr.top, left };
    });
    setPos({ w: cr.width, h: cr.height, p });
  }, [split, width, data]);

  if (!m || !data) return null;
  const F = focusOf(data.adj, pick);
  const af = anyFilter(filters);
  const keep = new Set(data.ids);
  const order = appOrder(m);
  const region = (d: string) => {
    const ids = m.appIds.filter((id) => keep.has(id) && m.apps.get(id)!.domainId === d).sort(order);
    if (!ids.length) return null;
    return (
      <section key={d} className="reg">
        <div className="reg-h">
          {m.domain.get(d)?.name ?? d}
          <span>{ids.length}</span>
        </div>
        {ids.map((id) => {
          const rs = isRestrictedApp(m.apps.get(id)!);
          const n = data.adj.get(id)?.size ?? 0;
          return (
            <button
              key={id}
              type="button"
              className={'cpill' + (rs ? ' restricted ' : ' ') + elClass(data.adj, F, pick, id, af && n === 0)}
              data-conn={id}
              aria-label={`${nameOf(t, m, id)}, ${t.conn.connectedTo(n)}`}
              onClick={() => onPick(id)}
              onMouseEnter={() => canHover() && onHover(id)}
              onFocus={() => onHover(id)}
            >
              <span className="cdot2" />
              <span className="cn">
                {rs ? (
                  <>
                    <IconLock />
                    <span className="sr">{t.common.restrictedApp}</span>
                  </>
                ) : (
                  nameOf(t, m, id)
                )}
              </span>
              <span className="cw" title={t.conn.connectedTo(n)}>
                {n}
              </span>
            </button>
          );
        })}
      </section>
    );
  };
  const edges = pos
    ? data.ps
        .filter((p) => pos.p[p.a] && pos.p[p.b])
        .map((p) => {
          const A = pos.p[p.a];
          const B = pos.p[p.b];
          let d: string;
          if (A.left === B.left) {
            const k = Math.min(corr * 0.45, 26 + Math.abs(B.y - A.y) * 0.32) * (A.left ? 1 : -1);
            d = `M${A.x},${A.y} C${A.x + k},${A.y} ${B.x + k},${B.y} ${B.x},${B.y}`;
          } else {
            const [L, Rr] = A.left ? [A, B] : [B, A];
            const dx = (Rr.x - L.x) * 0.45;
            d = `M${L.x},${L.y} C${L.x + dx},${L.y} ${Rr.x - dx},${Rr.y} ${Rr.x},${Rr.y}`;
          }
          return { p, d, ...edgeClass(F, p.a, p.b, p.w) };
        })
    : [];
  const ordered = [...edges.filter((e) => !e.top), ...edges.filter((e) => e.top)];
  return (
    <div
      ref={ref}
      className={'clu' + (narrow ? ' narrow' : '') + (F.X ? ' cfocus' : '')}
      style={{ gridTemplateColumns: `${colW}px ${corr}px ${colW}px`, width: 2 * colW + corr }}
    >
      <div className="clu-col" data-side="L">
        {split.left.map(region)}
      </div>
      <div />
      <div className="clu-col" data-side="R">
        {split.right.map(region)}
      </div>
      <svg width={pos?.w ?? 0} height={pos?.h ?? 0} aria-hidden="true">
        {ordered.map(({ p, d, cls }) => (
          <path key={p.key} className={cls} d={d} strokeWidth={(1.1 + 0.9 * Math.max(p.w, 1)).toFixed(1)} />
        ))}
      </svg>
    </div>
  );
}

export function ConnCardBody({ pick, setPick }: { pick: ConnPick; setPick: (p: ConnPick) => void }) {
  const { t } = useI18n();
  const { model: m, filters } = useLandscape();
  const data = useConnData();
  if (!m || !data) return null;
  const F = focusOf(data.adj, pick);
  const { X, Y, path } = F;
  const nm = (id: string) => nameOf(t, m, id);
  const lensBtn = (id: string) =>
    fullApp(m, id) ? (
      <Link className="btn" to={paths.appLens(id)}>
        {t.conn.openLens(nm(id))}
      </Link>
    ) : null;
  const clear = (
    <button type="button" className="aclear" onClick={() => setPick({ a: null, b: null, hover: null })}>
      {t.common.clear}
    </button>
  );
  if (!X)
    return (
      <>
        <div className="cc-t">
          <b>{t.conn.question}</b>
        </div>
        <div className="cc-hint">{canHover() ? t.conn.hintHover : t.conn.hintTap}</div>
      </>
    );
  if (!Y) {
    const order = appOrder(m);
    const partners = [...(data.adj.get(X)?.entries() ?? [])].sort((p, q) => order(p[0], q[0]));
    return (
      <>
        <div className="cc-t">
          <b>{nm(X)}</b> {t.conn.isConnectedTo} <b>{partners.length}</b> {t.conn.applicationsColon(partners.length)}
          {partners.length ? ':' : '.'}
        </div>
        <div className="cc-list">
          {partners.map(([id, w]) => (
            <button
              key={id}
              type="button"
              className="och"
              title={t.conn.flowsW(w)}
              onClick={() =>
                pick.a && id !== pick.a ? setPick({ ...pick, b: id }) : setPick({ a: id, b: null, hover: null })
              }
            >
              {!fullApp(m, id) && <IconLock />}
              {nm(id)}
              <span style={{ color: 'var(--fg-subtle)' }}>{w > 1 ? ' ×' + w : ''}</span>
            </button>
          ))}
        </div>
        {pick.a && (
          <>
            <div className="cc-hint">{t.conn.pickSecond}</div>
            <div className="cc-act">
              {lensBtn(X)}
              {clear}
            </div>
          </>
        )}
      </>
    );
  }
  const ok = makeMatcher(m, filters);
  const direct = m.flows.filter((f) => ((f.fromId === X && f.toId === Y) || (f.fromId === Y && f.toId === X)) && ok(f));
  let ans: ReactNode;
  if (direct.length)
    ans = (
      <>
        <div className="cc-ans">
          <span className="yes">{t.conn.yes}</span>
          {t.conn.directly(direct.length)}
        </div>
        <div className="rows">
          {direct.map((f) => (
            <FlowRow key={f.id} f={f} />
          ))}
        </div>
      </>
    );
  else if (path)
    ans = (
      <>
        <div className="cc-ans">
          <span className="via">{t.conn.indirectly}</span>
          {t.conn.between(path.length - 2)}
        </div>
        <div className="cc-path">
          {path.map((id, i) => (
            <span key={id} style={{ display: 'contents' }}>
              {i > 0 && <span style={{ color: 'var(--fg-subtle)' }}>→</span>}
              <button type="button" className="och" onClick={() => setPick({ a: id, b: null, hover: null })}>
                {nm(id)}
              </button>
            </span>
          ))}
        </div>
      </>
    );
  else
    ans = (
      <div className="cc-ans">
        <span className="no">{t.conn.no}</span>
        {t.conn.notConnected}
        {anyFilter(filters) ? t.conn.withFilters : ''}
      </div>
    );
  return (
    <>
      <div className="cc-t">
        <b>{nm(X)}</b> {t.conn.and} <b>{nm(Y)}</b>
      </div>
      {ans}
      <div className="cc-act">
        {lensBtn(X)}
        {lensBtn(Y)}
        {clear}
      </div>
    </>
  );
}

export function ConnectionsPanel({ pick, setPick }: { pick: ConnPick; setPick: (p: ConnPick) => void }) {
  const { t } = useI18n();
  const { model: m } = useLandscape();
  if (!m) return null;
  const opts = m.appIds.map((id) => (
    <option key={id} value={id}>
      {nameOf(t, m, id)}
    </option>
  ));
  const set = (a: string | null, b: string | null) =>
    setPick(!a && b ? { a: b, b: null, hover: null } : { a, b, hover: null });
  return (
    <>
      <h2>{t.conn.panelTitle}</h2>
      <p>{t.conn.panelText}</p>
      <div className="pcc">
        <ConnCardBody pick={pick} setPick={setPick} />
      </div>
      <h3>{t.conn.checkTwo}</h3>
      <div className="pick2">
        <select
          aria-label={t.conn.firstLabel}
          value={pick.a ?? ''}
          onChange={(e) => set(e.target.value || null, pick.b)}
        >
          <option value="">{t.conn.first}</option>
          {opts}
        </select>
        <select
          aria-label={t.conn.secondLabel}
          value={pick.b ?? ''}
          onChange={(e) => set(pick.a, e.target.value || null)}
        >
          <option value="">{t.conn.second}</option>
          {opts}
        </select>
      </div>
      <h3>{t.conn.reading}</h3>
      <div className="legend-list">
        <div>
          <svg width="34" height="10" aria-hidden="true">
            <path d="M1 5H33" stroke="var(--fg-subtle)" strokeWidth="2" />
          </svg>
          {t.conn.oneFlow}
        </div>
        <div>
          <svg width="34" height="10" aria-hidden="true">
            <path d="M1 5H33" stroke="var(--fg-subtle)" strokeWidth="4.6" />
          </svg>
          {t.conn.severalFlows}
        </div>
        <div>
          <svg width="34" height="10" aria-hidden="true">
            <path d="M1 5H33" stroke="var(--agent)" strokeWidth="2.4" />
          </svg>
          {t.conn.pathBetween}
        </div>
      </div>
    </>
  );
}

export function ConnectionsView({
  variant,
  head,
  pick,
  setPick,
}: {
  variant: 'circle' | 'cluster';
  head: ReactNode;
  pick: ConnPick;
  setPick: (p: ConnPick) => void;
}) {
  const nav = useGo();
  const { model } = useLandscape();
  const wrap = useRef<HTMLDivElement>(null);
  const [width, setWidth] = useState(0);
  const clearT = useRef<number | undefined>(undefined);
  useLayoutEffect(() => {
    const el = wrap.current;
    if (!el) return;
    const ro = new ResizeObserver(() => setWidth(el.clientWidth));
    ro.observe(el);
    setWidth(el.clientWidth);
    return () => ro.disconnect();
  }, []);
  useEffect(() => () => window.clearTimeout(clearT.current), []);
  const onPick = (id: string) => {
    const p = pick;
    if (!p.a) return setPick({ a: id, b: null, hover: null });
    if ((p.a === id && !p.b) || p.b === id) {
      setPick({ a: null, b: null, hover: null });
      if (model && fullApp(model, id)) nav(paths.appLens(id));
      return;
    }
    if (!p.b) return setPick({ ...p, b: id, hover: null });
    setPick({ a: id, b: null, hover: null });
  };
  const onHover = (id: string | null) => {
    window.clearTimeout(clearT.current);
    if (id !== pick.hover) setPick({ ...pick, hover: id });
  };
  const onMove = (e: MouseEvent) => {
    if (!canHover()) return;
    if ((e.target as HTMLElement).closest('[data-conn]')) return;
    if (pick.hover && clearT.current === undefined)
      clearT.current = window.setTimeout(() => {
        clearT.current = undefined;
        setPick({ ...pick, hover: null });
      }, 450);
  };
  const onBg = (e: MouseEvent) => {
    if ((e.target as HTMLElement).closest('[data-conn], button, a')) return;
    if (pick.a) setPick({ a: null, b: null, hover: null });
  };
  // background listeners (the pills and labels are the interactive elements)
  const handlers = useRef({ onMove, onBg });
  handlers.current = { onMove, onBg };
  useEffect(() => {
    const el = wrap.current;
    if (!el) return;
    const mv = (e: MouseEvent) => handlers.current.onMove(e);
    const ck = (e: MouseEvent) => handlers.current.onBg(e);
    el.addEventListener('mouseover', mv);
    el.addEventListener('click', ck);
    return () => {
      el.removeEventListener('mouseover', mv);
      el.removeEventListener('click', ck);
    };
  }, []);
  return (
    <div className="ov">
      {head}
      <div className="connwrap" ref={wrap}>
        {width > 0 &&
          (variant === 'circle' ? (
            <Circle width={width} pick={pick} onPick={onPick} onHover={onHover} />
          ) : (
            <Clusters width={width} pick={pick} onPick={onPick} onHover={onHover} />
          ))}
      </div>
    </div>
  );
}

export function connSub(t: Dict, n: number, total: number, pairsN: number) {
  return t.conn.sub(t.overview.ofTotal(n, total), pairsN);
}
