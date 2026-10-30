import { useCallback, useEffect, useLayoutEffect, useRef, useState } from 'react';
import { useGo } from '../ui/useGo';
import { Link, useLocation } from 'react-router-dom';
import { FLOW_TYPES } from '../api/types';
import { anyFilter, fullApp, fullFlow, makeMatcher, visibleApps } from '../domain/model';
import { useI18n } from '../i18n';
import type { Dict } from '../i18n/en';
import { TopBar } from '../layout/TopBar';
import { IconGrid, IconWarn, TypeIcon } from '../ui/icons';
import { LineSample, nameOf, objName, Sdot } from './bits';
import { ActiveChips, FilterControl } from './Filters';
import { GraphView } from './graph/GraphView';
import { useLandscape } from './landscape';
import { CompactLens, Lens } from './lens/Lens';
import {
  CardsView,
  ConnectionsPanel,
  ConnectionsView,
  ConnCardBody,
  isBlock,
  OvHead,
  OverviewSeg,
  useConnData,
  type ConnPick,
} from './Overview';
import { Panel } from './Panel';
import { paths } from './paths';
import { parseLocation, sameView, viewPath, type OverviewMode, type View } from './view';

function Legend() {
  const { t } = useI18n();
  return (
    <footer className="legend" aria-label={t.panel.howToReadLens}>
      {FLOW_TYPES.map((v) => (
        <span key={v} className={'lg t-' + v}>
          <LineSample type={v} />
          <span style={{ color: 'var(--tc)', display: 'flex' }}>
            <TypeIcon type={v} />
          </span>
          {t.type[v]}
        </span>
      ))}
      {(['internal', 'personal', 'sensitive'] as const).map((s) => (
        <span key={s} className="lg">
          <Sdot s={s} />
          {t.sens[s]}
        </span>
      ))}
      <span className="lg">
        <span className="tg plan">{t.status.planned}</span>
        <span className="tg danger">{t.status.deprecated}</span>
      </span>
      <span className="lg">
        <span className="tg warn icon">
          <IconWarn />
        </span>
        {t.legend.plannedChange}
      </span>
    </footer>
  );
}

function crumbLabel(t: Dict, m: NonNullable<ReturnType<typeof useLandscape>['model']>, v: NonNullable<View>) {
  if (v.kind === 'app') return nameOf(t, m, v.id);
  if (v.kind === 'flow') {
    const f = m.flow.get(v.id);
    return f ? (fullFlow(f)?.name ?? t.common.restrictedFlow) : v.id;
  }
  if (v.kind === 'object') return objName(m, v.id);
  return m.teams.get(v.id)?.name ?? v.id;
}
const KB: Record<string, [string, string]> = {
  app: ['app', 'app'],
  flow: ['flow', 'flow'],
  object: ['data', 'object'],
  team: ['team', 'team'],
};

function Skeleton() {
  return (
    <div className="ov" aria-busy="true">
      <div className="ov-h">
        <span className="sk title" style={{ width: 240 }} />
        <span className="sk line" style={{ width: 320 }} />
      </div>
      <div className="cols">
        {Array.from({ length: 6 }, (_, i) => (
          <section key={i} className="dom">
            <span className="sk line" style={{ width: '60%' }} />
            <span className="sk line" style={{ width: '80%', marginTop: 8 }} />
            {Array.from({ length: 3 - (i % 2) }, (_, j) => (
              <span key={j} className="sk block" style={{ height: 112, marginTop: 10 }} />
            ))}
          </section>
        ))}
      </div>
    </div>
  );
}

