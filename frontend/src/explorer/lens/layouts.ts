// Geometry of the lens views: an application with its upstream and downstream partners, a flow between two
// applications, and the lineage of a data object. Layouts are plain data; Lens.tsx draws them.
import type { LandscapeFlow } from '../../api/types';
import { isRestrictedApp } from '../../api/types';
import { appOrder, flowsOf, lineage, type Model } from '../../domain/model';

export const PH = 48;
export const PG = 8;
export const NH = 60;
const GG = 18;
const TP = 14;
const TOP = 54;
const PADX = 18;

export type NodeRole = 'center' | 'node' | 'ghost' | 'src';
export interface LNode {
  key: string;
  id: string;
  x: number;
  y: number;
  w: number;
  h: number;
  role: NodeRole;
  twoWay?: boolean;
  /** Lineage: other routes into this node. */
  extra?: LandscapeFlow[];
}
export interface LPill {
  f: LandscapeFlow;
  x: number;
  y: number;
  w: number;
  glyph: string;
}
export interface LFrame {
  x: number;
  y: number;
  w: number;
  h: number;
  partner: string;
  flows: string[];
}
export type HeadText =
  | { k: 'upstream'; n: number; two: boolean }
  | { k: 'downstream'; n: number; two: boolean }
  | { k: 'selected' }
  | { k: 'second' }
  | { k: 'from' }
  | { k: 'to' }
  | { k: 'flow'; n: number }
  | { k: 'depth'; d: number };
export interface LHead {
  x: number;
  w: number;
  text: HeadText;
}
export interface LWire {
  key: string;
  f: LandscapeFlow;
  d: string;
  marker: '' | 'start' | 'end';
  ghost: boolean;
}
export interface LFlowCard {
  f: LandscapeFlow;
  x: number;
  y: number;
  w: number;
}
export interface Layout {
  W: number;
  H: number;
  nodes: LNode[];
  pills: LPill[];
  frames: LFrame[];
  heads: LHead[];
  wires: LWire[];
  card?: LFlowCard;
  srcX?: number;
}

const clamp = (v: number, a: number, b: number) => Math.max(a, Math.min(b, v));
const curve = (x1: number, y1: number, x2: number, y2: number) => {
  const dx = (x2 - x1) * 0.5;
  return `M${x1.toFixed(1)},${y1.toFixed(1)} C${(x1 + dx).toFixed(1)},${y1.toFixed(1)} ${(x2 - dx).toFixed(1)},${y2.toFixed(1)} ${x2.toFixed(1)},${y2.toFixed(1)}`;
};
let wireSeq = 0;
function wire(
  f: LandscapeFlow,
  x1: number,
  y1: number,
  x2: number,
  y2: number,
  marker: '' | 'start' | 'end',
  ghost = false,
): LWire {
  if (marker === 'end') x2 -= 2;
  if (marker === 'start') x1 += 2;
  return { key: f.id + ':' + wireSeq++, f, d: curve(x1, y1, x2, y2), marker, ghost };
}
const spread = (c: number, n: number, step: number) =>
  Array.from({ length: n }, (_, i) => c + (i - (n - 1) / 2) * step);
const stackH = (n: number) => n * PH + (n - 1) * PG;
const restricted = (m: Model, id: string) => {
  const a = m.apps.get(id);
  return !a || isRestrictedApp(a);
};

interface Group {
  id: string;
  ins: LandscapeFlow[];
  outs: LandscapeFlow[];
  two?: boolean;
  items: { f: LandscapeFlow; dir: 'in' | 'out' }[];
  ph: number;
  gh: number;
}

