import { createContext, useContext, useMemo, useState } from 'react';
import { Link as RouterLink } from 'react-router-dom';
import type { FieldDiff, ImportEntry, ImportMessage, ImportPlan } from '../api/types';
import { recordHref } from '../docpages/model';
import type { Model } from '../domain/model';
import { useI18n } from '../i18n';
import type { Dict } from '../i18n/en';
import { displayValue, fieldLabel, WordDiff } from '../review/diff';
import { IconWarn } from '../ui/icons';
import {
  filterCounts,
  leftOutBecause,
  matches,
  rows as planRows,
  sourceText,
  writes,
  type Row,
  type RowFilter,
} from './plan';

const FILTERS: RowFilter[] = ['all', 'create', 'update', 'unchanged', 'warning', 'error'];
const SHOWN_FIELDS = 4;

const text = (v: unknown) => (v == null ? '' : typeof v === 'string' ? v : JSON.stringify(v));

/** Kind of the record a reference field names. */
const REF_KIND: Record<string, string> = {
  domainId: 'domain',
  teamId: 'team',
  ownerTeamId: 'team',
  fromId: 'application',
  toId: 'application',
  sourceApplicationId: 'application',
  dataObjectIds: 'dataObject',
  parentId: 'page',
};

/** Names of the records of the plan, by "kind:id": references to records the import creates show their name. */
const PlanNames = createContext<Map<string, string>>(new Map());

function value(t: Dict, m: Model | null, names: Map<string, string>, d: FieldDiff, side: 'before' | 'after'): string {
  const v = side === 'before' ? d.before : d.after;
  const title = side === 'before' ? d.beforeTitle : d.afterTitle;
  const one = (x: unknown) => {
    const shown = displayValue(t, m, d.field, x);
    return (
      (REF_KIND[d.field] && typeof x === 'string' && shown === x && names.get(`${REF_KIND[d.field]}:${x}`)) || shown
    );
  };
  if (Array.isArray(v)) return v.map(one).join(', ');
  return title || one(v);
}

