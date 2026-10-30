// Overview graph: a two-stage spring-electrical layout (d3-force) drawn on a canvas.
// Stage A places each domain as a disc (a super-node sized by its application count, pulled by inter-domain links);
// stage B places applications inside their disc, toward the domains they talk to. Discs are kept hard-separated.
// Filter changes re-optimise from the current positions (warm start) and tween smoothly.
import {
  forceCollide,
  forceLink,
  forceManyBody,
  forceSimulation,
  forceX,
  forceY,
  type Simulation,
  type SimulationLinkDatum,
  type SimulationNodeDatum,
} from 'd3-force';
import { drag } from 'd3-drag';
import { easeCubicInOut, easeCubicOut } from 'd3-ease';
import { polygonContains, polygonHull } from 'd3-polygon';
import { select } from 'd3-selection';
import { curveBasisClosed, line } from 'd3-shape';
import 'd3-transition';
import { zoom, zoomIdentity, type ZoomBehavior, type ZoomTransform } from 'd3-zoom';
import { isDark } from '../../ui/theme';
import { dcol, hueOf, type ColorKind } from './palette';

/* ---------- input ---------- */

export interface GSys {
  id: string;
  /** Null for a restricted application. */
  name: string | null;
  domain: string;
  kind: string;
  team?: string;
  tech?: string;
}
export interface GFlow {
  id: string;
  from: string;
  to: string;
  type: string;
  name: string | null;
  objects: string[];
}
export interface GData {
  systems: GSys[];
  domains: { id: string; name: string }[];
  flows: GFlow[];
}
export interface GFilter {
  flowOK: (f: GFlow) => boolean;
  hidden: Set<string>;
  isolated: boolean;
}

export interface Params {
  spacing: number;
  tension: number;
  group: number;
  stability: number;
}
export const DEFAULTS: Params = { spacing: 1, tension: 1, group: 1, stability: 0.6 };

/* ---------- internal types ---------- */

interface GNode extends SimulationNodeDatum {
  id: string;
  s: GSys;
  x: number;
  y: number;
  vx: number;
  vy: number;
  r: number;
  tr: number;
  fr: number;
  r0?: number;
  dx: number;
  dy: number;
  fx0: number;
  fy0: number;
  tx: number;
  ty: number;
  a: number;
  fa: number;
  ta: number;
  placed: boolean;
  pinned: boolean;
  gx?: number;
  gy?: number;
  gdom?: string;
  px: number;
  py: number;
  entering: boolean;
  hw: number;
  hh: number;
  oy: number;
}
export interface GPair {
  key: string;
  a: string;
  b: string;
  flows: GFlow[];
}
interface GLink extends SimulationLinkDatum<GNode> {
  p: GPair;
}
export interface Graph {
  ids: string[];
  idset: Set<string>;
  links: GPair[];
  adj: Map<string, Map<string, GPair>>;
  deg: Map<string, number>;
  maxDeg: number;
}
interface Anchor {
  x: number;
  y: number;
  r: number;
  c?: number;
}
interface Disc {
  id: string;
  x: number;
  y: number;
  r: number;
}
export interface RunInfo {
  mode: Mode;
  ms: number;
  ticks: number;
  n: number;
  m: number;
}
type Mode = 'init' | 'reset' | 'filter' | 'params';

/** Positions and pins survive switching views: they are kept per landscape. */
export interface GModel {
  data: GData;
  sys: Map<string, GSys>;
  domName: Map<string, string>;
  order: string[];
  nodes: Map<string, GNode>;
  anchors: Record<string, Anchor>;
  anchorDoms?: string;
  domGap: number;
  kFit: number;
  first: boolean;
  layoutDefault: boolean;
  lastDoms?: Set<string>;
  params: Params;
  transform?: ZoomTransform;
}

export function createModel(data: GData): GModel {
  const nodes = new Map<string, GNode>();
  for (const s of data.systems)
    nodes.set(s.id, {
      id: s.id,
      s,
      r: 6,
      tr: 6,
      fr: 6,
      x: 0,
      y: 0,
      vx: 0,
      vy: 0,
      dx: 0,
      dy: 0,
      fx0: 0,
      fy0: 0,
      tx: 0,
      ty: 0,
      a: 0,
      fa: 0,
      ta: 0,
      placed: false,
      pinned: false,
      px: 0,
      py: 0,
      entering: false,
      hw: 0,
      hh: 0,
      oy: 0,
    });
  return {
    data,
    sys: new Map(data.systems.map((s) => [s.id, s])),
    domName: new Map(data.domains.map((d) => [d.id, d.name])),
    order: data.domains.map((d) => d.id),
    nodes,
    anchors: {},
    domGap: 40,
    kFit: 1,
    first: true,
    layoutDefault: true,
    params: { ...DEFAULTS },
  };
}

/* ---------- helpers ---------- */

const min = <T>(a: T[], f: (x: T) => number) => a.reduce((m, x) => Math.min(m, f(x)), Infinity);
const max = <T>(a: T[], f: (x: T) => number) => a.reduce((m, x) => Math.max(m, f(x)), -Infinity);
const mean = <T>(a: T[], f: (x: T) => number) => (a.length ? a.reduce((s, x) => s + f(x), 0) / a.length : 0);
function median(v: number[]) {
  if (!v.length) return 0;
  const s = [...v].sort((a, b) => a - b);
  const m = s.length >> 1;
  return s.length % 2 ? s[m] : (s[m - 1] + s[m]) / 2;
}
function hashId(id: string) {
  let h = 2166136261;
  for (const c of id) {
    h ^= c.charCodeAt(0);
    h = Math.imul(h, 16777619);
  }
  return (h >>> 0) / 4294967296;
}
export const isExt = (s: GSys) => s.kind === 'external' || s.domain === 'external';

const HP = 16; // hull padding around each app, in world units
const MIN_GAP_PX = 28; // clear space between domain discs at the fit zoom
const LABEL_H = 15;

interface Colors {
  dark: boolean;
  bg: string;
  surface: string;
  surface2: string;
  fg: string;
  fgMuted: string;
  fgSubtle: string;
  accent: string;
  agent: string;
  edge: string;
  font: string;
}

export interface Selection {
  hover: string | null;
  sel: string | null;
  sel2: string | null;
  path: string[] | null;
}

export interface EngineHost {
  /** Selection, graph or pins changed: the host re-renders its panel. */
  onChange: () => void;
  onRun: (run: RunInfo) => void;
  onBusy: (busy: boolean) => void;
  onOpenLens: (id: string) => void;
  onHover: (id: string | null, x: number, y: number) => void;
  toast: (kind: 'pinned' | 'unpinned', id: string) => void;
}

type Label =
  | { kind: 'region'; txt: string; r: Rect; dom: string; a: number }
  | { kind: 'node'; n: GNode; r: Rect; font: string; a: number; strong: boolean };
interface Rect {
  x: number;
  y: number;
  w: number;
  h: number;
}

export class GraphEngine {
  M: GModel;
  G: Graph | null = null;
  S: Selection = { hover: null, sel: null, sel2: null, path: null };
  pointer: 'mouse' | 'touch' = 'mouse';
  reduced = false;
  private filter: GFilter;
  private sim: Simulation<GNode, GLink> | null = null;
  private live = false;
  private tween: { t0: number; dur: number; done: (() => void) | null; t: number } | null = null;
  private edgeUnion: Map<string, { l: GPair; fa: number; ta: number }> | null = null;
  private prevEdges: GPair[] | null = null;
  private ctx: CanvasRenderingContext2D;
  private W = 0;
  private H = 0;
  private DPR = 1;
  transform: ZoomTransform = zoomIdentity;
  private zoomB: ZoomBehavior<HTMLCanvasElement, unknown>;
  private C!: Colors;
  private mcache = new Map<string, number>();
  private rafPending = false;
  private lastLabels: Label[] = [];
  private dragNode: GNode | null = null;
  private dragMoved = false;
  private ro: ResizeObserver;
  private disposed = false;
  private cleanup: (() => void)[] = [];

  constructor(
    private cv: HTMLCanvasElement,
    model: GModel,
    filter: GFilter,
    private host: EngineHost,
  ) {
    this.M = model;
    this.filter = filter;
    this.ctx = cv.getContext('2d')!;
    this.reduced = window.matchMedia?.('(prefers-reduced-motion: reduce)').matches === true;
    this.readTheme();
    this.zoomB = zoom<HTMLCanvasElement, unknown>()
      .scaleExtent([0.15, 5])
      .filter(
        (ev: Event & { ctrlKey?: boolean; button?: number }) =>
          (!ev.ctrlKey || ev.type === 'wheel') && !ev.button && ev.type !== 'dblclick',
      )
      .on('zoom', (ev: { transform: ZoomTransform }) => {
        this.transform = ev.transform;
        this.M.transform = ev.transform;
        this.host.onHover(null, 0, 0);
        this.draw();
        this.host.onChange();
      })
      .on('start', (ev: { sourceEvent?: Event }) => {
        if (ev.sourceEvent && ev.sourceEvent.type !== 'wheel') cv.classList.add('dragging');
      })
      .on('end', () => cv.classList.remove('dragging'));
    this.bindPointer();
    this.ro = new ResizeObserver(() => this.resize());
    this.ro.observe(cv.parentElement!);
    const onTheme = () => {
      this.readTheme();
      this.draw();
    };
    window.addEventListener('noos-theme', onTheme);
    const mq = window.matchMedia?.('(prefers-color-scheme: dark)');
    mq?.addEventListener?.('change', onTheme);
    this.cleanup.push(() => {
      window.removeEventListener('noos-theme', onTheme);
      mq?.removeEventListener?.('change', onTheme);
    });
    if (document.fonts)
      void document.fonts.ready.then(() => {
        this.mcache.clear();
        this.draw();
      });
    this.resize();
    if (this.M.transform && !this.M.first) {
      this.transform = this.M.transform;
      // oxlint-disable-next-line typescript/unbound-method -- d3 calls these with the selection as `this`
      select(cv).call(this.zoomB.transform, this.M.transform);
    }
    this.relayout(this.M.first ? 'init' : 'filter');
  }

