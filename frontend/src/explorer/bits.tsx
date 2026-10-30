import type { FlowType, LandscapeFlow, Sensitivity } from '../api/types';
import { isRestrictedFlow } from '../api/types';
import type { Dict } from '../i18n/en';
import { useT } from '../i18n';
import type { Model } from '../domain/model';
import { IconWarn } from '../ui/icons';

export const pct = (v: number | undefined) => Math.round((v ?? 0) * 100);

export function nameOf(t: Dict, m: Model, id: string) {
  const a = m.apps.get(id);
  if (!a) return id;
  // a guest's outline keeps the name; only restricted applications lose it
  return a.restricted ? t.common.restrictedApp : a.name;
}
export const flowName = (t: Dict, f: LandscapeFlow) => (isRestrictedFlow(f) ? t.common.restrictedFlow : f.name);
export const teamName = (m: Model, id: string | null | undefined) => (id ? (m.teams.get(id)?.name ?? id) : '');
export const objName = (m: Model, id: string) => m.objects.get(id)?.name ?? id;

export function Sdot({ s }: { s: Sensitivity }) {
  const t = useT();
  return <span className={'sd ' + s} title={t.sens[s]} role="img" aria-label={t.sens[s]} />;
}

export function StatusTag({ f }: { f: LandscapeFlow }) {
  const t = useT();
  if (isRestrictedFlow(f)) return null;
  if (f.status === 'deprecated') return <span className="tg danger">{t.status.deprecated}</span>;
  if (f.status === 'planned') return <span className="tg plan">{t.status.planned}</span>;
  if (f.plannedChange?.summary)
    return (
      <span className="tg warn icon" title={`${t.legend.plannedChange}: ${f.plannedChange.summary}`}>
        <IconWarn />
        <span className="sr">{t.legend.plannedChange}</span>
      </span>
    );
  return null;
}

export function LineSample({ type }: { type: FlowType }) {
  const da = type === 'event' ? '7 5' : type === 'batch' ? '.5 5.5' : undefined;
  return (
    <svg width="34" height="10" className={'t-' + type} aria-hidden="true">
      <path
        d="M1 5H33"
        stroke="var(--tc)"
        strokeWidth={type === 'batch' ? 2.6 : 1.8}
        strokeDasharray={da}
        strokeLinecap="round"
      />
    </svg>
  );
}

export function Meter({ value, label, className = '' }: { value: number; label?: string; className?: string }) {
  const p = pct(value);
  return (
    <span className={'meter' + (value < 0.65 ? ' low' : '') + (className ? ' ' + className : '')}>
      <span
        className="bar2"
        role="progressbar"
        aria-valuenow={p}
        aria-valuemin={0}
        aria-valuemax={100}
        aria-label={label}
      >
        <i style={{ width: p + '%' }} />
      </span>
      {label === undefined ? null : label}
    </span>
  );
}

/** Highlight the first occurrence of `q` (already folded) inside `label`. */
export function Hl({ label, q }: { label: string; q: string }) {
  if (!q) return <>{label}</>;
  const folded = label.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase();
  const i = folded.indexOf(q);
  if (i < 0 || folded.length !== label.length) return <>{label}</>;
  return (
    <>
      {label.slice(0, i)}
      <mark>{label.slice(i, i + q.length)}</mark>
      {label.slice(i + q.length)}
    </>
  );
}