const excerpt = (s: string, n = 110) => {
  const flat = s
    .replace(/[#*_`>[\]]/g, '')
    .replace(/\s+/g, ' ')
    .trim();
  return flat.length > n ? flat.slice(0, n - 1) + '…' : flat;
};

/** Fields set by a creation: a short line, "Owner Team CRM · Lifecycle Live · +2 more". */
function CreateSummary({ changes, m }: { changes: FieldDiff[]; m: Model | null }) {
  const { t } = useI18n();
  const names = useContext(PlanNames);
  const short = changes.filter((d) => d.kind !== 'markdown' && d.field !== 'id' && d.field !== 'name');
  const shown = short.slice(0, SHOWN_FIELDS);
  if (!shown.length) {
    const long = changes.find((d) => d.kind === 'markdown' && typeof d.after === 'string' && d.after.trim());
    return long ? <span className="ip-set">{excerpt(long.after as string)}</span> : null;
  }
  return (
    <span className="ip-set">
      {shown.map((d, i) => (
        <span key={d.field}>
          {i > 0 && ' · '}
          <span className="ip-f">{fieldLabel(t, d)}</span> {value(t, m, names, d, 'after') || '—'}
        </span>
      ))}
      {short.length > shown.length && <> · {t.imports.andMore(short.length - shown.length)}</>}
    </span>
  );
}

/** Changed fields of an update: the old value struck, the new one after it; long texts open a word diff. */
function UpdateDiff({ changes, m }: { changes: FieldDiff[]; m: Model | null }) {
  const { t } = useI18n();
  const names = useContext(PlanNames);
  return (
    <ul className="ip-diff">
      {changes.map((d) =>
        d.kind === 'markdown' ? (
          <li key={d.field}>
            <details>
              <summary>
                {t.imports.textChanged(fieldLabel(t, d))} <span className="ip-show">{t.imports.showText}</span>
              </summary>
              <WordDiff before={text(d.before)} after={text(d.after)} />
            </details>
          </li>
        ) : (
          <li key={d.field}>
            <span className="ip-f">{fieldLabel(t, d)}</span>{' '}
            {value(t, m, names, d, 'before') && (
              <del className="d">
                <span className="sr">{t.review.removed} </span>
                {value(t, m, names, d, 'before')}
              </del>
            )}{' '}
            <ins className="d">
              <span className="sr">{t.review.added} </span>
              {value(t, m, names, d, 'after') || '—'}
            </ins>
          </li>
        ),
      )}
    </ul>
  );
}

function Messages({ list, kind }: { list: ImportMessage[]; kind: 'warning' | 'error' }) {
  if (!list.length) return null;
  return (
    <ul className={'ip-msgs ' + kind}>
      {list.map((x, i) => (
        <li key={i}>
          <IconWarn size={13} />
          <span>{x.message}</span>
        </li>
      ))}
    </ul>
  );
}

function EntryRow({
  row,
  m,
  skipped,
  locked,
  byIndex,
  onToggle,
}: {
  row: Extract<Row, { type: 'entry' }>;
  m: Model | null;
  skipped: ReadonlySet<number>;
  locked: boolean;
  byIndex: (i: number) => ImportEntry | undefined;
  onToggle: (index: number, keep: boolean) => void;
}) {
  const { t } = useI18n();
  const T = t.imports;
  const e = row.entry;
  const out = writes(e) && skipped.has(e.index);
  const because = out ? leftOutBecause(e, skipped) : [];
  const names = (ix: number[]) =>
    ix
      .map((i) => byIndex(i)?.name)
      .filter(Boolean)
      .join(', ');
  const chip = { create: 'ok', update: 'info', unchanged: 'muted' }[e.action];
  const id = `ip-e${e.index}`;
  return (
    <tr className={(e.action === 'unchanged' ? 'dim' : '') + (out ? ' off' : '')}>
      <td data-l={T.cols.action} className="ip-act">
        <span className="ip-actw">
          {writes(e) && (
            <input
              type="checkbox"
              className="ip-cb"
              checked={!out}
              disabled={locked}
              aria-label={T.keep(e.name)}
              aria-describedby={id}
              onChange={(ev) => onToggle(e.index, ev.target.checked)}
            />
          )}
          <span className={'pchip ' + chip}>{T.action[e.action]}</span>
        </span>
      </td>
      <td data-l={T.cols.file}>
        <span className="mono ip-src">{sourceText(e.source)}</span>
      </td>
      <td data-l={T.cols.noosphere} className="ip-rec">
        <b id={id}>
          {e.action === 'create' ? e.name : <RouterLink to={recordHref(e.kind, e.id)}>{e.name}</RouterLink>}
        </b>
        <small>
          {t.review.kind[e.kind]}
          {e.matchedBy && <> · {T.matchedBy[e.matchedBy] ?? e.matchedBy}</>}
          {e.action === 'create' && <span className="mono"> · {e.id}</span>}
        </small>
        {e.possibleMatches.length > 0 && (
          <span className="ip-close">
            {T.possible}{' '}
            {e.possibleMatches.map((p, i) => (
              <span key={p.id}>
                {i > 0 && ', '}
                <RouterLink to={recordHref(e.kind, p.id)}>{p.name}</RouterLink>
              </span>
            ))}
          </span>
        )}
      </td>
      <td data-l={T.cols.details} className="ip-det">
        {e.action === 'create' && <CreateSummary changes={e.changes} m={m} />}
        {e.action === 'update' && <UpdateDiff changes={e.changes} m={m} />}
        {e.action === 'unchanged' && <span className="none">{T.identical}</span>}
        <Messages list={row.errors} kind="error" />
        <Messages list={row.warnings} kind="warning" />
        {!out && e.requires.length > 0 && <span className="ip-note">{T.requires(names(e.requires))}</span>}
        {out && <span className="ip-note out">{because.length ? T.leftOutWith(names(because)) : T.leftOut}</span>}
      </td>
    </tr>
  );
}

function MessageRow({ row }: { row: Extract<Row, { type: 'warning' | 'error' }> }) {
  const { t } = useI18n();
  const T = t.imports;
  return (
    <tr>
      <td data-l={T.cols.action} className="ip-act">
        <span className="ip-actw">
          <span className={'pchip ' + (row.type === 'error' ? 'danger' : 'warn')}>{T.action[row.type]}</span>
        </span>
      </td>
      <td data-l={T.cols.file}>
        <span className="mono ip-src">{sourceText(row.message.source) || T.none}</span>
      </td>
      <td data-l={T.cols.noosphere}>
        <span className="none">{T.none}</span>
      </td>
      <td data-l={T.cols.details} className="ip-det">
        {row.message.message}
      </td>
    </tr>
  );
}

/**
 * The preview of a plan: counts, filters, and one line per entry of the files with its record in Noosphere, its field
 * diff, its messages and a box to leave it out.
 */
export function PlanPreview({
  plan,
  m,
  skipped,
  locked,
  onToggle,
}: {
  plan: ImportPlan;
  m: Model | null;
  skipped: ReadonlySet<number>;
  locked: boolean;
  onToggle: (index: number, keep: boolean) => void;
}) {
  const { t } = useI18n();
  const T = t.imports;
  const [filter, setFilter] = useState<RowFilter>('all');
  const all = useMemo(() => planRows(plan), [plan]);
  const counts = useMemo(() => filterCounts(all), [all]);
  const names = useMemo(() => new Map(plan.entries.map((e) => [`${e.kind}:${e.id}`, e.name])), [plan]);
  const shown = all.filter((r) => matches(r, filter));
  const byIndex = (i: number) => plan.entries[i];
  const c = plan.counts;
  const tiles: [string, number, string][] = [
    ['c', c.create, T.tiles.create],
    ['u', c.update, T.tiles.update],
    ['', c.unchanged, T.tiles.unchanged],
    ['w', c.warnings, T.tiles.warnings],
    ...(c.errors ? ([['e', c.errors, T.tiles.errors]] as [string, number, string][]) : []),
    ['d', 0, T.tiles.deleted],
  ];
  return (
    <PlanNames.Provider value={names}>
      <div className="ip-body">
        <ul className={'ip-tiles' + (c.errors ? ' six' : '')}>
          {tiles.map(([cls, n, label]) => (
            <li key={label} className={'stat ' + cls}>
              <b>{n}</b>
              <span>{label}</span>
            </li>
          ))}
        </ul>
        {all.length > 0 && (
          <div className="ip-filters">
            <div className="seg wrap2" role="group" aria-label={T.filterLabel}>
              {FILTERS.filter((f) => f !== 'error' || counts.error > 0).map((f) => (
                <button key={f} type="button" aria-pressed={filter === f} onClick={() => setFilter(f)}>
                  {T.filters[f]} <span className="cnt">{counts[f]}</span>
                </button>
              ))}
            </div>
            <span className="muted2 ip-match">{T.matchNote}</span>
          </div>
        )}
      </div>
      {all.length === 0 ? (
        <p className="log-empty">{T.emptyPlan}</p>
      ) : shown.length === 0 ? (
        <p className="log-empty">{T.noEntries}</p>
      ) : (
        <table className="table rtable ip-table">
          <thead>
            <tr>
              <th scope="col">{T.cols.action}</th>
              <th scope="col">{T.cols.file}</th>
              <th scope="col">{T.cols.noosphere}</th>
              <th scope="col">{T.cols.details}</th>
            </tr>
          </thead>
          <tbody>
            {shown.map((r) =>
              r.type === 'entry' ? (
                <EntryRow
                  key={'e' + r.entry.index}
                  row={r}
                  m={m}
                  skipped={skipped}
                  locked={locked}
                  byIndex={byIndex}
                  onToggle={onToggle}
                />
              ) : (
                <MessageRow key={r.type + all.indexOf(r)} row={r} />
              ),
            )}
          </tbody>
        </table>
      )}
    </PlanNames.Provider>
  );
}