export function appLayout(m: Model, cid: string, W: number, second: boolean): Layout {
  wireSeq = 0;
  const order = appOrder(m);
  const g = new Map<string, Group>();
  const add = (id: string) => {
    let x = g.get(id);
    if (!x) g.set(id, (x = { id, ins: [], outs: [], items: [], ph: 0, gh: 0 }));
    return x;
  };
  for (const f of m.flows) {
    if (f.toId === cid && f.fromId !== cid) add(f.fromId).ins.push(f);
    else if (f.fromId === cid && f.toId !== cid) add(f.toId).outs.push(f);
  }
  const groups = [...g.values()].sort((a, b) => order(a.id, b.id));
  const L: Group[] = [];
  const R: Group[] = [];
  let lL = 0;
  let lR = 0;
  const two: Group[] = [];
  for (const x of groups) {
    if (x.ins.length && x.outs.length) two.push(x);
    else if (x.ins.length) {
      L.push(x);
      lL += x.ins.length;
    } else {
      R.push(x);
      lR += x.outs.length;
    }
  }
  for (const x of two) {
    x.two = true;
    const n = x.ins.length + x.outs.length;
    if (lL <= lR) {
      L.push(x);
      lL += n;
    } else {
      R.push(x);
      lR += n;
    }
  }
  L.sort((a, b) => order(a.id, b.id));
  R.sort((a, b) => order(a.id, b.id));
  for (const x of [...L, ...R]) {
    x.items = [...x.ins.map((f) => ({ f, dir: 'in' as const })), ...x.outs.map((f) => ({ f, dir: 'out' as const }))];
    x.ph = stackH(x.items.length) + (x.two ? TP * 2 : 0);
    x.gh = Math.max(NH, x.ph);
  }

  // second level
  const OW = 156;
  const OS = 46;
  const OH = 44;
  const first = new Set([cid, ...g.keys()]);
  const outer = {
    L: new Map<string, { id: string; links: { f: LandscapeFlow; n: string }[]; want: number; y: number }>(),
    R: new Map<string, { id: string; links: { f: LandscapeFlow; n: string }[]; want: number; y: number }>(),
  };
  if (second) {
    for (const [side, arr] of [
      ['L', L],
      ['R', R],
    ] as const)
      for (const x of arr) {
        if (restricted(m, x.id)) continue;
        for (const f of flowsOf(m, x.id)) {
          const o = f.fromId === x.id ? f.toId : f.fromId;
          if (first.has(o) || !m.apps.has(o)) continue;
          const other = side === 'L' ? outer.R : outer.L;
          if (other.has(o)) continue;
          const mm = outer[side];
          if (!mm.has(o)) mm.set(o, { id: o, links: [], want: 0, y: 0 });
          mm.get(o)!.links.push({ f, n: x.id });
        }
      }
  }
  const tight = W < 1200;
  const NW = tight ? 142 : 160;
  const SEG = tight ? 32 : 36;
  const CW = tight ? 184 : 204;
  const fixed = 2 * PADX + 2 * NW + 4 * SEG + CW + (second ? 2 * (OW + OS) : 0);
  const lane = clamp(Math.floor((W - fixed) / 2), 160, 250);
  const x0 = PADX + (second ? OW + OS : 0);
  const X = { ln: x0, ll: 0, c: 0, rl: 0, rn: 0, or: 0 };
  X.ll = X.ln + NW + SEG;
  X.c = X.ll + lane + SEG;
  X.rl = X.c + CW + SEG;
  X.rn = X.rl + lane + SEG;
  X.or = X.rn + NW + OS;
  const totalW = (second ? X.or + OW : X.rn + NW) + PADX;

  const sum = (arr: Group[]) => arr.reduce((a, x) => a + x.gh, 0) + Math.max(0, arr.length - 1) * GG;
  const nL = L.reduce((a, x) => a + x.items.length, 0);
  const nR = R.reduce((a, x) => a + x.items.length, 0);
  const CH = Math.max(170, Math.max(nL, nR) * 16 + 70);
  const inner = Math.max(sum(L), sum(R), CH, NH);
  const nodes: LNode[] = [];
  const pills: LPill[] = [];
  const frames: LFrame[] = [];
  const wires: LWire[] = [];
  const cy = TOP + (inner - CH) / 2;
  nodes.push({ key: cid, id: cid, x: X.c, y: cy, w: CW, h: CH, role: 'center' });
  const ca = {
    L: spread(cy + CH / 2, nL, Math.min(18, (CH - 60) / Math.max(1, nL - 1))),
    R: spread(cy + CH / 2, nR, Math.min(18, (CH - 60) / Math.max(1, nR - 1))),
  };
  const nodeY: Record<string, number> = {};
  for (const [side, arr] of [
    ['L', L],
    ['R', R],
  ] as const) {
    let y = TOP + (inner - sum(arr)) / 2;
    let k = 0;
    for (const x of arr) {
      const ny = y + (x.gh - NH) / 2;
      nodeY[x.id] = ny + NH / 2;
      const nx = side === 'L' ? X.ln : X.rn;
      nodes.push({ key: x.id, id: x.id, x: nx, y: ny, w: NW, h: NH, role: 'node', twoWay: x.two });
      let py = y + (x.gh - x.ph) / 2 + (x.two ? TP : 0);
      const lx = side === 'L' ? X.ll : X.rl;
      if (x.two)
        frames.push({ x: lx - 8, y: py - TP, w: lane + 16, h: x.ph, partner: x.id, flows: x.items.map((i) => i.f.id) });
      const na = spread(ny + NH / 2, x.items.length, Math.min(12, (NH - 20) / Math.max(1, x.items.length - 1)));
      x.items.forEach((it, i) => {
        const pm = py + PH / 2;
        const f = it.f;
        const glyph = x.two ? (side === 'L' ? (it.dir === 'in' ? '→' : '←') : it.dir === 'out' ? '→' : '←') : '';
        pills.push({ f, x: lx, y: py, w: lane, glyph });
        const c = ca[side][k++];
        if (side === 'L') {
          wires.push(wire(f, X.ln + NW, na[i], lx, pm, it.dir === 'out' ? 'start' : ''));
          wires.push(wire(f, lx + lane, pm, X.c, c, it.dir === 'in' ? 'end' : ''));
        } else {
          wires.push(wire(f, X.c + CW, c, lx, pm, it.dir === 'in' ? 'start' : ''));
          wires.push(wire(f, lx + lane, pm, X.rn, na[i], it.dir === 'out' ? 'end' : ''));
        }
        py += PH + PG;
      });
      y += x.gh + GG;
    }
  }
  let H = TOP + inner + 36;
  if (second) {
    for (const side of ['L', 'R'] as const) {
      const arr = [...outer[side].values()];
      for (const o of arr) o.want = o.links.reduce((a, l) => a + nodeY[l.n], 0) / o.links.length - OH / 2;
      arr.sort((a, b) => a.want - b.want || order(a.id, b.id));
      let last = TOP - 10;
      for (const o of arr) {
        o.y = Math.max(o.want, last + 10);
        last = o.y + OH;
      }
      const over = last - (TOP + inner);
      if (over > 0) {
        const shift = Math.min(over, arr.length ? arr[0].y - TOP : 0);
        for (const o of arr) o.y -= shift;
      }
      for (const o of arr) {
        const ox = side === 'L' ? PADX : X.or;
        nodes.push({ key: '2:' + o.id, id: o.id, x: ox, y: o.y, w: OW, h: OH, role: 'ghost' });
        for (const l of o.links) {
          const toward = l.f.toId === l.n;
          if (side === 'L')
            wires.push(wire(l.f, ox + OW, o.y + OH / 2, X.ln, nodeY[l.n], toward ? 'end' : 'start', true));
          else wires.push(wire(l.f, X.rn + NW, nodeY[l.n], ox, o.y + OH / 2, toward ? 'start' : 'end', true));
        }
        H = Math.max(H, o.y + OH + 36);
      }
    }
  }
  const heads: LHead[] = [
    { x: X.ln, w: NW + SEG + lane, text: { k: 'upstream', n: L.length, two: L.some((x) => x.two) } },
    { x: X.c, w: CW, text: { k: 'selected' } },
    { x: X.rl, w: lane + SEG + NW, text: { k: 'downstream', n: R.length, two: R.some((x) => x.two) } },
  ];
  if (second) heads.push({ x: PADX, w: OW, text: { k: 'second' } }, { x: X.or, w: OW, text: { k: 'second' } });
  return { W: totalW, H, nodes, pills, frames, heads, wires };
}

