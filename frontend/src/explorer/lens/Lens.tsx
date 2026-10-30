import { useEffect, useLayoutEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { useGo } from '../../ui/useGo';
import { Link } from 'react-router-dom';
import type { LandscapeFlow } from '../../api/types';
import { appOrder, fullApp, fullFlow, lineage, makeMatcher, type Model } from '../../domain/model';
import { useI18n } from '../../i18n';
import type { Dict } from '../../i18n/en';
import { IconLock, IconWarn, TypeIcon } from '../../ui/icons';
import { nameOf, objName, pct, Sdot, StatusTag, teamName } from '../bits';
import { useLandscape } from '../landscape';
import { flowTitle } from '../Panel';
import { paths } from '../paths';
import { flip, type LensKind } from '../view';
import { appLayout, flowLayout, FLOW_CARD_W, lineageLayout, type HeadText, type Layout, type LNode } from './layouts';

const NO_NODES: LNode[] = [];
const REDUCED = () => window.matchMedia?.('(prefers-reduced-motion: reduce)').matches === true;

function Pill({
  m,
  f,
  glyph,
  style,
  on,
  filtered,
}: {
  m: Model;
  f: LandscapeFlow;
  glyph: string;
  style?: React.CSSProperties;
  on?: boolean;
  filtered?: boolean;
}) {
  const { t } = useI18n();
  const nav = useGo();
  const full = fullFlow(f);
  const title = flowTitle(t, m, f);
  const cls = [
    'pill',
    't-' + f.type,
    full && 'st-' + full.status,
    !style && 'static',
    !full && 'restricted',
    on && 'on',
    filtered && 'filtered',
  ]
    .filter(Boolean)
    .join(' ');
  return (
    <button
      type="button"
      className={cls}
      data-f={f.id}
      style={style}
      title={title}
      aria-label={title}
      onClick={() => nav(paths.flowLens(f.id))}
    >
      <span className="pi">
        <TypeIcon type={f.type} />
      </span>
      <span className="pn">
        {full ? (
          full.name
        ) : (
          <>
            <IconLock />
            {t.common.restrictedFlow}
          </>
        )}
      </span>
      {full ? <Sdot s={full.sensitivity} /> : <span />}
      <span className="pd">{glyph}</span>
      <span className="ps">{full?.schedule ?? t.type[f.type]}</span>
      <StatusTag f={f} />
    </button>
  );
}

function NodeInner({ m, id, children }: { m: Model; id: string; children?: ReactNode }) {
  const { t } = useI18n();
  const a = fullApp(m, id);
  if (!a)
    return (
      <span className="nm">
        <IconLock />
        <span>{t.common.restrictedApp}</span>
      </span>
    );
  return (
    <>
      <span className="nm">{a.name}</span>
      <span className="meta">
        <span className="kind">{t.kind[a.kind]}</span>
        <span>{teamName(m, a.teamId)}</span>
      </span>
      {children}
    </>
  );
}

export function CenterInner({ m, id }: { m: Model; id: string }) {
  const { t } = useI18n();
  const a = fullApp(m, id);
  if (!a) return null;
  const ins = (m.flowsIn.get(id) ?? []).length;
  const outs = (m.flowsOut.get(id) ?? []).length;
  const p = pct(a.completeness);
  return (
    <>
      <span className="meta">
        <span className="kind">{t.kind[a.kind]}</span>
        <span>{teamName(m, a.teamId)}</span>
      </span>
      <span className="nm">{a.name}</span>
      <span className="tech">{[a.tech, m.domain.get(a.domainId)?.name].filter(Boolean).join(' · ')}</span>
      <span className={'meter' + (a.completeness < 0.65 ? ' low' : '')}>
        <span className="bar2">
          <i style={{ width: p + '%' }} />
        </span>
        {t.common.percentDocumented(p)}
      </span>
      <span className="c-io">
        <span>
          <b>{ins}</b> {t.overview.in}
        </span>
        <span>
          <b>{outs}</b> {t.overview.out}
        </span>
      </span>
      {a.sourceOf.map((o) => (
        <span key={o} className="badges" style={{ marginTop: 2 }}>
          <span className="b src">{t.overview.sourceOf(objName(m, o))}</span>
        </span>
      ))}
    </>
  );
}

export function FlowCardBody({ m, f }: { m: Model; f: LandscapeFlow }) {
  const { t } = useI18n();
  const full = fullFlow(f);
  return (
    <>
      <div className="fh">
        <TypeIcon type={f.type} />
        {t.type[f.type]}
      </div>
      <h2>{full ? full.name : t.common.restrictedFlow}</h2>
      <div className="fr">
        {nameOf(t, m, f.fromId)} → {nameOf(t, m, f.toId)}
      </div>
      {full ? (
        <dl className="kv">
          <dt>{t.lens.schedule}</dt>
          <dd>{full.schedule || '—'}</dd>
          {full.volume && (
            <>
              <dt>{t.lens.volume}</dt>
              <dd>{full.volume}</dd>
            </>
          )}
          <dt>{t.lens.data}</dt>
          <dd>
            {full.dataObjectIds.map((o) => (
              <Link key={o} className="och" to={paths.lineage(o)}>
                {m.objects.get(o) && <Sdot s={m.objects.get(o)!.sensitivity} />}
                {objName(m, o)}
              </Link>
            ))}
          </dd>
          <dt>{t.lens.sensitivity}</dt>
          <dd>
            <Sdot s={full.sensitivity} />
            {t.sens[full.sensitivity]}
          </dd>
          <dt>{t.lens.owner}</dt>
          <dd>{teamName(m, full.ownerTeamId) || '—'}</dd>
          <dt>{t.lens.status}</dt>
          <dd>
            {t.status[full.status]} <StatusTag f={full} />
          </dd>
        </dl>
      ) : (
        <p className="muted">{t.panel.restrictedFlowText}</p>
      )}
      {full?.plannedChange?.summary && (
        <div className="callout">
          <IconWarn />
          <span>
            <b>{t.lens.plannedChange}</b> {full.plannedChange.summary}
          </span>
        </div>
      )}
    </>
  );
}

function headText(t: Dict, h: HeadText): ReactNode {
  switch (h.k) {
    case 'upstream':
      return h.n ? (
        <>
          {t.lens.upstream}{' '}
          <small>
            · {t.lens.appsCount(h.n)}
            {h.two ? t.lens.inclTwoWay : ''}
          </small>
        </>
      ) : (
        <small>{t.lens.noUpstream}</small>
      );
    case 'downstream':
      return h.n ? (
        <>
          {t.lens.downstream}{' '}
          <small>
            · {t.lens.appsCount(h.n)}
            {h.two ? t.lens.inclTwoWay : ''}
          </small>
        </>
      ) : (
        <small>{t.lens.noDownstream}</small>
      );
    case 'selected':
      return t.lens.selected;
    case 'second':
      return <small>{t.lens.secondLevelHead}</small>;
    case 'from':
      return t.lens.from;
    case 'to':
      return t.lens.to;
    case 'flow':
      return (
        <>
          {t.lens.flow}
          {h.n > 1 && <small> · {t.lens.flowsBetween(h.n)}</small>}
        </>
      );
    case 'depth': {
      const d = h.d;
      return d === 0
        ? t.lens.sourceOfTruth
        : d === -1
          ? t.lens.feedsSource
          : d < -1
            ? t.lens.furtherUp
            : d === 1
              ? t.lens.receivesDirectly
              : t.lens.hop(d);
    }
  }
}

/** One positioned node; on first mount it moves from the overview card it came from (FLIP) or fades in. */
function LensNode({
  n,
  cls,
  label,
  onClick,
  children,
}: {
  n: LNode;
  cls: string;
  label?: string;
  onClick?: () => void;
  children: ReactNode;
}) {
  const ref = useRef<HTMLElement | null>(null);
  useLayoutEffect(() => {
    const el = ref.current;
    if (!el) return;
    const fr = flip.rects?.[n.id];
    const lens = el.closest('.lens');
    el.style.transition = 'none';
    if (fr && lens && !REDUCED()) {
      const lr = lens.getBoundingClientRect();
      el.style.transform = `translate(${fr.left - lr.left}px,${fr.top - lr.top}px)`;
      el.style.width = fr.width + 'px';
      el.style.height = fr.height + 'px';
    } else {
      el.style.transform = `translate(${n.x}px,${n.y + 8}px)`;
      el.style.opacity = '0';
    }
    void el.offsetWidth;
    el.style.transition = '';
    el.style.transform = `translate(${n.x}px,${n.y}px)`;
    el.style.width = n.w + 'px';
    el.style.height = n.h + 'px';
    el.style.opacity = '';
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);
  const style: React.CSSProperties = { transform: `translate(${n.x}px,${n.y}px)`, width: n.w, height: n.h };
  if (n.role === 'center')
    return (
      <div ref={(e) => void (ref.current = e)} className={cls} data-node={n.id} style={style}>
        {children}
      </div>
    );
  return (
    <button
      ref={(e) => void (ref.current = e)}
      type="button"
      className={cls}
      data-node={n.id}
      style={style}
      aria-label={label}
      title={label}
      onClick={onClick}
    >
      {children}
    </button>
  );
}

interface Hi {
  flows: Set<string>;
  nodes: Set<string>;
}

/** A node that left the lens, kept for its fade-out at the position it had. */
export interface Leaving {
  n: LNode;
  dx: number;
  dy: number;
}

export const LEAVE_MS = 260;

/** Nodes present in the previous layout and absent from this one. */
export function leavingNodes(prev: Leaving[], next: LNode[]): Leaving[] {
  const keep = new Set(next.map((n) => n.key));
  return prev.filter((p) => !keep.has(p.n.key));
}

/** Keeps the nodes that leave the lens on screen for a short fade-out instead of dropping them at once. */
function useLeaving(nodes: LNode[], dx: number, dy: number): Leaving[] {
  const prev = useRef<Leaving[]>([]);
  const [leaving, setLeaving] = useState<Leaving[]>([]);
  useLayoutEffect(() => {
    const gone = leavingNodes(prev.current, nodes);
    prev.current = nodes.map((n) => ({ n, dx, dy }));
    if (gone.length && !REDUCED()) setLeaving(gone);
  }, [nodes, dx, dy]);
  useEffect(() => {
    if (!leaving.length) return;
    const id = window.setTimeout(() => setLeaving([]), LEAVE_MS);
    return () => window.clearTimeout(id);
  }, [leaving]);
  return leaving;
}

export function Lens({
  kind,
  id,
  width,
  height,
  second,
  onRestricted,
}: {
  kind: LensKind;
  id: string;
  width: number;
  height: number;
  second: boolean;
  onRestricted: () => void;
}) {
  const { t } = useI18n();
  const { model: m, filters } = useLandscape();
  const nav = useGo();
  const [cardH, setCardH] = useState(230);
  const [hi, setHi] = useState<Hi | null>(null);
  const cardRef = useRef<HTMLDivElement>(null);
  const lay: Layout | null = useMemo(() => {
    if (!m || !width) return null;
    if (kind === 'app') return m.apps.has(id) ? appLayout(m, id, width, second) : null;
    if (kind === 'flow') return flowLayout(m, id, width, cardH);
    return lineageLayout(m, id);
  }, [m, kind, id, width, second, cardH]);

  useLayoutEffect(() => {
    const h = cardRef.current?.offsetHeight;
    if (h && Math.abs(h - cardH) > 1) setCardH(h);
  });
  // keep the source of truth in view for long lineages
  const rootRef = useRef<HTMLDivElement>(null);
  const srcX = lay?.srcX;
  const layW = lay?.W ?? 0;
  useLayoutEffect(() => {
    if (kind !== 'object' || srcX == null || layW <= width) return;
    const sc = rootRef.current?.closest('.scroll');
    const id = requestAnimationFrame(() => {
      if (sc) sc.scrollLeft = Math.max(0, srcX - 430);
    });
    return () => cancelAnimationFrame(id);
  }, [kind, id, srcX, layW, width]);
  // FLIP rects are used once, by the nodes of the first lens after the overview
  useLayoutEffect(() => {
    flip.rects = null;
  }, [kind, id]);
  const leaving = useLeaving(
    lay?.nodes ?? NO_NODES,
    lay ? Math.max(0, (width - lay.W) / 2) : 0,
    lay ? Math.max(0, (height - lay.H) / 2 - 10) : 0,
  );

  if (!m) return null;
  if (!lay)
    return (
      <div className="compact">
        <p className="muted">{kind === 'object' && m.objects.has(id) ? t.lens.noLineage : t.lens.notFound}</p>
      </div>
    );
  const ok = makeMatcher(m, filters);
  const W = Math.max(lay.W, width);
  const H = Math.max(lay.H, height);
  const dx = Math.max(0, (width - lay.W) / 2);
  const dy = Math.max(0, (height - lay.H) / 2 - 10);
  const shown = new Set<string>([...lay.pills.map((p) => p.f.id), ...(lay.card ? [lay.card.f.id] : [])]);
  const visFlows = m.flows.filter((f) => shown.has(f.id));
  const nodeFiltered = (nid: string) => {
    const fs = visFlows.filter((f) => f.fromId === nid || f.toId === nid);
    return fs.length > 0 && !fs.some(ok);
  };
  const hover = (e: React.SyntheticEvent) => {
    const el = (e.target as HTMLElement).closest<HTMLElement>('[data-f], .node');
    if (!el) return setHi(null);
    if (el.dataset.f) {
      const f = m.flow.get(el.dataset.f)!;
      return setHi({ flows: new Set([f.id]), nodes: new Set([f.fromId, f.toId]) });
    }
    if (el.classList.contains('center')) return setHi(null);
    const nid = el.dataset.node!;
    const fs = new Set<string>();
    const ns = new Set([nid]);
    for (const w of lay.wires)
      if (w.f.fromId === nid || w.f.toId === nid) {
        fs.add(w.f.id);
        ns.add(w.f.fromId);
        ns.add(w.f.toId);
      }
    setHi({ flows: fs, nodes: ns });
  };
  const goNode = (nid: string) => {
    if (!fullApp(m, nid)) return onRestricted();
    nav(paths.appLens(nid));
  };
  const nodeLabel = (nid: string) => t.lens.focus(nameOf(t, m, nid));
  return (
    <div
      ref={rootRef}
      className={'lens' + (hi ? ' focusing' : '')}
      style={{ width: W, height: H }}
      onMouseOver={hover}
      onFocus={hover}
      onMouseLeave={() => setHi(null)}
      onBlur={(e) => {
        if (!e.currentTarget.contains(e.relatedTarget as Node)) setHi(null);
      }}
    >
      <svg className="wires" width={W} height={H} aria-hidden="true">
        <defs>
          {(['api', 'event', 'batch', 'ghost'] as const).map((k) => (
            <marker
              key={k}
              id={'ar-' + k}
              viewBox="0 0 10 10"
              refX="9"
              refY="5"
              markerWidth={k === 'ghost' ? 6 : 7}
              markerHeight={k === 'ghost' ? 6 : 7}
              markerUnits="userSpaceOnUse"
              orient="auto-start-reverse"
            >
              <path className={'mk-' + k} d="M0,0.5 L10,5 L0,9.5 z" />
            </marker>
          ))}
        </defs>
        <g transform={`translate(${dx},${dy})`} className="fresh" key={kind + id + second}>
          {lay.wires.map((w) => {
            const full = fullFlow(w.f);
            const cls = [
              'wire',
              't-' + w.f.type,
              full && 'st-' + full.status,
              w.ghost && 'ghost',
              hi?.flows.has(w.f.id) && 'on',
              !ok(w.f) && 'filtered',
            ]
              .filter(Boolean)
              .join(' ');
            const mk = w.marker ? `url(#ar-${w.ghost ? 'ghost' : w.f.type})` : undefined;
            return (
              <path
                key={w.key}
                className={cls}
                data-f={w.f.id}
                d={w.d}
                markerEnd={w.marker === 'end' ? mk : undefined}
                markerStart={w.marker === 'start' ? mk : undefined}
              />
            );
          })}
          {/* wider invisible strokes on top, so a thin line is easy to hover and click */}
          {lay.wires.map((w) => (
            <path
              key={'hit' + w.key}
              className="hit"
              data-f={w.f.id}
              d={w.d}
              onClick={() => (fullFlow(w.f) ? nav(paths.flowLens(w.f.id)) : onRestricted())}
            />
          ))}
        </g>
      </svg>
      <div className="layer fresh" style={{ transform: `translate(${dx}px,${dy}px)` }} key={'p' + kind + id + second}>
        {lay.heads.map((h, i) => (
          <div key={i} className="colhead" style={{ left: h.x, top: 18, width: h.w }}>
            {headText(t, h.text)}
          </div>
        ))}
        {lay.frames.map((fr) => (
          <div
            key={fr.partner}
            className={'frame' + (hi && fr.flows.some((x) => hi.flows.has(x)) ? ' on' : '')}
            style={{ left: fr.x, top: fr.y, width: fr.w, height: fr.h }}
          >
            <span>{t.lens.twoWayWith(nameOf(t, m, fr.partner))}</span>
          </div>
        ))}
        {lay.pills.map((p) => (
          <Pill
            key={p.f.id + ':' + p.x}
            m={m}
            f={p.f}
            glyph={p.glyph}
            style={{ left: p.x, top: p.y, width: p.w }}
            on={hi?.flows.has(p.f.id)}
            filtered={!ok(p.f)}
          />
        ))}
        {lay.card && (
          <div
            ref={cardRef}
            className={`fcard t-${lay.card.f.type}${hi?.flows.has(lay.card.f.id) ? ' on' : ''}`}
            data-f={lay.card.f.id}
            style={{ left: lay.card.x, top: lay.card.y, width: FLOW_CARD_W }}
          >
            <FlowCardBody m={m} f={lay.card.f} />
          </div>
        )}
      </div>
      {leaving.length > 0 && (
        <div className="layer" aria-hidden="true">
          {leaving.map(({ n, dx: ox, dy: oy }) => (
            <div
              key={'leave' + n.key}
              className={'node leaving' + (n.role === 'ghost' ? ' ghost' : '') + (n.role === 'center' ? ' center' : '')}
              style={{ transform: `translate(${n.x + ox}px,${n.y + oy}px)`, width: n.w, height: n.h }}
            >
              {n.role === 'center' ? (
                <CenterInner m={m} id={n.id} />
              ) : n.role === 'ghost' ? (
                <span className="nm">{nameOf(t, m, n.id)}</span>
              ) : (
                <NodeInner m={m} id={n.id} />
              )}
            </div>
          ))}
        </div>
      )}
      <div className="layer" style={{ transform: `translate(${dx}px,${dy}px)` }}>
        {lay.nodes.map((n) => {
          const restricted = !fullApp(m, n.id);
          const on = hi && (hi.nodes.has(n.id) || n.role === 'center');
          const cls = [
            'node',
            n.role === 'center' && 'center',
            n.role === 'ghost' && 'ghost',
            n.role === 'src' && 'src',
            restricted && 'restricted',
            on && 'on',
            n.role === 'node' && nodeFiltered(n.id) && 'filtered',
          ]
            .filter(Boolean)
            .join(' ');
          if (n.role === 'center')
            return (
              <LensNode key={n.key} n={n} cls={cls}>
                <CenterInner m={m} id={n.id} />
              </LensNode>
            );
          if (n.role === 'ghost')
            return (
              <LensNode key={n.key} n={n} cls={cls} label={nodeLabel(n.id)} onClick={() => goNode(n.id)}>
                <span className="nm">
                  {restricted ? (
                    <>
                      <IconLock />
                      {t.common.restrictedShort}
                    </>
                  ) : (
                    nameOf(t, m, n.id)
                  )}
                </span>
              </LensNode>
            );
          return (
            <LensNode key={n.key} n={n} cls={cls} label={nodeLabel(n.id)} onClick={() => goNode(n.id)}>
              <NodeInner m={m} id={n.id}>
                {n.twoWay && <span className="twb">{t.lens.twoWay}</span>}
                {n.role === 'src' && <span className="srcb">{t.lens.sourceOfTruth}</span>}
                {n.extra && (
                  <span
                    className="xb"
                    title={n.extra
                      .map((f) => t.lens.alsoReceives(nameOf(t, m, f.fromId), fullFlow(f)?.name ?? ''))
                      .join('\n')}
                  >
                    {t.lens.moreRoutes(n.extra.length)}
                  </span>
                )}
              </NodeInner>
            </LensNode>
          );
        })}
      </div>
    </div>
  );
}

/* ---------------- compact (phone) lens ---------------- */

function CNode({ m, id, center, onRestricted }: { m: Model; id: string; center?: boolean; onRestricted: () => void }) {
  const { t } = useI18n();
  const nav = useGo();
  if (center)
    return (
      <div className="cnode center">
        <CenterInner m={m} id={id} />
      </div>
    );
  const a = fullApp(m, id);
  return (
    <button type="button" className="cnode" onClick={() => (a ? nav(paths.appLens(id)) : onRestricted())}>
      {a ? (
        <>
          <span className="nm">{a.name}</span>
          <span className="meta">
            <span className="kind">{t.kind[a.kind]}</span>
            {teamName(m, a.teamId)}
          </span>
        </>
      ) : (
        <span className="nm">
          <IconLock /> {t.common.restrictedApp}
        </span>
      )}
    </button>
  );
}

export function CompactLens({ kind, id, onRestricted }: { kind: LensKind; id: string; onRestricted: () => void }) {
  const { t } = useI18n();
  const { model: m, filters } = useLandscape();
  if (!m) return null;
  const ok = makeMatcher(m, filters);
  const order = appOrder(m);
  const pill = (f: LandscapeFlow, g: string) => <Pill key={f.id} m={m} f={f} glyph={g} filtered={!ok(f)} />;
  if (kind === 'app') {
    if (!m.apps.has(id))
      return (
        <div className="compact">
          <p className="muted">{t.lens.notFound}</p>
        </div>
      );
    const grp = (fs: LandscapeFlow[], key: 'fromId' | 'toId') => {
      const g = new Map<string, LandscapeFlow[]>();
      for (const f of fs) {
        if (!g.has(f[key])) g.set(f[key], []);
        g.get(f[key])!.push(f);
      }
      return [...g.entries()].sort((a, b) => order(a[0], b[0]));
    };
    const ins = grp(m.flowsIn.get(id) ?? [], 'fromId');
    const outs = grp(m.flowsOut.get(id) ?? [], 'toId');
    return (
      <div className="compact">
        <h4>
          {t.lens.receivesFrom} · {ins.length}
        </h4>
        {ins.map(([n, fs]) => (
          <div key={n} className="cgroup">
            <CNode m={m} id={n} onRestricted={onRestricted} />
            {fs.map((f) => pill(f, '↓'))}
          </div>
        ))}
        <div className="cdown">↓</div>
        <CNode m={m} id={id} center onRestricted={onRestricted} />
        <div className="cdown">↓</div>
        <h4>
          {t.lens.sendsTo} · {outs.length}
        </h4>
        {outs.map(([n, fs]) => (
          <div key={n} className="cgroup">
            {fs.map((f) => pill(f, '↓'))}
            <CNode m={m} id={n} onRestricted={onRestricted} />
          </div>
        ))}
      </div>
    );
  }
  if (kind === 'flow') {
    const f = m.flow.get(id);
    if (!f)
      return (
        <div className="compact">
          <p className="muted">{t.lens.notFound}</p>
        </div>
      );
    return (
      <div className="compact">
        <CNode m={m} id={f.fromId} onRestricted={onRestricted} />
        <div className="cdown">↓</div>
        <div className={`fcard t-${f.type}`} data-f={f.id}>
          <FlowCardBody m={m} f={f} />
        </div>
        <div className="cdown">↓</div>
        <CNode m={m} id={f.toId} onRestricted={onRestricted} />
      </div>
    );
  }
  const Lg = lineage(m, id);
  if (!Lg)
    return (
      <div className="compact">
        <p className="muted">{m.objects.has(id) ? t.lens.noLineage : t.lens.notFound}</p>
      </div>
    );
  const ids = [...Lg.depth.keys()].sort((a, b) => Lg.depth.get(a)! - Lg.depth.get(b)! || order(a, b));
  const out: ReactNode[] = [];
  let last: number | null = null;
  for (const n of ids) {
    const d = Lg.depth.get(n)!;
    if (d !== last) {
      out.push(
        <h4 key={'h' + d}>
          {d === 0
            ? t.lens.sourceOfTruth
            : d < 0
              ? t.lens.feedsSource
              : d === 1
                ? t.lens.receivesDirectly
                : t.lens.hop(d)}
        </h4>,
      );
      last = d;
    }
    const l = Lg.link.get(n);
    out.push(
      <div key={n} className="cgroup">
        {l?.flows.map((f) => pill(f, l.dir === 'down' ? '↓' : '↑'))}
        <CNode m={m} id={n} onRestricted={onRestricted} />
      </div>,
    );
  }
  return <div className="compact">{out}</div>;
}