  dispose() {
    this.disposed = true;
    this.sim?.stop();
    this.ro.disconnect();
    select(this.cv).on('.zoom', null).on('.drag', null);
    for (const f of this.cleanup) f();
    if (this.tween) this.finishTween();
  }

  /* ---------- theme ---------- */
  readTheme() {
    const cs = getComputedStyle(document.documentElement);
    const g = (n: string) => cs.getPropertyValue(n).trim();
    this.C = {
      dark: isDark(),
      bg: g('--bg'),
      surface: g('--surface'),
      surface2: g('--surface-2'),
      fg: g('--fg'),
      fgMuted: g('--fg-muted'),
      fgSubtle: g('--fg-subtle'),
      accent: g('--accent'),
      agent: g('--agent'),
      edge: g('--edge').split(' ').join(','),
      font: getComputedStyle(document.body).getPropertyValue('--font-sans') || 'Inter, system-ui, sans-serif',
    };
  }
  color(dom: string, kind: ColorKind = 'fill') {
    return dcol(hueOf(dom, this.M.order), kind, this.C.dark);
  }

  /* ---------- graph ---------- */
  setFilter(f: GFilter) {
    this.filter = f;
    this.relayout('filter');
  }
  private computeGraph(): Graph {
    const { hidden, isolated, flowOK } = this.filter;
    const sys = this.M.sys;
    const domOK = (s: GSys) => !hidden.has(s.domain);
    const flows = this.M.data.flows.filter((fl) => {
      const a = sys.get(fl.from);
      const b = sys.get(fl.to);
      if (!a || !b || fl.from === fl.to || !domOK(a) || !domOK(b)) return false;
      return flowOK(fl);
    });
    const pairs = new Map<string, GPair>();
    const adj = new Map<string, Map<string, GPair>>();
    for (const fl of flows) {
      const [a, b] = fl.from < fl.to ? [fl.from, fl.to] : [fl.to, fl.from];
      const k = a + '|' + b;
      let p = pairs.get(k);
      if (!p) {
        p = { key: k, a, b, flows: [] };
        pairs.set(k, p);
        if (!adj.has(a)) adj.set(a, new Map());
        if (!adj.has(b)) adj.set(b, new Map());
        adj.get(a)!.set(b, p);
        adj.get(b)!.set(a, p);
      }
      p.flows.push(fl);
    }
    const ids = this.M.data.systems
      .filter((s) => domOK(s) && (!isolated || (adj.get(s.id)?.size ?? 0) > 0))
      .map((s) => s.id);
    const idset = new Set(ids);
    const links = [...pairs.values()].filter((p) => idset.has(p.a) && idset.has(p.b));
    const deg = new Map(ids.map((id) => [id, adj.get(id)?.size ?? 0]));
    let maxDeg = 1;
    for (const d of deg.values()) maxDeg = Math.max(maxDeg, d);
    return { ids, idset, links, adj, deg, maxDeg };
  }
  nbrs(id: string): string[] {
    const G = this.G!;
    const m = G.adj.get(id);
    return m ? [...m.keys()].filter((x) => G.idset.has(x)) : [];
  }
  private radiusFor(deg: number) {
    return deg === 0 ? 5 : 6 + 9 * Math.sqrt(deg / Math.max(this.G!.maxDeg, 6));
  }

  /* ---------- stage A: domain discs ---------- */
  private domWeights(doms: string[]) {
    const w: Record<string, Record<string, number>> = {};
    for (const a of doms) {
      w[a] = {};
      for (const b of doms) w[a][b] = 0;
    }
    for (const l of this.G!.links) {
      const da = this.M.sys.get(l.a)!.domain;
      const db = this.M.sys.get(l.b)!.domain;
      if (da !== db && w[da] && w[db]) {
        w[da][db]++;
        w[db][da]++;
      }
    }
    return w;
  }
  private stageAspect() {
    return Math.max(0.65, Math.min(1.9, (this.W || 1000) / Math.max(1, (this.H || 700) - 30)));
  }
  private domRho(c: number) {
    return (26 + 34 * Math.sqrt(c)) * Math.sqrt(this.M.params.spacing);
  }
  private separateDiscs(dn: Disc[], gap: number) {
    for (let it = 0; it < 400; it++) {
      let moved = false;
      for (let i = 0; i < dn.length; i++)
        for (let j = i + 1; j < dn.length; j++) {
          const a = dn[i];
          const b = dn[j];
          let dx = b.x - a.x;
          let dy = b.y - a.y;
          let d = Math.hypot(dx, dy);
          const need = a.r + b.r + gap;
          if (d >= need - 1e-6) continue;
          if (d < 1e-6) {
            const t = hashId(a.id + b.id) * 2 * Math.PI;
            dx = Math.cos(t);
            dy = Math.sin(t);
            d = 1;
          }
          const m = (((need - d) / 2) * 1.002) / d;
          a.x -= dx * m;
          a.y -= dy * m;
          b.x += dx * m;
          b.y += dy * m;
          moved = true;
        }
      if (!moved) return true;
    }
    return false;
  }
  private estFitK(dn: Disc[]) {
    const x0 = min(dn, (d) => d.x - d.r) - 12;
    const x1 = max(dn, (d) => d.x + d.r) + 12;
    const y0 = min(dn, (d) => d.y - d.r) - 26;
    const y1 = max(dn, (d) => d.y + d.r) + 22;
    const w = this.W || 1000;
    const h = this.H || 700;
    const pad = w < 600 ? 14 : 32;
    const right = w < 600 ? 50 : 56;
    return Math.max(0.2, Math.min(1.6, Math.min((w - pad - right) / (x1 - x0), (h - 2 * pad - 22 - 34) / (y1 - y0))));
  }
  private settleDiscs(dn: Disc[], mode: Mode) {
    let gap = 40;
    for (let it = 0; it < 8; it++) {
      this.separateDiscs(dn, gap);
      let k = this.estFitK(dn);
      if (mode === 'filter' || mode === 'params') k = Math.min(k, this.transform.k);
      if (gap * k >= MIN_GAP_PX) break;
      gap = (MIN_GAP_PX / k) * 1.04;
    }
    this.M.domGap = gap;
  }
  private computeAnchors(mode: Mode): Record<string, Anchor> {
    const M = this.M;
    const counts: Record<string, number> = {};
    for (const id of this.G!.ids) {
      const d = M.sys.get(id)!.domain;
      counts[d] = (counts[d] || 0) + 1;
    }
    const fresh = mode === 'init' || mode === 'reset';
    const key = Object.keys(counts).sort().join();
    // filter changes that keep the same domains keep the domain map as it is (mental map)
    if (!fresh && Object.keys(counts).every((d) => M.anchors[d]) && M.anchorDoms && M.anchorDoms === key) {
      const dn = Object.keys(counts)
        .sort()
        .map((d) => {
          const A = M.anchors[d];
          const r = A.c
            ? Math.min(this.domRho(counts[d]), A.r * Math.sqrt(counts[d] / A.c) + 6)
            : this.domRho(counts[d]);
          return { id: d, x: A.x, y: A.y, r: Math.max(r, 30) };
        });
      this.settleDiscs(dn, mode);
      const keep: Record<string, Anchor> = {};
      for (const d of dn) keep[d.id] = { x: d.x, y: d.y, r: d.r, c: counts[d.id] };
      M.anchors = { ...M.anchors, ...keep };
      return keep;
    }
    M.anchorDoms = key;
    let order = M.order.filter((d) => counts[d]);
    const w = this.domWeights(order);
    const n = order.length;
    const asp = this.stageAspect();
    if (fresh) {
      // deterministic seed: ring order that keeps linked domains adjacent
      const cost = (o: string[]) => {
        let c = 0;
        for (let i = 0; i < n; i++)
          for (let j = i + 1; j < n; j++) {
            const dd = Math.min(j - i, n - (j - i));
            c += w[o[i]][o[j]] * dd;
          }
        return c;
      };
      let best = cost(order);
      let improved = true;
      let guard = 0;
      while (improved && guard++ < 60) {
        improved = false;
        for (let i = 0; i < n; i++)
          for (let j = i + 1; j < n; j++) {
            const o = order.slice();
            [o[i], o[j]] = [o[j], o[i]];
            const c = cost(o);
            if (c < best - 1e-9) {
              best = c;
              order = o;
              improved = true;
            }
          }
      }
    }
    const sp = Math.sqrt(M.params.spacing);
    const gap = 58 * sp;
    // hub domains (linked to many others, relative to size) seed the centre
    const strength: Record<string, number> = {};
    for (const a of order) strength[a] = order.reduce((t, b) => t + w[a][b], 0) / Math.sqrt(counts[a]);
    const med = median(order.map((d) => strength[d]));
    const hubs =
      fresh && n > 4
        ? order
            .filter((d) => strength[d] > 1.6 * med)
            .sort((a, b) => strength[b] - strength[a])
            .slice(0, 2)
        : [];
    const ringOrder = order.filter((d) => !hubs.includes(d));
    const total = ringOrder.reduce((a, d) => a + 2 * this.domRho(counts[d]) + gap, 0);
    const R = Math.max(total / (2 * Math.PI), 60);
    let cum = 0;
    const hubR = hubs.reduce((a, d) => a + this.domRho(counts[d]), 0);
    const dn: (Disc & SimulationNodeDatum)[] = ringOrder.map((d) => {
      const r = this.domRho(counts[d]);
      const th = (2 * Math.PI * (cum + r)) / total - Math.PI / 2;
      cum += 2 * r + gap;
      const prev = !fresh ? M.anchors[d] : undefined;
      const RR = R + hubR * 0.6;
      return {
        id: d,
        r,
        x: prev ? prev.x : RR * Math.cos(th) * Math.sqrt(asp),
        y: prev ? prev.y : (RR * Math.sin(th)) / Math.sqrt(asp),
      };
    });
    hubs.forEach((d, i) => {
      const r = this.domRho(counts[d]);
      const prev = !fresh ? M.anchors[d] : undefined;
      dn.push({ id: d, r, x: prev ? prev.x : hubs.length > 1 ? (i ? 1 : -1) * r * 1.05 : 0, y: prev ? prev.y : 0 });
    });
    if (dn.length > 1) {
      let maxW = 1;
      for (const a of order) for (const b of order) maxW = Math.max(maxW, w[a][b]);
      const dl: { source: string; target: string; w: number }[] = [];
      for (let i = 0; i < n; i++)
        for (let j = i + 1; j < n; j++) {
          const ww = w[order[i]][order[j]];
          if (ww > 0) dl.push({ source: order[i], target: order[j], w: ww / maxW });
        }
      type DL = { source: Disc; target: Disc; w: number };
      const ds = forceSimulation(dn)
        .stop()
        .force(
          'link',
          forceLink<Disc & SimulationNodeDatum, SimulationLinkDatum<Disc & SimulationNodeDatum> & { w: number }>(dl)
            .id((d) => d.id)
            .distance((l) => {
              const L = l as unknown as DL;
              return (L.source.r + L.target.r) * (1.02 - 0.1 * L.w) + gap * 0.5;
            })
            .strength((l) => 0.03 + 0.55 * Math.pow(l.w, 1.4)),
        )
        .force(
          'collide',
          forceCollide<Disc & SimulationNodeDatum>((d) => d.r + gap * 0.5)
            .strength(0.9)
            .iterations(3),
        )
        .force('x', forceX(0).strength(0.07 / asp))
        .force('y', forceY(0).strength(0.07 * asp))
        .velocityDecay(0.35);
      const a0 = fresh ? 1 : 0.35;
      const ticks = fresh ? 400 : 150;
      ds.alpha(a0)
        .alphaMin(0.001)
        .alphaDecay(1 - Math.pow(0.001 / a0, 1 / ticks));
      for (let i = 0; i < ticks + 5 && ds.alpha() > ds.alphaMin(); i++) ds.tick();
      if (fresh) {
        // stretch the shorter axis so the domain map matches the viewport aspect (gaps only grow, no overlap)
        const bx0 = min(dn, (d) => d.x - d.r);
        const bx1 = max(dn, (d) => d.x + d.r);
        const by0 = min(dn, (d) => d.y - d.r);
        const by1 = max(dn, (d) => d.y + d.r);
        const ba = (bx1 - bx0) / (by1 - by0);
        const cxs = max(dn, (d) => d.x) - min(dn, (d) => d.x);
        const cys = max(dn, (d) => d.y) - min(dn, (d) => d.y);
        if (ba < asp && cxs > 1) {
          const f = Math.max(1, Math.min(1.8, (asp * (by1 - by0) - (bx1 - bx0 - cxs)) / cxs));
          for (const d of dn) d.x *= f;
        } else if (ba > asp && cys > 1) {
          const f = Math.max(1, Math.min(1.8, ((bx1 - bx0) / asp - (by1 - by0 - cys)) / cys));
          for (const d of dn) d.y *= f;
        }
      }
    } else if (fresh && dn.length) {
      dn[0].x = 0;
      dn[0].y = 0;
    }
    this.settleDiscs(dn, mode);
    const pos: Record<string, Anchor> = {};
    for (const d of dn) pos[d.id] = { x: d.x, y: d.y, r: d.r, c: counts[d.id] };
    M.anchors = { ...M.anchors, ...pos };
    return pos;
  }

