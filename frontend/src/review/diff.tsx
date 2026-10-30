import { diffWordsWithSpace } from 'diff';
import type { ReactNode } from 'react';
import { PROTECTED_MASK, type FieldDiff } from '../api/types';
import type { Model } from '../domain/model';
import { useI18n } from '../i18n';
import type { Dict } from '../i18n/en';
import { IconInfo, IconLock } from '../ui/icons';

/** One piece of an inline word diff. */
export interface Segment {
  text: string;
  op: 'same' | 'add' | 'del';
}

/** Word-level changes between two texts; whitespace and line breaks are kept so Markdown stays readable. */
export function wordSegments(before: string, after: string): Segment[] {
  const out: Segment[] = [];
  const push = (text: string, op: Segment['op']) => {
    if (!text) return;
    const last = out[out.length - 1];
    if (last && last.op === op) last.text += text;
    else out.push({ text, op });
  };
  for (const c of diffWordsWithSpace(before, after)) {
    if (!c.added && !c.removed) {
      push(c.value, 'same');
      continue;
    }
    // Whitespace around a change is not worth a highlight: keep the new layout, and of the old one only the spaces
    // that keep the struck words apart.
    const m = /^(\s*)([\s\S]*?)(\s*)$/.exec(c.value)!;
    const keep = (ws: string) => c.added || !ws.includes('\n');
    if (keep(m[1])) push(m[1], 'same');
    push(m[2], c.added ? 'add' : 'del');
    if (keep(m[3])) push(m[3], 'same');
  }
  return out;
}

/** Items removed from and added to a list. */
export function listChanges(before: unknown, after: unknown) {
  const b = asList(before);
  const a = asList(after);
  return { removed: b.filter((x) => !a.includes(x)), added: a.filter((x) => !b.includes(x)), before: b, after: a };
}

const asText = (v: unknown): string => {
  if (v == null) return '';
  if (typeof v === 'string') return v;
  if (typeof v === 'number' || typeof v === 'boolean') return String(v);
  if (typeof v === 'object' && 'summary' in v) {
    const pc = v as { summary?: string; date?: string | null };
    return pc.date ? `${pc.summary ?? ''} (${pc.date})` : (pc.summary ?? '');
  }
  return JSON.stringify(v);
};

const asList = (v: unknown): string[] =>
  Array.isArray(v) ? v.map((x) => asText(x)) : v == null || v === '' ? [] : [asText(v)];

/** Translated field label; falls back to the label sent by the server. */
export function fieldLabel(t: Dict, d: Pick<FieldDiff, 'field' | 'label'>): string {
  const known = (t.record.fields as Record<string, string>)[d.field] ?? t.review.fieldNames[d.field];
  return known ?? d.label ?? d.field;
}

/** Readable value of one field: names instead of ids, translated enum values. */
export function displayValue(t: Dict, m: Model | null, field: string, v: unknown): string {
  if (v == null || v === '') return '';
  const s = asText(v);
  const enums: Record<string, Record<string, string>> = {
    kind: t.kindName,
    lifecycle: t.lifecycle,
    visibility: t.visibility,
    type: t.type,
    status: t.status,
    sensitivity: t.sens,
  };
  if (enums[field]) return enums[field][s] ?? s;
  if (field === 'access') {
    // access lists come as "team:<id>" and "user:<username>"
    if (s.startsWith('team:')) return m?.teams.get(s.slice(5))?.name ?? s.slice(5);
    if (s.startsWith('user:')) return s.slice(5);
  }
  if (!m) return s;
  if (field === 'domainId') return m.domain.get(s)?.name ?? s;
  if (field === 'teamId' || field === 'ownerTeamId') return m.teams.get(s)?.name ?? s;
  if (field === 'fromId' || field === 'toId' || field === 'sourceApplicationId') {
    const a = m.apps.get(s);
    return a && !a.restricted ? a.name : s;
  }
  if (field === 'dataObjectIds' || field === 'sourceOf') return m.objects.get(s)?.name ?? s;
  return s;
}

function Ins({ children }: { children: ReactNode }) {
  const { t } = useI18n();
  return (
    <ins className="d">
      <span className="sr">{t.review.added} </span>
      {children}
    </ins>
  );
}
function Del({ children }: { children: ReactNode }) {
  const { t } = useI18n();
  return (
    <del className="d">
      <span className="sr">{t.review.removed} </span>
      {children}
    </del>
  );
}

