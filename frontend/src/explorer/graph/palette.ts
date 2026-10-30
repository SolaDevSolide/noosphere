import type { Model } from '../../domain/model';
import { isDark } from '../../ui/theme';

/** Calm hues per domain (hue, saturation), tuned per theme. Unknown domains take the next free hue by order. */
const HUES: Record<string, [number, number]> = {
  customer: [212, 58],
  contracts: [258, 42],
  claims: [20, 60],
  finance: [154, 42],
  hr: [332, 46],
  risk: [40, 58],
  data: [188, 55],
  platform: [222, 20],
  digital: [96, 32],
  external: [210, 8],
};
const SEQUENCE: [number, number][] = [
  [212, 58],
  [258, 42],
  [20, 60],
  [154, 42],
  [222, 20],
  [332, 46],
  [40, 58],
  [188, 55],
  [96, 32],
  [280, 40],
  [0, 50],
  [60, 45],
];

export type ColorKind = 'fill' | 'text' | 'hull' | 'hullLine';

export function hueOf(domainId: string, order: string[]): [number, number] {
  if (HUES[domainId]) return HUES[domainId];
  const used = new Set(order.filter((d) => HUES[d]).map((d) => HUES[d].join()));
  const free = SEQUENCE.filter((h) => !used.has(h.join()));
  const i = Math.max(0, order.filter((d) => !HUES[d]).indexOf(domainId));
  return free[i % free.length] ?? SEQUENCE[i % SEQUENCE.length];
}

export function dcol(h: [number, number], kind: ColorKind = 'fill', dark = isDark()) {
  const [hu, s] = h;
  if (kind === 'text') return `hsl(${hu} ${Math.min(s, 50)}% ${dark ? 74 : 33}%)`;
  if (kind === 'hull') return `hsl(${hu} ${s}% ${dark ? 55 : 52}% / ${dark ? 0.1 : 0.075})`;
  if (kind === 'hullLine') return `hsl(${hu} ${s}% ${dark ? 60 : 48}% / ${dark ? 0.3 : 0.28})`;
  return `hsl(${hu} ${s}% ${dark ? 60 : 50}%)`;
}

export function domainColor(m: Model, domainId: string, kind: ColorKind = 'fill') {
  return dcol(
    hueOf(
      domainId,
      m.domains.map((d) => d.id),
    ),
    kind,
  );
}