  /* ---------- stage B: targets inside each disc ---------- */
  private computeTargets(anchors: Record<string, Anchor>, blend: boolean) {
    const G = this.G!;
    for (const id of G.ids) {
      const n = this.M.nodes.get(id)!;
      const d = n.s.domain;
      const a = anchors[d];
      if (!a) continue;
      let vx = 0;
      let vy = 0;
      let ext = 0;
      const ps = this.nbrs(id);
      for (const p of ps) {
        const pd = this.M.sys.get(p)!.domain;
        if (pd === d || !anchors[pd]) continue;
        const b = anchors[pd];
        const L = Math.hypot(b.x - a.x, b.y - a.y) || 1;
        const wgt = G.adj.get(id)!.get(p)!.flows.length;
        vx += ((b.x - a.x) / L) * wgt;
        vy += ((b.y - a.y) / L) * wgt;
        ext++;
      }
      const L = Math.hypot(vx, vy);
      const frac = ps.length ? ext / ps.length : 0;
      const reach = L > 0 ? a.r * 0.7 * Math.pow(frac, 0.5) * Math.min(1, L / Math.max(1, ext) + 0.35) : 0;
      const gx = a.x + (L > 0 ? (vx / L) * reach : 0);
      const gy = a.y + (L > 0 ? (vy / L) * reach : 0);
      if (blend && n.gx != null && n.gy != null && n.gdom === this.M.anchorDoms) {
        n.gx = 0.4 * n.gx + 0.6 * gx;
        n.gy = 0.4 * n.gy + 0.6 * gy;
      } else {
        n.gx = gx;
        n.gy = gy;
      }
      n.gdom = this.M.anchorDoms;
    }
  }

  /* ---------- forces ---------- */
  private forceDomain() {
    let nodes: GNode[] = [];
    const M = this.M;
    const f = (alpha: number) => {
      const g = 0.2 * M.params.group;
      for (const n of nodes) {
        if (n.gx == null || n.gy == null) continue;
        n.vx += (n.gx - n.x) * g * alpha;
        n.vy += (n.gy - n.y) * g * alpha;
      }
    };
    f.initialize = (ns: GNode[]) => (nodes = ns);
    return f;
  }
  // hard constraint, applied last each tick: the next position of every app stays inside its domain disc
  private forceClamp(anchors: Record<string, Anchor>) {
    let nodes: GNode[] = [];
    const f = () => {
      for (const n of nodes) {
        const A = anchors[n.s.domain];
        if (!A || n.fx != null) continue;
        const lim = Math.max(0, A.r - n.r - HP);
        const px = n.x + n.vx - A.x;
        const py = n.y + n.vy - A.y;
        const d = Math.hypot(px, py);
        if (d > lim) {
          const s = lim / d;
          n.vx = A.x + px * s - n.x;
          n.vy = A.y + py * s - n.y;
        }
      }
    };
    f.initialize = (ns: GNode[]) => (nodes = ns);
    return f;
  }
  private forceStay(strength: number) {
    let nodes: GNode[] = [];
    const f = (alpha: number) => {
      for (const n of nodes) {
        if (n.entering) continue;
        n.vx += (n.px - n.x) * strength * alpha;
        n.vy += (n.py - n.y) * strength * alpha;
      }
    };
    f.initialize = (ns: GNode[]) => (nodes = ns);
    return f;
  }
  // collision on node disc + its label box (label sits under the node)
  private forceBoxes() {
    let nodes: GNode[] = [];
    const f = () => {
      const n = nodes.length;
      for (let i = 0; i < n; i++) {
        const a = nodes[i];
        for (let j = i + 1; j < n; j++) {
          const b = nodes[j];
          let dx = b.x + b.vx - (a.x + a.vx);
          const dy = b.y + b.vy + b.oy - (a.y + a.vy + a.oy);
          const ox = a.hw + b.hw - Math.abs(dx);
          if (ox <= 0) continue;
          const oy = a.hh + b.hh - Math.abs(dy);
          if (oy <= 0) continue;
          if (dx === 0 && dy === 0) dx = (hashId(a.id + b.id) - 0.5) * 1e-3;
          const wa = (b.hw * b.hh) / (a.hw * a.hh + b.hw * b.hh);
          const wb = 1 - wa;
          if (ox / (a.hw + b.hw) < oy / (a.hh + b.hh)) {
            const m = ox * 0.5 * Math.sign(dx || 1);
            a.vx -= m * wa;
            b.vx += m * wb;
          } else {
            const m = oy * 0.5 * Math.sign(dy || 1);
            a.vy -= m * wa;
            b.vy += m * wb;
          }
        }
      }
    };
    f.initialize = (ns: GNode[]) => (nodes = ns);
    return f;
  }
  private sizeBoxes(nodes: GNode[]) {
    const big = this.G!.ids.length > 40;
    const q = big ? 0.62 : 1;
    const font = '500 12.5px ' + this.C.font;
    for (const n of nodes) {
      const lw = n.s.name == null ? 16 : this.measure(n.s.name, font);
      n.hw = Math.max(n.r + 7, (lw * q) / 2 + 4);
      const lh = n.s.name == null ? 0 : LABEL_H * q;
      n.oy = lh / 2;
      n.hh = n.r + 5 + lh / 2;
    }
  }
  private buildSim(nodes: GNode[], links: GLink[], anchors: Record<string, Anchor>, stay: number) {
    this.sim?.stop();
    const P = this.M.params;
    const G = this.G!;
    const asp = this.stageAspect();
    const sim = forceSimulation<GNode, GLink>(nodes)
      .stop()
      .force(
        'link',
        forceLink<GNode, GLink>(links)
          .id((d) => d.id)
          .distance((l) => {
            const c = l.p.flows.length;
            const s = l.source as GNode;
            const t = l.target as GNode;
            return (46 + 26 * P.spacing) * (1.15 - 0.35 * Math.min(1, Math.log2(c) / 2.5)) + s.r + t.r;
          })
          .strength((l) => {
            const s = l.source as GNode;
            const t = l.target as GNode;
            const same = s.s.domain === t.s.domain;
            const base = 1 / Math.max(1, Math.min(G.deg.get(s.id) ?? 0, G.deg.get(t.id) ?? 0));
            return Math.min(1, base * P.tension * (same ? 1 : 0.32));
          }),
      )
      .force(
        'charge',
        forceManyBody<GNode>()
          .strength((n) => -(70 + 5 * n.r) * P.spacing)
          .theta(0.9)
          .distanceMax(700),
      )
      .force('boxes', this.forceBoxes())
      .force('domain', this.forceDomain())
      .force('cx', forceX<GNode>(0).strength(0.012 / asp))
      .force('cy', forceY<GNode>(0).strength(0.012 * asp))
      .velocityDecay(0.42);
    if (stay > 0) sim.force('stay', this.forceStay(stay));
    sim.force('clamp', this.forceClamp(anchors));
    this.sim = sim;
    return sim;
  }