export const FLOW_CARD_W = 340;

export function flowLayout(m: Model, fid: string, W: number, cardH: number): Layout | null {
  wireSeq = 0;
  const f = m.flow.get(fid);
  if (!f) return null;
  const pair = [
    f,
    ...m.flows.filter(
      (x) => x !== f && ((x.fromId === f.fromId && x.toId === f.toId) || (x.fromId === f.toId && x.toId === f.fromId)),
    ),
  ];
  const NW = 180;
  const FW = FLOW_CARD_W;
  const SEG = clamp(Math.floor((W - 2 * PADX - 2 * NW - FW) / 2), 90, 200);
  const xl = PADX;
  const xc = xl + NW + SEG;
  const xr = xc + FW + SEG;
  const items = pair.map((x, i) => ({ f: x, h: i ? PH : cardH }));
  const stack = items.reduce((a, it) => a + it.h, 0) + (items.length - 1) * 14;
  const inner = Math.max(NH, stack);
  let y = TOP + (inner - stack) / 2;
  const ny = TOP + (inner - NH) / 2;
  const nodes: LNode[] = [
    { key: f.fromId, id: f.fromId, x: xl, y: ny, w: NW, h: NH, role: 'node' },
    { key: f.toId, id: f.toId, x: xr, y: ny, w: NW, h: NH, role: 'node' },
  ];
  const la = spread(ny + NH / 2, items.length, 10);
  const pills: LPill[] = [];
  const wires: LWire[] = [];
  let card: LFlowCard | undefined;
  items.forEach((it, i) => {
    const mid = y + it.h / 2;
    const fw = it.f.fromId === f.fromId;
    if (i === 0) card = { f, x: xc, y, w: FW };
    else pills.push({ f: it.f, x: xc + 20, y, w: FW - 40, glyph: fw ? '→' : '←' });
    const inset = i === 0 ? 0 : 20;
    wires.push(wire(it.f, xl + NW, la[i], xc + inset, mid, fw ? '' : 'start'));
    wires.push(wire(it.f, xc + FW - inset, mid, xr, la[i], fw ? 'end' : ''));
    y += it.h + 14;
  });
  const heads: LHead[] = [
    { x: xl, w: NW, text: { k: 'from' } },
    { x: xc, w: FW, text: { k: 'flow', n: pair.length } },
    { x: xr, w: NW, text: { k: 'to' } },
  ];
  return { W: xr + NW + PADX, H: TOP + inner + 36, nodes, pills, frames: [], heads, wires, card };
}

