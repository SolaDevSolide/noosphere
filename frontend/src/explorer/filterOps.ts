import type { FlowStatus, FlowType, Sensitivity } from '../api/types';
import type { Filters } from '../domain/model';
import type { FilterKey } from '../layout/searchIndex';

export type ListKey = 'type' | 'obj' | 'sens' | 'status' | 'hidden';

export function toggleIn(f: Filters, k: ListKey, v: string): Filters {
  const cur = f[k] as string[];
  const next = cur.includes(v) ? cur.filter((x) => x !== v) : [...cur, v];
  return { ...f, [k]: next } as Filters;
}

export type PresetId = 'sensitive' | 'changes' | 'deprecated';

export function presetOn(f: Filters, p: PresetId) {
  if (p === 'sensitive') return f.sens.length === 1 && f.sens[0] === 'sensitive';
  if (p === 'changes') return f.changeOnly;
  return f.status.length === 1 && f.status[0] === 'deprecated';
}

/** Turn a preset on (replacing its dimension) or off. */
export function togglePreset(f: Filters, p: PresetId): Filters {
  const on = presetOn(f, p);
  if (p === 'sensitive') return { ...f, sens: on ? [] : ['sensitive' as Sensitivity] };
  if (p === 'changes') return { ...f, changeOnly: !on };
  return { ...f, status: on ? [] : ['deprecated' as FlowStatus] };
}

/** Apply a filter picked in the search box (adds it, never removes). */
export function applySearchFilter(f: Filters, k: FilterKey, v: string): Filters {
  if (k === 'preset') return presetOn(f, v as PresetId) ? f : togglePreset(f, v as PresetId);
  const cur = f[k] as string[];
  if (cur.includes(v)) return f;
  return { ...f, [k]: [...cur, v] } as Filters;
}

export type { FlowType };