  /* ---------- post-processing ---------- */
  private projectInside(n: { x: number; y: number; r: number }, A: Anchor) {
    const lim = Math.max(0, A.r - n.r - HP);
    const dx = n.x - A.x;
    const dy = n.y - A.y;
    const d = Math.hypot(dx, dy);
    if (d > lim) {
      const s = lim / (d || 1);
      n.x = A.x + dx * s;
      n.y = A.y + dy * s;
    }
  }
  // After the app layout: shrink every domain disc to the real extent of its apps, then pull the domains
  // together rigidly (aspect-aware) while keeping the hard separation. Apps move with their domain.
  private compactDomains(vis: GNode[], anchors: Record<string, Anchor>, full: boolean) {
    const by: Record<string, GNode[]> = {};
    for (const n of vis) (by[n.s.domain] ??= []).push(n);
    const dn = Object.entries(by).map(([d, ns]) => {
      const x0 = min(ns, (n) => n.x - n.r);
      const x1 = max(ns, (n) => n.x + n.r);
      const y0 = min(ns, (n) => n.y - n.r);
      const y1 = max(ns, (n) => n.y + n.r);
      const cx = (x0 + x1) / 2;
      const cy = (y0 + y1) / 2;
      const r = max(ns, (n) => Math.hypot(n.x - cx, n.y - cy) + n.r + HP) + 2;
      return { id: d, x: cx, y: cy, r, ox: cx, oy: cy, ns };
    });
    const asp = this.stageAspect();
    if (full && dn.length > 1) {
      for (let round = 0; round < 3; round++) {
        for (let it = 0; it < 80; it++) {
          const x0 = min(dn, (d) => d.x - d.r);
          const x1 = max(dn, (d) => d.x + d.r);
          const y0 = min(dn, (d) => d.y - d.r);
          const y1 = max(dn, (d) => d.y + d.r);
          const mx = (x0 + x1) / 2;
          const my = (y0 + y1) / 2;
          const ba = (x1 - x0) / (y1 - y0);
          const fx = ba > asp ? 0.97 : 0.99;
          const fy = ba > asp ? 0.99 : 0.97;
          for (const d of dn) {
            d.x = mx + (d.x - mx) * fx;
            d.y = my + (d.y - my) * fy;
          }
          this.separateDiscs(dn, this.M.domGap);
        }
        const k = this.estFitK(dn);
        if (this.M.domGap * k >= MIN_GAP_PX) break;
        this.M.domGap = (MIN_GAP_PX / k) * 1.04;
        this.separateDiscs(dn, this.M.domGap);
      }
    } else this.separateDiscs(dn, this.M.domGap);
    for (const d of dn) {
      const dx = d.x - d.ox;
      const dy = d.y - d.oy;
      for (const n of d.ns) {
        n.x += dx;
        n.y += dy;
        if (n.gx != null && n.gy != null) {
          n.gx += dx;
          n.gy += dy;
        }
        if (n.pinned) {
          n.fx = n.x;
          n.fy = n.y;
        }
      }
      anchors[d.id] = this.M.anchors[d.id] = { x: d.x, y: d.y, r: d.r, c: d.ns.length };
    }
  }
  // Hull aspect = longest / shortest extent of the padded app discs (checked over 36 directions).
  private domAspect(ns: GNode[]) {
    let worst = 1;
    let best = Infinity;
    for (let i = 0; i < 36; i++) {
      const t = (i / 36) * Math.PI;
      const c = Math.cos(t);
      const s = Math.sin(t);
      let lo = Infinity;
      let hi = -Infinity;
      for (const n of ns) {
        const p = n.x * c + n.y * s;
        const a = n.r + HP;
        lo = Math.min(lo, p - a);
        hi = Math.max(hi, p + a);
      }
      worst = Math.max(worst, hi - lo);
      best = Math.min(best, hi - lo);
    }
    return worst / best;
  }
  // No slivers: squeeze a domain along its long axis, or fall back to a ring, until its aspect is <= 1.65.
  private roundDomains(vis: GNode[], anchors: Record<string, Anchor>) {
    const by: Record<string, GNode[]> = {};
    for (const n of vis) (by[n.s.domain] ??= []).push(n);
    for (const [d, ns] of Object.entries(by)) {
      const A = anchors[d];
      if (!A || ns.length < 2 || this.domAspect(ns) <= 1.65) continue;
      const free = ns.filter((n) => !n.pinned);
      const cx = mean(ns, (n) => n.x);
      const cy = mean(ns, (n) => n.y);
      let sxx = 0;
      let syy = 0;
      let sxy = 0;
      for (const n of ns) {
        const x = n.x - cx;
        const y = n.y - cy;
        sxx += x * x;
        syy += y * y;
        sxy += x * y;
      }
      const th = 0.5 * Math.atan2(2 * sxy, sxx - syy);
      const ux = Math.cos(th);
      const uy = Math.sin(th);
      const orig = ns.map((n) => [n.x, n.y] as const);
      const minOk = () => {
        for (let i = 0; i < ns.length; i++)
          for (let j = i + 1; j < ns.length; j++)
            if (Math.hypot(ns[i].x - ns[j].x, ns[i].y - ns[j].y) < ns[i].r + ns[j].r + 6) return false;
        return true;
      };
      let ok = false;
      for (let sc = 0.9; sc >= 0.15; sc -= 0.05) {
        for (const n of free) {
          const i = ns.indexOf(n);
          const x = orig[i][0] - cx;
          const y = orig[i][1] - cy;
          const u = x * ux + y * uy;
          n.x = orig[i][0] - ux * u * (1 - sc);
          n.y = orig[i][1] - uy * u * (1 - sc);
        }
        if (this.domAspect(ns) <= 1.65) {
          ok = minOk();
          break;
        }
      }
      if (!ok && free.length) {
        // ring around the centroid, keeping each app's angular order
        const ang = (n: GNode) => Math.atan2(orig[ns.indexOf(n)][1] - cy, orig[ns.indexOf(n)][0] - cx);
        const order = free.slice().sort((a, b) => ang(a) - ang(b));
        const m = order.length;
        const rmax = max(order, (n) => n.r);
        let rho = m === 1 ? 0 : m === 2 ? rmax + 4 : (2 * rmax + 8) / (2 * Math.sin(Math.PI / m));
        rho = Math.min(rho, Math.max(0, A.r - rmax - HP));
        let ccx = cx;
        let ccy = cy;
        const dd = Math.hypot(cx - A.x, cy - A.y);
        const room = A.r - rmax - HP - rho;
        if (dd > room) {
          const f = Math.max(0, room) / (dd || 1);
          ccx = A.x + (cx - A.x) * f;
          ccy = A.y + (cy - A.y) * f;
        }
        const a0 = ang(order[0]);
        order.forEach((n, i) => {
          const a = a0 + (2 * Math.PI * i) / m;
          n.x = ccx + rho * Math.cos(a);
          n.y = ccy + rho * Math.sin(a);
        });
      }
      for (const n of ns) this.projectInside(n, A);
    }
  }