export function lineageLayout(m: Model, obj: string): Layout | null {
  wireSeq = 0;
  const Lg = lineage(m, obj);
  if (!Lg) return null;
  const NW = 160;
  const SEG = 30;
  const lane = 196;
  const link = (n: string) => Lg.link.get(n);
  const rowH = (n: string) => Math.max(NH, link(n) ? stackH(link(n)!.flows.length) : NH) + GG + 4;
  const tidy = (root: string, km: Map<string, string[]>) => {
    const y: Record<string, number> = {};
    let cur = 0;
    const rec = (n: string) => {
      const ks = km.get(n) ?? [];
      if (!ks.length) {
        const h = rowH(n);
        y[n] = cur + h / 2;
        cur += h;
        return;
      }
      ks.forEach(rec);
      y[n] = (y[ks[0]] + y[ks[ks.length - 1]]) / 2;
    };
    rec(root);
    return y;
  };
  const yd = tidy(Lg.src, Lg.kids);
  const yu = tidy(Lg.src, Lg.ukids);
  const off = yd[Lg.src] - yu[Lg.src];
  const Y: Record<string, number> = { ...yd };
  for (const k of Object.keys(yu)) if (k !== Lg.src) Y[k] = yu[k] + off;
  const ids = [...Lg.depth.keys()];
  let minTop = Infinity;
  for (const n of ids) minTop = Math.min(minTop, Y[n] - (rowH(n) - GG - 4) / 2);
  for (const n of ids) Y[n] += TOP + 8 - minTop;
  const ds = ids.map((n) => Lg.depth.get(n)!);
  const minD = Math.min(...ds);
  const maxD = Math.max(...ds);
  const colX = (d: number) => PADX + (d - minD) * (NW + SEG + lane + SEG);
  const extraTo = new Map<string, LandscapeFlow[]>();
  for (const f of Lg.extra) {
    if (!extraTo.has(f.toId)) extraTo.set(f.toId, []);
    extraTo.get(f.toId)!.push(f);
  }
  const nodes: LNode[] = [];
  const pills: LPill[] = [];
  const wires: LWire[] = [];
  let maxY = 0;
  for (const n of ids) {
    nodes.push({
      key: n,
      id: n,
      x: colX(Lg.depth.get(n)!),
      y: Y[n] - NH / 2,
      w: NW,
      h: NH,
      role: n === Lg.src ? 'src' : 'node',
      extra: extraTo.get(n),
    });
    maxY = Math.max(maxY, Y[n] + NH / 2);
  }
  const outIdx: Record<string, number> = {};
  const anchor = (p: string, side: string) => {
    const key = p + side;
    outIdx[key] = (outIdx[key] || 0) + 1;
    return outIdx[key] - 1;
  };
  const sortedIds = ids.filter((n) => link(n)).sort((a, b) => Y[a] - Y[b]);
  const upCount: Record<string, number> = {};
  const downCount: Record<string, number> = {};
  for (const n of sortedIds) {
    const l = link(n)!;
    const c = l.dir === 'down' ? downCount : upCount;
    c[l.parent] = (c[l.parent] || 0) + l.flows.length;
  }
  for (const n of sortedIds) {
    const l = link(n)!;
    const d = Lg.depth.get(n)!;
    const ph = stackH(l.flows.length);
    let py = Y[n] - ph / 2;
    if (l.dir === 'down') {
      const lx = colX(d - 1) + NW + SEG;
      const total = downCount[l.parent];
      for (const f of l.flows) {
        const i = anchor(l.parent, 'R');
        const pa = spread(Y[l.parent], total, Math.min(12, (NH - 20) / Math.max(1, total - 1)))[i];
        pills.push({ f, x: lx, y: py, w: lane, glyph: '' });
        wires.push(wire(f, colX(d - 1) + NW, pa, lx, py + PH / 2, ''));
        wires.push(wire(f, lx + lane, py + PH / 2, colX(d), Y[n], 'end'));
        py += PH + PG;
      }
    } else {
      const lx = colX(d) + NW + SEG;
      const total = upCount[l.parent];
      for (const f of l.flows) {
        const i = anchor(l.parent, 'L');
        const pa = spread(Y[l.parent], total, Math.min(12, (NH - 20) / Math.max(1, total - 1)))[i];
        pills.push({ f, x: lx, y: py, w: lane, glyph: '' });
        wires.push(wire(f, colX(d) + NW, Y[n], lx, py + PH / 2, ''));
        wires.push(wire(f, lx + lane, py + PH / 2, colX(d + 1), pa, 'end'));
        py += PH + PG;
      }
    }
    maxY = Math.max(maxY, py);
  }
  const heads: LHead[] = [];
  for (let d = minD; d <= maxD; d++) heads.push({ x: colX(d), w: NW, text: { k: 'depth', d } });
  return { W: colX(maxD) + NW + PADX, H: maxY + 40, nodes, pills, frames: [], heads, wires, srcX: colX(0) };
}