/** Inline highlighted diff of a long text, for Markdown fields. */
export function WordDiff({ before, after }: { before: string; after: string }) {
  const segs = wordSegments(before, after);
  return (
    <div className="wdiff">
      {segs.map((s, i) =>
        s.op === 'add' ? <Ins key={i}>{s.text}</Ins> : s.op === 'del' ? <Del key={i}>{s.text}</Del> : s.text,
      )}
    </div>
  );
}

/** True for a value the reader may not see: protected notes they cannot read. */
export const isMasked = (v: unknown) => v === PROTECTED_MASK;

/** "••• protected", for a protected value the reader may not read. */
export function Masked() {
  const { t } = useI18n();
  return (
    <span className="masked">
      <IconLock size={11} />
      ••• {t.access.maskedValue}
    </span>
  );
}

function Empty() {
  return (
    <span className="muted" aria-label="—">
      —
    </span>
  );
}

/** One side of a changed field in the side-by-side comparison. */
function Side({ d, side, m }: { d: FieldDiff; side: 'before' | 'after'; m: Model | null }) {
  const { t } = useI18n();
  const v = side === 'before' ? d.before : d.after;
  const Mark = side === 'before' ? Del : Ins;
  if (isMasked(v)) return <Masked />;
  if (d.kind === 'list') {
    const c = listChanges(d.before, d.after);
    const items = side === 'before' ? c.before : c.after;
    const changed = new Set(side === 'before' ? c.removed : c.added);
    if (!items.length) return <Empty />;
    return (
      <span className="dlist">
        {items.map((x) => (
          <span key={x}>
            {changed.has(x) ? <Mark>{displayValue(t, m, d.field, x)}</Mark> : displayValue(t, m, d.field, x)}
          </span>
        ))}
      </span>
    );
  }
  // references come with the record names from the server; the landscape resolves the rest
  const title = side === 'before' ? d.beforeTitle : d.afterTitle;
  const text = title || displayValue(t, m, d.field, v);
  if (!text) return <Empty />;
  return <Mark>{text}</Mark>;
}

/**
 * Field-level comparison of a record: published version on the left, proposed version on the right; on phones the two
 * sides stack. Markdown fields span both columns with an inline word diff.
 */
export function DiffView({
  diff,
  m,
  leftTitle,
  rightTitle,
  conflicts = [],
  afterOnly = false,
  notesOwner,
}: {
  diff: FieldDiff[];
  m: Model | null;
  leftTitle: string;
  rightTitle: string;
  conflicts?: string[];
  /** A new record: only the proposed values, without an empty "before" column. */
  afterOnly?: boolean;
  /** Who reads the protected notes, for the line under a masked change. */
  notesOwner?: string;
}) {
  const { t } = useI18n();
  if (!diff.length) return <p className="muted dempty">{t.review.noChange}</p>;
  const masked = (d: FieldDiff) => isMasked(d.before) || isMasked(d.after);
  const short = diff.filter((d) => d.kind !== 'markdown' || masked(d));
  const long = diff.filter((d) => d.kind === 'markdown' && !masked(d));
  const maskedChange = diff.some(masked);
  const flag = (d: FieldDiff) =>
    conflicts.includes(d.field) ? (
      <span className="tg warn" title={t.review.conflictHint}>
        {t.review.conflict}
      </span>
    ) : null;
  return (
    <div className="dview">
      {short.length > 0 && (
        <div className={'diff' + (afterOnly ? ' one' : '')}>
          {(afterOnly ? (['after'] as const) : (['before', 'after'] as const)).map((side) => (
            <div key={side} className={'dside ' + side}>
              <h3>{side === 'before' ? leftTitle : rightTitle}</h3>
              <dl>
                {short.map((d) => (
                  <div key={d.field} className="drow">
                    <dt>
                      {fieldLabel(t, d)} {side === 'after' && flag(d)}
                    </dt>
                    <dd>
                      <Side d={d} side={side} m={m} />
                    </dd>
                  </div>
                ))}
              </dl>
            </div>
          ))}
        </div>
      )}
      {long.map((d) => (
        <div key={d.field} className="dlong">
          <h3>
            {fieldLabel(t, d)} {flag(d)}
          </h3>
          {afterOnly ? (
            <div className="wdiff">{asText(d.after)}</div>
          ) : (
            <WordDiff before={asText(d.before)} after={asText(d.after)} />
          )}
        </div>
      ))}
      {maskedChange && (
        <p className="masked-note">
          <IconInfo size={14} />
          {t.access.maskedNote(notesOwner ?? null)}
        </p>
      )}
    </div>
  );
}