  /* ---------- layout run: off-screen ticks, then tween ---------- */
  relayout(mode: Mode) {
    if (this.disposed) return;
    const M = this.M;
    const t0 = performance.now();
    if (this.tween) this.finishTween();
    if (this.live) {
      this.sim?.stop();
      this.live = false;
    }
    const G = (this.G = this.computeGraph());
    for (const id of G.ids) M.nodes.get(id)!.r = this.radiusFor(G.deg.get(id) ?? 0);
    const anchors = this.computeAnchors(mode);
    this.computeTargets(anchors, mode === 'filter');
    const vis = G.ids.map((id) => M.nodes.get(id)!);
    const fresh = mode === 'init' || mode === 'reset';
    if (fresh)
      for (const n of M.nodes.values()) {
        n.pinned = false;
        n.fx = n.fy = null;
      }
    const domIdx: Record<string, number> = {};
    const domWasVisible = new Set([...M.nodes.values()].filter((n) => n.placed && n.a > 0.01).map((n) => n.s.domain));
    vis.forEach((n, i) => {
      const wasVisible = n.placed && n.a > 0.01;
      n.entering = !wasVisible && !fresh;
      const gx = n.gx ?? 0;
      const gy = n.gy ?? 0;
      if (fresh) {
        // seeded placement: golden-angle spiral around the stage-B target
        const k = (domIdx[n.s.domain] = (domIdx[n.s.domain] || 0) + 1);
        const ang = k * 2.39996;
        const rr = 9 * Math.sqrt(k);
        n.x = gx + rr * Math.cos(ang);
        n.y = gy + rr * Math.sin(ang);
      } else if (wasVisible) {
        n.x = n.dx;
        n.y = n.dy;
      } else {
        // entering: average of visible neighbours, blended with the domain target
        const ps = this.nbrs(n.id)
          .map((id) => M.nodes.get(id)!)
          .filter((m) => m.placed && m.a > 0.01 && m.s.domain === n.s.domain);
        let x = gx;
        let y = gy;
        if (!domWasVisible.has(n.s.domain)) {
          const ang = i * 2.39996;
          const rr = 14 * Math.sqrt((i % 40) + 1);
          x += rr * Math.cos(ang);
          y += rr * Math.sin(ang);
        } else if (ps.length) {
          x = 0.5 * gx + 0.5 * mean(ps, (m) => m.dx);
          y = 0.5 * gy + 0.5 * mean(ps, (m) => m.dy);
        }
        const j = hashId(n.id) * Math.PI * 2;
        n.x = x + 18 * Math.cos(j);
        n.y = y + 18 * Math.sin(j);
      }
      if (n.pinned) {
        n.fx = n.x;
        n.fy = n.y;
      } else n.fx = n.fy = null;
      n.px = n.x;
      n.py = n.y;
      n.vx = 0;
      n.vy = 0;
    });
    this.sizeBoxes(vis);
    const links: GLink[] = G.links.map((p) => ({ source: p.a, target: p.b, p }));
    const domsNow = new Set(G.ids.map((id) => M.sys.get(id)!.domain));
    const domChanged =
      !!M.lastDoms && (M.lastDoms.size !== domsNow.size || [...domsNow].some((d) => !M.lastDoms!.has(d)));
    M.lastDoms = domsNow;
    const stab = M.params.stability;
    const stay = mode === 'filter' ? (domChanged ? 0.35 : 0.8) * stab : 0;
    const sim = this.buildSim(vis, links, anchors, stay);
    let alpha = fresh ? 1 : mode === 'params' ? 0.5 : 0.55 - 0.42 * stab;
    if (domChanged && !fresh) alpha = Math.max(alpha, 0.5 - 0.2 * stab);
    const ticks = fresh ? 320 : 180;
    sim
      .alpha(alpha)
      .alphaMin(0.002)
      .alphaDecay(1 - Math.pow(0.002 / alpha, 1 / ticks));
    let k = 0;
    while (sim.alpha() > sim.alphaMin() && k < 600) {
      sim.tick();
      k++;
    }
    for (const n of vis) {
      const A = anchors[n.s.domain];
      if (A) this.projectInside(n, A);
      if (n.pinned) {
        n.fx = n.x;
        n.fy = n.y;
      }
    }
    this.roundDomains(vis, anchors);
    this.compactDomains(vis, anchors, fresh);
    const run: RunInfo = { mode, ms: Math.round(performance.now() - t0), ticks: k, n: vis.length, m: G.links.length };
    // tween targets
    for (const n of M.nodes.values()) {
      n.fx0 = n.dx;
      n.fy0 = n.dy;
      n.fa = n.a;
      n.fr = n.r0 ?? n.r;
      if (G.idset.has(n.id)) {
        n.tx = n.x;
        n.ty = n.y;
        n.ta = 1;
        n.tr = n.r;
        if (!n.placed || n.a <= 0.01 || (fresh && M.first)) {
          n.fx0 = n.tx;
          n.fy0 = n.ty;
          n.fr = n.r;
        }
        n.placed = true;
      } else {
        n.tx = n.dx;
        n.ty = n.dy;
        n.ta = 0;
        n.tr = n.r0 ?? n.r;
      }
    }
    // edge fades
    const newKeys = new Set(G.links.map((l) => l.key));
    const eu = new Map<string, { l: GPair; fa: number; ta: number }>();
    for (const l of this.prevEdges ?? []) eu.set(l.key, { l, fa: 1, ta: newKeys.has(l.key) ? 1 : 0 });
    for (const l of G.links) {
      const e = eu.get(l.key);
      if (e) e.l = l;
      else eu.set(l.key, { l, fa: 0, ta: 1 });
    }
    this.edgeUnion = eu;
    this.prevEdges = G.links;
    this.host.onRun(run);
    const wantFit = M.first || fresh;
    const dur = M.first || this.reduced ? 0 : fresh ? 750 : 650;
    this.startTween(dur, () => {
      this.edgeUnion = null;
      this.host.onChange();
    });
    if (wantFit) this.fit(dur);
    else this.maybeRefit();
    M.first = false;
    M.layoutDefault = fresh;
    const S = this.S;
    if (S.sel && !G.idset.has(S.sel)) {
      S.sel = null;
      S.sel2 = null;
    }
    if (S.sel2 && !G.idset.has(S.sel2)) S.sel2 = null;
    this.computePath();
    this.host.onChange();
  }

  /* ---------- tween ---------- */
  private startTween(dur: number, done: (() => void) | null) {
    const t0 = performance.now();
    this.tween = { t0, dur, done, t: 0 };
    const step = (now: number) => {
      if (!this.tween || this.disposed) return;
      const t = dur ? Math.min(1, (now - t0) / dur) : 1;
      const e = easeCubicInOut(t);
      this.tween.t = e;
      for (const n of this.M.nodes.values()) {
        n.dx = n.fx0 + (n.tx - n.fx0) * e;
        n.dy = n.fy0 + (n.ty - n.fy0) * e;
        n.a = n.fa + (n.ta - n.fa) * (n.ta > n.fa ? easeCubicOut(t) : e);
        n.r0 = n.fr + (n.tr - n.fr) * e;
      }
      this.draw();
      if (t < 1) requestAnimationFrame(step);
      else {
        const d = this.tween.done;
        this.tween = null;
        d?.();
        this.draw();
      }
    };
    requestAnimationFrame(step);
  }
  private finishTween() {
    if (!this.tween) return;
    for (const n of this.M.nodes.values()) {
      n.dx = n.tx;
      n.dy = n.ty;
      n.a = n.ta;
      n.r0 = n.tr;
    }
    const d = this.tween.done;
    this.tween = null;
    d?.();
  }

  /* ---------- live simulation (drag) ---------- */
  private startLive() {
    if (this.tween) this.finishTween();
    const G = this.G!;
    const vis = G.ids.map((id) => this.M.nodes.get(id)!);
    for (const n of vis) {
      n.x = n.dx;
      n.y = n.dy;
      n.vx = n.vy = 0;
      n.px = n.x;
      n.py = n.y;
      n.entering = false;
      if (!n.pinned && n !== this.dragNode) n.fx = n.fy = null;
    }
    if (!this.live) {
      const sim = this.buildSim(
        vis,
        G.links.map((p) => ({ source: p.a, target: p.b, p })),
        this.M.anchors,
        0.25,
      );
      sim.alphaMin(0.004).alphaDecay(0.04);
      sim.on('tick', () => {
        for (const n of vis) {
          n.dx = n.x;
          n.dy = n.y;
          n.tx = n.x;
          n.ty = n.y;
        }
        this.draw();
      });
      sim.on('end', () => {
        this.live = false;
        this.host.onBusy(false);
        const vis2 = this.G!.ids.map((id) => this.M.nodes.get(id)!);
        for (const n of vis2) {
          n.x = n.dx;
          n.y = n.dy;
        }
        this.roundDomains(vis2, this.M.anchors);
        if (vis2.some((n) => Math.abs(n.x - n.dx) > 0.5 || Math.abs(n.y - n.dy) > 0.5)) {
          for (const n of this.M.nodes.values()) {
            n.fx0 = n.dx;
            n.fy0 = n.dy;
            n.fa = n.a;
            n.fr = n.r0 ?? n.r;
            n.tr = n.fr;
            if (this.G!.idset.has(n.id)) {
              n.tx = n.x;
              n.ty = n.y;
              n.ta = 1;
            } else {
              n.tx = n.dx;
              n.ty = n.dy;
              n.ta = n.a;
            }
          }
          this.startTween(this.reduced ? 0 : 320, null);
        }
      });
    }
    this.live = true;
    this.host.onBusy(true);
    this.sim!.alpha(Math.max(this.sim!.alpha(), 0.18)).alphaTarget(0.06).restart();
  }
  private coolLive() {
    if (this.live) this.sim?.alphaTarget(0);
  }