export function Explorer() {
  const { t } = useI18n();
  const loc = useLocation();
  const nav = useGo();
  const { model: m, error, loading, reload, filters, second, setSecond } = useLandscape();
  const { view, mode } = parseLocation(loc.pathname, loc.search);
  const [trail, setTrail] = useState<NonNullable<View>[]>([]);
  const [restricted, setRestricted] = useState(false);
  const [pick, setPick] = useState<ConnPick>({ a: null, b: null, hover: null });
  const [variant, setVariant] = useState<'circle' | 'cluster'>(() => (window.innerWidth < 720 ? 'cluster' : 'circle'));
  const [panelEl, setPanelEl] = useState<HTMLElement | null>(null);
  const scroll = useRef<HTMLDivElement>(null);
  const [size, setSize] = useState({ w: 0, h: 0 });
  const prevView = useRef<View>(null);
  const [block, setBlock] = useState(isBlock);
  const lensView = view && view.kind !== 'team' ? view : null;
  const graph = !view && mode === 'graph';

  // breadcrumb trail: lens to lens pushes, a known crumb truncates, coming from the overview restarts
  const viewKey = view ? view.kind + ':' + view.id : '';
  useEffect(() => {
    const prev = prevView.current;
    prevView.current = view;
    setRestricted(false);
    if (!view || view.kind === 'team') return setTrail([]);
    setTrail((tr) => {
      const i = tr.findIndex((x) => sameView(x, view));
      if (i >= 0) return tr.slice(0, i + 1);
      return prev && prev.kind !== 'team' ? [...tr, view] : [view];
    });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [viewKey]);

  useLayoutEffect(() => {
    const el = scroll.current;
    if (!el) return;
    const measure = () => {
      setSize({ w: el.clientWidth, h: isBlock() ? 0 : el.clientHeight });
      setBlock(isBlock());
    };
    measure();
    const ro = new ResizeObserver(measure);
    ro.observe(el);
    return () => ro.disconnect();
  }, []);

  useLayoutEffect(() => {
    if (scroll.current) {
      scroll.current.scrollTop = 0;
      scroll.current.scrollLeft = 0;
    }
    if (isBlock()) window.scrollTo({ top: 0 });
  }, [viewKey, mode]);

  const goOverview = useCallback(() => nav(paths.explorer(mode)), [nav, mode]);
  const setMode = (md: OverviewMode) => {
    setPick({ a: null, b: null, hover: null });
    nav(paths.explorer(md));
  };

  // Esc returns to the overview from a lens
  useEffect(() => {
    const k = (e: KeyboardEvent) => {
      if (e.key !== 'Escape' || e.defaultPrevented) return;
      const tag = (e.target as HTMLElement).tagName;
      if (tag === 'INPUT' || tag === 'SELECT' || tag === 'TEXTAREA' || tag === 'CANVAS') return;
      if (restricted) setRestricted(false);
      else if (view) goOverview();
      else if (pick.a) setPick({ a: null, b: null, hover: null });
    };
    document.addEventListener('keydown', k);
    return () => document.removeEventListener('keydown', k);
  }, [view, restricted, goOverview, pick.a]);

  // drag the lens background to pan; a plain click returns to the overview
  const pan = useRef<{ x: number; y: number; sl: number; st: number; moved: boolean } | null>(null);
  const panned = useRef(false);
  const onDown = (e: PointerEvent) => {
    if (e.button !== 0 || !lensView || (e.target as HTMLElement).closest('button, a, .node, .pill, .fcard, .compact'))
      return;
    const el = scroll.current!;
    pan.current = { x: e.clientX, y: e.clientY, sl: el.scrollLeft, st: el.scrollTop, moved: false };
    panned.current = false;
  };
  useEffect(() => {
    const move = (e: PointerEvent) => {
      const p = pan.current;
      if (!p) return;
      const dx = e.clientX - p.x;
      const dy = e.clientY - p.y;
      if (!p.moved && Math.hypot(dx, dy) < 5) return;
      p.moved = panned.current = true;
      const el = scroll.current!;
      el.classList.add('panning');
      el.scrollLeft = p.sl - dx;
      el.scrollTop = p.st - dy;
    };
    const up = () => {
      pan.current = null;
      scroll.current?.classList.remove('panning');
      setTimeout(() => (panned.current = false), 0);
    };
    window.addEventListener('pointermove', move);
    window.addEventListener('pointerup', up);
    return () => {
      window.removeEventListener('pointermove', move);
      window.removeEventListener('pointerup', up);
    };
  }, []);
  const onBgClick = (e: MouseEvent) => {
    if (!lensView || panned.current) return;
    const tg = e.target as Element;
    if (
      tg === scroll.current ||
      tg.classList.contains('lens') ||
      tg.classList.contains('wires') ||
      tg.tagName === 'svg'
    )
      goOverview();
  };

  const [panHint, setPanHint] = useState(false);
  const compact = !!lensView && size.w > 0 && size.w < 1000;
  const updatePanHint = useCallback(() => {
    const sc = scroll.current;
    setPanHint(
      !!sc &&
        !!lensView &&
        !compact &&
        sc.scrollWidth > sc.clientWidth + 4 &&
        sc.scrollLeft + sc.clientWidth < sc.scrollWidth - 4,
    );
  }, [lensView, compact]);
  useEffect(() => {
    const id = window.setTimeout(updatePanHint, 340);
    return () => window.clearTimeout(id);
  }, [updatePanHint, viewKey, size, second]);

  // background listeners on the scroll area: pan, click to go back, pan hint
  const bg = useRef({ onDown, onBgClick, updatePanHint });
  bg.current = { onDown, onBgClick, updatePanHint };
  useEffect(() => {
    const el = scroll.current;
    if (!el) return;
    const d = (e: PointerEvent) => bg.current.onDown(e);
    const c = (e: MouseEvent) => bg.current.onBgClick(e);
    const s = () => bg.current.updatePanHint();
    el.addEventListener('pointerdown', d);
    el.addEventListener('click', c);
    el.addEventListener('scroll', s, { passive: true });
    return () => {
      el.removeEventListener('pointerdown', d);
      el.removeEventListener('click', c);
      el.removeEventListener('scroll', s);
    };
  }, []);

  const conn = useConnData();
  const appsShown = m ? visibleApps(m, filters).length : undefined;
  const filterUi = <FilterControl appsShown={!view ? appsShown : undefined} />;

  let stage: React.ReactNode;
  let panelChildren: React.ReactNode = null;
  if (loading && !m) stage = <Skeleton />;
  else if (error && !m)
    stage = (
      <div className="ov">
        <div className="banner danger" role="alert">
          <span className="bt">
            {error.status === 0 ? t.common.networkError : error.message || t.common.genericError}
          </span>
          <button type="button" className="btn" onClick={reload}>
            {t.common.retry}
          </button>
        </div>
      </div>
    );
  else if (m && !m.appIds.length)
    stage = (
      <div className="ov">
        <OvHead title={t.overview.title} sub={t.overview.empty} />
        <Link className="btn primary" to="/import-export">
          {t.overview.emptyAction}
        </Link>
      </div>
    );
  else if (m && lensView) {
    stage = compact ? (
      <CompactLens
        kind={lensView.kind as 'app' | 'flow' | 'object'}
        id={lensView.id}
        onRestricted={() => setRestricted(true)}
      />
    ) : (
      <Lens
        kind={lensView.kind as 'app' | 'flow' | 'object'}
        id={lensView.id}
        width={size.w}
        height={size.h}
        second={second}
        onRestricted={() => setRestricted(true)}
      />
    );
  } else if (m) {
    const seg = <OverviewSeg mode={mode} onMode={setMode} />;
    const team = view?.kind === 'team' ? view.id : null;
    if (graph) stage = <GraphView seg={seg} panelEl={panelEl} onOpenLens={(id) => nav(paths.appLens(id))} />;
    else if (mode === 'connections' && !team) {
      const np = conn ? conn.ps.filter((p) => p.w).length : 0;
      const head = (
        <OvHead title={t.conn.title} sub={t.conn.sub(t.overview.ofTotal(conn?.ids.length ?? 0, m.appIds.length), np)}>
          <div className="ov-ctl">
            {seg}
            <div className="seg sm" role="group" aria-label={t.overview.connLayout}>
              <button type="button" aria-pressed={variant === 'circle'} onClick={() => setVariant('circle')}>
                {t.overview.circle}
              </button>
              <button type="button" aria-pressed={variant === 'cluster'} onClick={() => setVariant('cluster')}>
                {t.overview.clusters}
              </button>
            </div>
          </div>
        </OvHead>
      );
      stage = <ConnectionsView variant={variant} head={head} pick={pick} setPick={setPick} />;
      panelChildren = <ConnectionsPanel pick={pick} setPick={setPick} />;
    } else {
      const total = m.appIds.length;
      const nf = m.flows.filter(makeMatcher(m, filters)).length;
      const sub = team ? (
        <>
          {t.overview.teamSub(m.appIds.filter((id) => fullApp(m, id)?.teamId === team).length)}{' '}
          <b>{m.teams.get(team)?.name ?? team}</b> {t.overview.teamSubEnd}
        </>
      ) : (
        t.overview.sub(
          t.overview.ofTotal(appsShown ?? total, total),
          anyFilter(filters) ? t.overview.ofTotal(nf, m.flows.length) : String(m.flows.length),
        )
      );
      const head = (
        <OvHead title={t.overview.title} sub={sub}>
          {!team && <div className="ov-ctl">{seg}</div>}
        </OvHead>
      );
      stage = <CardsView team={team} head={head} onRestricted={() => setRestricted(true)} />;
    }
  }

  const showLegend = !!m && !graph && !(mode === 'connections' && !view);
  const ccardOpen = block && !view && mode === 'connections' && !!(pick.a || pick.hover);
  useEffect(() => {
    document.body.classList.toggle('ccard-open', ccardOpen);
    return () => document.body.classList.remove('ccard-open');
  }, [ccardOpen]);
  const panelOwnedByGraph = graph && !restricted && !!m;

  return (
    <div className="shell">
      <TopBar filter={filterUi} />
      <div className="bar">
        <nav className="crumbs" aria-label="Breadcrumb">
          {view ? (
            <Link className="crumb" to={paths.explorer(mode)}>
              {t.lens.overview}
            </Link>
          ) : (
            <span className="crumb cur" aria-current="page">
              {t.lens.overview}
            </span>
          )}
          {m &&
            (view?.kind === 'team' ? [view] : trail).map((it, i, arr) => {
              const last = i === arr.length - 1;
              const label = crumbLabel(t, m, it);
              return (
                <span key={it.kind + it.id} style={{ display: 'contents' }}>
                  <span className="csep" aria-hidden="true">
                    ›
                  </span>
                  {last ? (
                    <span className="crumb cur" aria-current="page">
                      <span className={'kb ' + KB[it.kind][0]}>{t.search.badge[KB[it.kind][1] as 'app']}</span>
                      {label}
                    </span>
                  ) : (
                    <Link className="crumb" to={viewPath(it)}>
                      {label}
                    </Link>
                  )}
                </span>
              );
            })}
        </nav>
        <ActiveChips appsShown={!view ? appsShown : undefined} />
        <div className="bar-r">
          {lensView?.kind === 'app' && !compact && (
            <button
              type="button"
              className="switch"
              role="switch"
              aria-checked={second}
              onClick={() => setSecond(!second)}
            >
              <span className="tr" />
              {t.lens.secondLevel}
            </button>
          )}
          {view && (
            <button type="button" className="btn" onClick={goOverview}>
              <IconGrid />
              {t.lens.overview} <kbd className="kbd-esc">Esc</kbd>
            </button>
          )}
        </div>
      </div>
      <main className="main">
        <section className="stage" aria-label={t.brand.sub}>
          <div ref={scroll} className={'scroll' + (graph ? ' g-mode' : '')}>
            {stage}
          </div>
          {panHint && <div className="panhint">{t.lens.panHint}</div>}
          {showLegend && <Legend />}
          {ccardOpen && (
            <div className="ccard" aria-live="polite">
              <div className="ccard-in">
                <ConnCardBody pick={pick} setPick={setPick} />
              </div>
            </div>
          )}
        </section>
        <aside className="panel" aria-live="polite" ref={setPanelEl}>
          {!panelOwnedByGraph && m && (
            <Panel view={view} restricted={restricted} onRestricted={() => setRestricted(true)}>
              {panelChildren}
            </Panel>
          )}
          {!m && loading && (
            <div className="pn-in" aria-busy="true">
              <span className="sk title" style={{ width: '60%' }} />
              <span className="sk line" style={{ width: '90%', marginTop: 12 }} />
              <span className="sk block" style={{ height: 70, marginTop: 16 }} />
              <span className="sk block" style={{ height: 160, marginTop: 16 }} />
            </div>
          )}
        </aside>
      </main>
    </div>
  );
}