  /* ---------- canvas, zoom ---------- */
  resize() {
    const st = this.cv.parentElement;
    if (!st || this.disposed) return;
    const r = st.getBoundingClientRect();
    if (!r.width || !r.height) return;
    this.DPR = window.devicePixelRatio || 1;
    const wasDefault = !!this.G && this.isDefaultNow() && r.width !== this.W;
    if (this.W && this.H && (r.width !== this.W || r.height !== this.H) && this.G) {
      this.transform = this.transform.translate(
        (r.width - this.W) / 2 / this.transform.k,
        (r.height - this.H) / 2 / this.transform.k,
      );
      (this.cv as HTMLCanvasElement & { __zoom?: ZoomTransform }).__zoom = this.transform;
    }
    this.W = r.width;
    this.H = r.height;
    if (wasDefault) {
      const t = this.fitTransform();
      this.transform = t;
      (this.cv as HTMLCanvasElement & { __zoom?: ZoomTransform }).__zoom = t;
    }
    this.cv.width = Math.round(this.W * this.DPR);
    this.cv.height = Math.round(this.H * this.DPR);
    if (this.S.sel) requestAnimationFrame(() => this.ensureVisible(this.S.sel!));
    this.draw();
  }
  toScreen(x: number, y: number): [number, number] {
    return [this.transform.applyX(x), this.transform.applyY(y)];
  }
  private rScale() {
    return Math.pow(this.transform.k, 0.55);
  }
  private screenR(n: GNode) {
    return (n.r0 ?? n.r) * this.rScale();
  }
  private contentBox() {
    const ns = this.G!.ids.map((id) => this.M.nodes.get(id)!);
    if (!ns.length) return null;
    let x0 = Infinity;
    let y0 = Infinity;
    let x1 = -Infinity;
    let y1 = -Infinity;
    for (const n of ns) {
      const a = n.r + HP;
      x0 = Math.min(x0, n.tx - a - 6);
      x1 = Math.max(x1, n.tx + a + 6);
      y0 = Math.min(y0, n.ty - a - 20);
      y1 = Math.max(y1, n.ty + a + 16);
    }
    return { x0, y0, x1, y1 };
  }
  private fitTransform() {
    const b = this.G && this.contentBox();
    if (!b) return zoomIdentity;
    const { W, H } = this;
    const pad = W < 600 ? 14 : 32;
    const top = 22;
    const bottom = 34;
    const right = W < 600 ? 50 : 56;
    const k = Math.max(
      0.2,
      Math.min(1.6, Math.min((W - pad - right) / (b.x1 - b.x0), (H - 2 * pad - top - bottom) / (b.y1 - b.y0))),
    );
    return zoomIdentity
      .translate(
        pad + (W - pad - right) / 2 - (k * (b.x0 + b.x1)) / 2,
        top + pad + (H - 2 * pad - top - bottom) / 2 - (k * (b.y0 + b.y1)) / 2,
      )
      .scale(k);
  }
  isDefaultNow() {
    const M = this.M;
    if (!M.layoutDefault || this.tween) return M.layoutDefault && !!this.tween;
    if ([...M.nodes.values()].some((n) => n.pinned)) return false;
    if ((Object.keys(DEFAULTS) as (keyof Params)[]).some((k) => M.params[k] !== DEFAULTS[k])) return false;
    const f = this.fitTransform();
    const t = this.transform;
    return Math.abs(t.k / f.k - 1) < 0.02 && Math.abs(t.x - f.x) < 6 && Math.abs(t.y - f.y) < 6;
  }
  private zoomTo(t: ZoomTransform, dur: number, ease = easeCubicInOut) {
    const sel = select(this.cv);
    // oxlint-disable-next-line typescript/unbound-method -- d3 calls these with the selection as `this`
    if (dur && !this.reduced) sel.transition().duration(dur).ease(ease).call(this.zoomB.transform, t);
    // oxlint-disable-next-line typescript/unbound-method -- d3 calls these with the selection as `this`
    else sel.call(this.zoomB.transform, t);
  }
  fit(dur = 450) {
    const t = this.fitTransform();
    this.M.kFit = t.k;
    this.zoomTo(t, dur);
  }
  private maybeRefit() {
    const b = this.contentBox();
    if (!b) return;
    const [x0, y0] = this.toScreen(b.x0, b.y0);
    const [x1, y1] = this.toScreen(b.x1, b.y1);
    const out = x0 < -40 || y0 < -40 || x1 > this.W + 40 || y1 > this.H + 10;
    const small = (x1 - x0) * (y1 - y0) < 0.3 * this.W * this.H;
    if (out || small) this.fit(650);
  }
  zoomBy(f: number) {
    const sel = select(this.cv);
    // oxlint-disable-next-line typescript/unbound-method -- d3 calls these with the selection as `this`
    if (this.reduced) sel.call(this.zoomB.scaleBy, f);
    // oxlint-disable-next-line typescript/unbound-method -- d3 calls these with the selection as `this`
    else sel.transition().duration(250).call(this.zoomB.scaleBy, f);
  }
  ensureVisible(id: string) {
    const n = this.M.nodes.get(id);
    if (!n) return;
    const [x, y] = this.toScreen(n.tx, n.ty);
    const m = 50;
    if (x > m && x < this.W - m && y > m && y < this.H - 40 - m) return;
    const k = this.transform.k;
    this.zoomTo(
      zoomIdentity.translate(this.W / 2 - k * n.tx, (this.H - 30) / 2 - k * n.ty).scale(k),
      400,
      easeCubicOut,
    );
  }
  focusOn(id: string) {
    const n = this.M.nodes.get(id);
    if (!n) return;
    requestAnimationFrame(() => {
      const k = Math.max(this.transform.k, Math.min(1.4, (this.M.kFit || 1) * 2.2));
      this.zoomTo(zoomIdentity.translate(this.W / 2 - k * n.tx, (this.H - 30) / 2 - k * n.ty).scale(k), 600);
    });
  }
  private measure(t: string, font: string) {
    const k = font + '|' + t;
    let w = this.mcache.get(k);
    if (w == null) {
      this.ctx.font = font;
      w = this.ctx.measureText(t).width;
      this.mcache.set(k, w);
    }
    return w;
  }

  /* ---------- selection ---------- */
  focusInfo() {
    const G = this.G;
    const S = this.S;
    if (!G) return null;
    const h = S.hover && G.idset.has(S.hover) ? S.hover : null;
    if (h) return { center: h as string | null, set: new Set([h, ...this.nbrs(h)]), kind: 'hover' as const };
    if (S.sel && S.sel2) return { center: null, set: new Set(S.path ?? [S.sel, S.sel2]), kind: 'pair' as const };
    if (S.sel)
      return { center: S.sel as string | null, set: new Set([S.sel, ...this.nbrs(S.sel)]), kind: 'sel' as const };
    return null;
  }
  computePath() {
    const S = this.S;
    S.path = null;
    if (!S.sel || !S.sel2 || !this.G) return;
    const prev = new Map<string, string | null>([[S.sel, null]]);
    const q = [S.sel];
    while (q.length) {
      const u = q.shift()!;
      if (u === S.sel2) break;
      for (const v of this.nbrs(u))
        if (!prev.has(v)) {
          prev.set(v, u);
          q.push(v);
        }
    }
    if (!prev.has(S.sel2)) return;
    const p: string[] = [];
    for (let u: string | null = S.sel2; u; u = prev.get(u) ?? null) p.unshift(u);
    S.path = p;
  }
  /** Click semantics: select; a second app checks the connection; the same app again opens its lens. */
  clickNode(id: string) {
    const S = this.S;
    if (S.sel2 && id === S.sel2) return this.host.onOpenLens(id);
    if (!S.sel || S.sel2) {
      S.sel = id;
      S.sel2 = null;
    } else if (S.sel === id) return this.host.onOpenLens(id);
    else S.sel2 = id;
    if (this.pointer === 'touch') S.hover = null;
    this.host.onHover(null, 0, 0);
    this.computePath();
    this.host.onChange();
    this.draw();
  }
  select(id: string | null) {
    this.S.sel = id;
    this.S.sel2 = null;
    this.computePath();
    this.host.onChange();
    this.draw();
  }
  setHover(id: string | null) {
    if (this.S.hover === id) return;
    this.S.hover = id;
    this.draw();
  }
  unpin(id: string) {
    const n = this.M.nodes.get(id);
    if (!n) return;
    n.pinned = false;
    n.fx = n.fy = null;
    this.M.layoutDefault = false;
    this.startLive();
    setTimeout(() => this.coolLive(), 250);
    this.host.onChange();
  }
  isPinned(id: string) {
    return this.M.nodes.get(id)?.pinned === true;
  }
  setParams(p: Params, relayout: boolean) {
    this.M.params = { ...p };
    if (relayout) this.relayout('params');
    else this.host.onChange();
  }
  resetLayout() {
    this.M.params = { ...DEFAULTS };
    this.S.hover = null;
    this.relayout('reset');
  }

  /* ---------- drawing ---------- */
  draw() {
    if (this.rafPending || this.disposed) return;
    this.rafPending = true;
    requestAnimationFrame(() => this.render());
  }
  private render() {
    this.rafPending = false;
    const { M, G, C, ctx, W, H, S } = this;
    if (!G || this.disposed) return;
    ctx.setTransform(this.DPR, 0, 0, this.DPR, 0, 0);
    ctx.clearRect(0, 0, W, H);
    const fi = this.focusInfo();
    const k = this.transform.k;
    const all = [...M.nodes.values()].filter((n) => n.a > 0.005);
    const P = new Map<string, [number, number]>();
    for (const n of all) P.set(n.id, this.toScreen(n.dx, n.dy));
    const hulls: Record<string, [number, number][]> = {};
    const cenS: Record<string, [number, number]> = {};
    const regionLabels: {
      dom: string;
      tx: number;
      y: number;
      a: number;
      n: number;
      vx0: number;
      vx1: number;
      hull: [number, number][];
    }[] = [];
    const hullLine = line().curve(curveBasisClosed).context(ctx);

    // 1. domain regions (smoothed hulls)
    const byDom: Record<string, GNode[]> = {};
    for (const n of all) if (G.idset.has(n.id) || n.a > 0.3) (byDom[n.s.domain] ??= []).push(n);
    for (const [dom, ns] of Object.entries(byDom)) {
      const pts: [number, number][] = [];
      for (const n of ns) {
        const [x, y] = P.get(n.id)!;
        const pr = ((n.r0 ?? n.r) + HP) * k;
        for (let i = 0; i < 16; i++) {
          const a = (i / 16) * 2 * Math.PI;
          pts.push([x + pr * Math.cos(a), y + pr * Math.sin(a)]);
        }
      }
      const hull = polygonHull(pts);
      if (!hull) continue;
      hulls[dom] = hull;
      cenS[dom] = [mean(ns, (n) => P.get(n.id)![0]), mean(ns, (n) => P.get(n.id)![1])];
      const alphaDom = mean(ns, (n) => n.a);
      ctx.globalAlpha = alphaDom * (fi ? 0.55 : 1);
      ctx.beginPath();
      hullLine(hull);
      ctx.fillStyle = this.color(dom, 'hull');
      ctx.fill();
      ctx.lineWidth = 1;
      ctx.strokeStyle = this.color(dom, 'hullLine');
      ctx.stroke();
      ctx.globalAlpha = 1;
      let top = hull[0];
      for (const p of hull) if (p[1] < top[1]) top = p;
      const xs = hull.map((p) => p[0]);
      const bot = hull.reduce((a, p) => (p[1] > a[1] ? p : a), hull[0]);
      const vx0 = Math.max(Math.min(...xs), 6);
      const vx1 = Math.min(Math.max(...xs), W - 70);
      if (vx1 - vx0 < 60 || bot[1] < 30 || top[1] > H - 40) continue;
      regionLabels.push({ dom, tx: top[0], y: top[1], a: alphaDom, n: ns.length, vx0, vx1, hull });
    }

    // 2. edges
    const drawEdge = (l: GPair, alpha: number, color: string, width: number) => {
      const a = M.nodes.get(l.a)!;
      const b = M.nodes.get(l.b)!;
      if (!P.has(a.id) || !P.has(b.id)) return;
      const [x1, y1] = P.get(a.id)!;
      const [x2, y2] = P.get(b.id)!;
      ctx.globalAlpha = alpha * Math.min(a.a, b.a);
      ctx.strokeStyle = color;
      ctx.lineWidth = width;
      ctx.beginPath();
      ctx.moveTo(x1, y1);
      const ca = cenS[a.s.domain];
      const cb = cenS[b.s.domain];
      if (a.s.domain !== b.s.domain && ca && cb) {
        // faint bundling: bend toward the line joining the two domain centres
        const mx = (x1 + x2) / 2;
        const my = (y1 + y2) / 2;
        const cx = (ca[0] + cb[0]) / 2;
        const cy = (ca[1] + cb[1]) / 2;
        ctx.quadraticCurveTo(mx + (cx - mx) * 0.55, my + (cy - my) * 0.55, x2, y2);
      } else ctx.lineTo(x2, y2);
      ctx.stroke();
    };
    const wOf = (l: GPair) => (0.9 + 0.95 * Math.log2(l.flows.length)) * Math.max(0.85, Math.min(1.5, Math.sqrt(k)));
    const tw = this.tween ? this.tween.t : 1;
    const edges = this.edgeUnion
      ? [...this.edgeUnion.values()].map((e) => ({ l: e.l, m: e.fa + (e.ta - e.fa) * tw }))
      : G.links.map((l) => ({ l, m: 1 }));
    const pathKeys = new Set<string>();
    if (S.path)
      for (let i = 1; i < S.path.length; i++) {
        const [a, b] = S.path[i - 1] < S.path[i] ? [S.path[i - 1], S.path[i]] : [S.path[i], S.path[i - 1]];
        pathKeys.add(a + '|' + b);
      }
    ctx.lineCap = 'round';
    const hiEdges: GPair[] = [];
    for (const { l, m } of edges) {
      if (m <= 0.01) continue;
      const same = M.sys.get(l.a)!.domain === M.sys.get(l.b)!.domain;
      let alpha = (same ? 0.55 : 0.22) * (C.dark ? 0.85 : 1);
      if (fi) {
        const inc = fi.center ? l.a === fi.center || l.b === fi.center : pathKeys.has(l.key);
        if (inc) {
          hiEdges.push(l);
          continue;
        }
        alpha = 0.07;
      }
      drawEdge(l, alpha * m, `rgb(${C.edge})`, wOf(l));
    }
    for (const l of hiEdges) {
      const onPath = pathKeys.has(l.key);
      drawEdge(l, 0.95, onPath ? C.agent : C.accent, wOf(l) + (onPath ? 1.6 : 0.8));
    }
    if (fi && fi.kind !== 'pair' && S.path)
      S.path.forEach((id, i) => {
        if (!i) return;
        const k2 = [S.path![i - 1], id].sort().join('|');
        const l = G.links.find((x) => x.key === k2);
        if (l && !hiEdges.includes(l)) drawEdge(l, 0.9, C.agent, wOf(l) + 1.6);
      });
    ctx.globalAlpha = 1;

    // 3. nodes
    const inSet = (id: string) => (fi ? fi.set.has(id) : false);
    const order = all.slice().sort((a, b) => (inSet(a.id) ? 1 : 0) - (inSet(b.id) ? 1 : 0) || a.r - b.r);
    for (const n of order) {
      const [x, y] = P.get(n.id)!;
      const r = this.screenR(n);
      if (x < -30 || y < -30 || x > W + 30 || y > H + 30) continue;
      const inF = !fi || fi.set.has(n.id);
      const iso = G.idset.has(n.id) && G.deg.get(n.id) === 0;
      ctx.globalAlpha = n.a * (inF ? 1 : 0.15) * (iso && !fi ? 0.55 : 1);
      ctx.beginPath();
      ctx.arc(x, y, r, 0, 2 * Math.PI);
      if (n.s.name == null) {
        ctx.fillStyle = C.surface2;
        ctx.fill();
        ctx.setLineDash([3, 2.5]);
        ctx.strokeStyle = C.fgSubtle;
        ctx.lineWidth = 1.5;
        ctx.stroke();
        ctx.setLineDash([]);
        this.drawLock(x, y, Math.max(7, r * 0.95));
      } else if (isExt(n.s)) {
        ctx.fillStyle = C.surface;
        ctx.fill();
        ctx.strokeStyle = this.color(n.s.domain);
        ctx.lineWidth = 2.2;
        ctx.stroke();
      } else {
        ctx.fillStyle = this.color(n.s.domain);
        ctx.fill();
        ctx.strokeStyle = C.surface;
        ctx.lineWidth = 1.5;
        ctx.stroke();
      }
      const isSel = n.id === S.sel || n.id === S.sel2;
      const isHov = n.id === S.hover;
      const onPath = !!S.path && S.path.includes(n.id);
      if (isSel || isHov || (onPath && fi && fi.kind === 'pair')) {
        ctx.globalAlpha = n.a;
        ctx.beginPath();
        ctx.arc(x, y, r + 3.5, 0, 2 * Math.PI);
        ctx.strokeStyle = isSel ? C.fg : onPath ? C.agent : C.accent;
        ctx.lineWidth = isSel ? 2.4 : 2;
        ctx.stroke();
      }
      if (n.pinned) {
        ctx.globalAlpha = n.a * (inF ? 1 : 0.3);
        ctx.beginPath();
        ctx.arc(x + r * 0.75, y - r * 0.75, 4.2, 0, 2 * Math.PI);
        ctx.fillStyle = C.fg;
        ctx.fill();
        ctx.lineWidth = 1.5;
        ctx.strokeStyle = C.surface;
        ctx.stroke();
      }
    }
    ctx.globalAlpha = 1;

    // 4. labels: region labels, then app labels by priority, greedy collision avoidance
    const placed: Rect[] = [];
    const hit = (r: Rect) =>
      placed.some((p) => r.x < p.x + p.w && r.x + r.w > p.x && r.y < p.y + p.h && r.y + r.h > p.y);
    const nodeHit = (r: Rect, self: GNode | null, f = 1) => {
      for (const n of all) {
        if (n === self || n.a < 0.3) continue;
        const [x, y] = P.get(n.id)!;
        const rr = (this.screenR(n) + 1) * f;
        const cx = Math.max(r.x, Math.min(x, r.x + r.w));
        const cy = Math.max(r.y, Math.min(y, r.y + r.h));
        if ((cx - x) ** 2 + (cy - y) ** 2 < rr * rr && (!fi || fi.set.has(n.id))) return true;
      }
      return false;
    };
    const out: Label[] = [];
    placed.push({ x: W - 64, y: H - 170, w: 64, h: 170 }); // zoom controls
    const regionFont = '600 12px ' + C.font;
    regionLabels
      .sort((a, b) => b.n - a.n)
      .forEach((rl) => {
        const txt = (M.domName.get(rl.dom) ?? rl.dom).toUpperCase();
        const w = this.measure(txt, regionFont) + txt.length * 0.8 + 4;
        const h = 16;
        const span = (y: number): [number, number] | null => {
          let x0 = Infinity;
          let x1 = -Infinity;
          const hp = rl.hull;
          for (let i = 0; i < hp.length; i++) {
            const p = hp[i];
            const q = hp[(i + 1) % hp.length];
            if ((p[1] - y) * (q[1] - y) <= 0 && p[1] !== q[1]) {
              const x = p[0] + ((y - p[1]) / (q[1] - p[1])) * (q[0] - p[0]);
              x0 = Math.min(x0, x);
              x1 = Math.max(x1, x);
            }
          }
          return x1 > x0 ? [x0, x1] : null;
        };
        const cands: { x: number; y: number }[] = [];
        const tx = Math.max(rl.vx0, Math.min(rl.vx1, rl.tx));
        for (const dx of [0, -w / 3, w / 3]) cands.push({ x: tx - w / 2 + dx, y: rl.y - h - 3 });
        for (let dy = 5; dy <= 90; dy += 8) {
          const sp = span(rl.y + dy + h / 2);
          if (sp && sp[1] - sp[0] > w + 10) {
            const mid = (sp[0] + sp[1]) / 2 - w / 2;
            const slack = (sp[1] - sp[0] - w - 10) / 2;
            for (const o of [0, -slack, slack]) cands.push({ x: mid + o, y: rl.y + dy });
          }
        }
        const others = Object.entries(hulls)
          .filter(([d]) => d !== rl.dom)
          .map((e) => e[1]);
        const clean = (r: Rect) => {
          const pad = 7;
          const xs = [r.x - pad, r.x + r.w / 2, r.x + r.w + pad];
          const ys = [r.y - pad, r.y + r.h / 2, r.y + r.h + pad];
          for (const hp of others)
            for (const x of xs) for (const y of ys) if (polygonContains(hp, [x, y])) return false;
          return true;
        };
        let ok = false;
        for (let pass = 0; pass < 2 && !ok; pass++)
          for (const c of cands) {
            const r = { x: c.x, y: c.y, w, h };
            if (r.x < 4 || r.x + w > W - 4 || r.y < 2 || r.y + r.h > H - 30) continue;
            if (hit(r) || !clean(r) || (pass === 0 && nodeHit(r, null))) continue;
            placed.push(r);
            out.push({ kind: 'region', txt, r, dom: rl.dom, a: rl.a * (fi ? 0.55 : 1) });
            ok = true;
            break;
          }
      });
    const N = G.ids.length;
    const zoomRatio = k / (M.kFit || k);
    const cap = N <= 30 ? N : Math.round(Math.max(12, N * 0.17) * zoomRatio * zoomRatio);
    const cand = all
      .filter((n) => G.idset.has(n.id) && n.s.name != null && n.a > 0.4)
      .map((n) => {
        let pr = G.deg.get(n.id) ?? 0;
        let forced = false;
        if (n.id === S.hover) {
          pr += 1e9;
          forced = true;
        } else if (n.id === S.sel || n.id === S.sel2) {
          pr += 1e8;
          forced = true;
        } else if (S.path && S.path.includes(n.id)) {
          pr += 1e7;
          forced = true;
        } else if (fi && fi.set.has(n.id)) {
          pr += 1e6;
          forced = true;
        } else if (n.pinned) pr += 100;
        return { n, pr, forced };
      })
      .filter((c) => !fi || fi.set.has(c.n.id))
      .sort((a, b) => b.pr - a.pr);
    let used = 0;
    for (const c of cand) {
      if (!c.forced && used >= cap) continue;
      const n = c.n;
      const [x, y] = P.get(n.id)!;
      const r = this.screenR(n);
      const strong = c.pr >= 1e6;
      const font = (strong ? '600 13px ' : '500 12.5px ') + C.font;
      const w = this.measure(n.s.name!, font) + 4;
      const h = 16;
      const d = r * 0.7;
      const cands = [
        { x: x - w / 2, y: y + r + 2 },
        { x: x + r + 4, y: y - h / 2 },
        { x: x - r - 4 - w, y: y - h / 2 },
        { x: x - w / 2, y: y - r - 2 - h },
        { x: x + d + 2, y: y + d },
        { x: x - d - 2 - w, y: y + d },
        { x: x + d + 2, y: y - d - h },
        { x: x - d - 2 - w, y: y - d - h },
      ];
      const hub = used < 8 || c.forced;
      for (const cc of cands) {
        const rr = { x: cc.x, y: cc.y, w, h };
        if (rr.x < 2 || rr.x + rr.w > W - 2 || rr.y < 2 || rr.y + rr.h > H - 28) continue;
        if (hit(rr) || nodeHit(rr, n, hub ? 0.45 : 1)) continue;
        placed.push(rr);
        out.push({ kind: 'node', n, r: rr, font, a: n.a, strong });
        used++;
        break;
      }
    }
    this.lastLabels = out;
    ctx.textBaseline = 'middle';
    ctx.lineJoin = 'round';
    const ls = ctx as CanvasRenderingContext2D & { letterSpacing?: string };
    for (const o of out) {
      ctx.globalAlpha = o.a;
      if (o.kind === 'region') {
        ctx.font = regionFont;
        if ('letterSpacing' in ls) ls.letterSpacing = '0.8px';
        ctx.lineWidth = 4;
        ctx.strokeStyle = C.bg;
        ctx.strokeText(o.txt, o.r.x + 2, o.r.y + o.r.h / 2);
        ctx.fillStyle = this.color(o.dom, 'text');
        ctx.fillText(o.txt, o.r.x + 2, o.r.y + o.r.h / 2);
        if ('letterSpacing' in ls) ls.letterSpacing = '0px';
      } else {
        ctx.font = o.font;
        ctx.lineWidth = 3.5;
        ctx.strokeStyle = C.bg;
        ctx.strokeText(o.n.s.name!, o.r.x + 2, o.r.y + o.r.h / 2 + 0.5);
        const onPath = !!S.path && S.path.includes(o.n.id) && (!fi || fi.kind === 'pair');
        ctx.fillStyle = onPath ? C.agent : o.n.id === S.hover || o.n.id === S.sel || o.strong ? C.fg : C.fgMuted;
        ctx.fillText(o.n.s.name!, o.r.x + 2, o.r.y + o.r.h / 2 + 0.5);
      }
    }
    ctx.globalAlpha = 1;
  }
  private drawLock(x: number, y: number, s: number) {
    const { ctx, C } = this;
    ctx.save();
    ctx.strokeStyle = C.fgMuted;
    ctx.fillStyle = C.fgMuted;
    ctx.lineWidth = 1.4;
    const w = s * 0.9;
    const h = s * 0.7;
    ctx.beginPath();
    if (ctx.roundRect) ctx.roundRect(x - w / 2, y - h * 0.2, w, h, 1.5);
    else ctx.rect(x - w / 2, y - h * 0.2, w, h);
    ctx.fill();
    ctx.beginPath();
    ctx.arc(x, y - h * 0.2, w * 0.32, Math.PI, 0);
    ctx.stroke();
    ctx.restore();
  }

  /* ---------- hit testing + pointer ---------- */
  nodeAt(sx: number, sy: number): GNode | null {
    if (!this.G) return null;
    let best: GNode | null = null;
    let bd = Infinity;
    for (const id of this.G.ids) {
      const n = this.M.nodes.get(id)!;
      const [x, y] = this.toScreen(n.dx, n.dy);
      const r = this.screenR(n) + (this.pointer === 'touch' ? 10 : 4);
      const d = Math.hypot(sx - x, sy - y);
      if (d < r && d < bd) {
        bd = d;
        best = n;
      }
    }
    if (!best)
      for (const o of this.lastLabels)
        if (o.kind === 'node' && sx >= o.r.x && sx <= o.r.x + o.r.w && sy >= o.r.y && sy <= o.r.y + o.r.h) return o.n;
    return best;
  }
  private bindPointer() {
    const cv = this.cv;
    type Subj = { n: GNode; x: number; y: number };
    select(cv)
      .call(
        drag<HTMLCanvasElement, unknown, Subj | null>()
          .container(cv)
          .clickDistance(5)
          .subject((ev: { x: number; y: number }) => {
            const n = this.nodeAt(ev.x, ev.y);
            if (!n) return null;
            const [x, y] = this.toScreen(n.dx, n.dy);
            return { n, x, y };
          })
          .on('start', (ev: { subject: Subj }) => {
            this.dragNode = ev.subject.n;
            this.dragMoved = false;
          })
          .on('drag', (ev: { x: number; y: number }) => {
            const n = this.dragNode;
            if (!n) return;
            if (!this.dragMoved) {
              this.dragMoved = true;
              this.M.layoutDefault = false;
              this.host.onHover(null, 0, 0);
              cv.classList.add('dragging');
              n.pinned = true;
              this.startLive();
            }
            const [wx, wy] = this.transform.invert([ev.x, ev.y]);
            const A = this.M.anchors[n.s.domain];
            const p = { x: wx, y: wy, r: n.r };
            if (A) this.projectInside(p, A);
            n.fx = p.x;
            n.fy = p.y;
          })
          .on('end', () => {
            if (this.dragMoved && this.dragNode) {
              this.coolLive();
              cv.classList.remove('dragging');
              this.host.toast('pinned', this.dragNode.id);
              this.host.onChange();
            }
            this.dragNode = null;
          }),
      )
      .call(this.zoomB)
      .on('dblclick.zoom', null);

    const local = (ev: MouseEvent) => {
      const r = cv.getBoundingClientRect();
      return [ev.clientX - r.left, ev.clientY - r.top] as const;
    };
    const onMove = (ev: PointerEvent) => {
      this.pointer = ev.pointerType === 'touch' ? 'touch' : 'mouse';
      if (ev.pointerType !== 'mouse' || this.dragNode || ev.buttons) return;
      const [x, y] = local(ev);
      const n = this.nodeAt(x, y);
      const id = n ? n.id : null;
      cv.classList.toggle('pointing', !!n);
      if (id !== this.S.hover) {
        this.S.hover = id;
        this.draw();
      }
      this.host.onHover(id, x, y);
    };
    const onDown = (ev: PointerEvent) => {
      this.pointer = ev.pointerType === 'touch' ? 'touch' : 'mouse';
    };
    const onLeave = () => {
      if (this.S.hover) {
        this.S.hover = null;
        this.draw();
      }
      this.host.onHover(null, 0, 0);
    };
    const onClick = (ev: MouseEvent) => {
      const [x, y] = local(ev);
      const n = this.nodeAt(x, y);
      if (!n) {
        if (this.S.sel) {
          this.S.sel = this.S.sel2 = null;
          this.S.path = null;
          this.host.onChange();
          this.draw();
        }
        return;
      }
      this.clickNode(n.id);
    };
    const onDbl = (ev: MouseEvent) => {
      const [x, y] = local(ev);
      const n = this.nodeAt(x, y);
      if (n && n.pinned) {
        this.unpin(n.id);
        this.host.toast('unpinned', n.id);
      }
    };
    cv.addEventListener('pointermove', onMove);
    cv.addEventListener('pointerdown', onDown);
    cv.addEventListener('pointerleave', onLeave);
    cv.addEventListener('click', onClick);
    cv.addEventListener('dblclick', onDbl);
    this.cleanup.push(() => {
      cv.removeEventListener('pointermove', onMove);
      cv.removeEventListener('pointerdown', onDown);
      cv.removeEventListener('pointerleave', onLeave);
      cv.removeEventListener('click', onClick);
      cv.removeEventListener('dblclick', onDbl);
    });
  }

  /** Keyboard: step the selection through applications in domain order. */
  stepSelection(dir: 1 | -1) {
    const ids = this.G?.ids ?? [];
    if (!ids.length) return;
    const i = this.S.sel ? ids.indexOf(this.S.sel) : -1;
    const next = ids[(i + dir + ids.length) % ids.length];
    this.select(next);
    this.ensureVisible(next);
  }
}
