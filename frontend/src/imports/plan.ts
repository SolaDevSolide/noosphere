import type { ImportEntry, ImportFormat, ImportMessage, ImportPlan, ImportSource } from '../api/types';

/** Sources offered on the import page: the formats of the import, and the exchange format of Noosphere. */
export type SourceId = ImportFormat | 'native';
export const SOURCES: SourceId[] = ['backstage', 'openapi', 'asyncapi', 'adr', 'mermaid', 'excel', 'native'];

/** Files the file picker offers for each source; zip archives everywhere but the exchange format. */
export const ACCEPT: Record<SourceId, string> = {
  backstage: '.yaml,.yml,.zip',
  openapi: '.yaml,.yml,.json,.zip',
  asyncapi: '.yaml,.yml,.json,.zip',
  adr: '.md,.markdown,.zip',
  mermaid: '.mmd,.mermaid,.md,.zip',
  excel: '.xlsx,.zip',
  native: '.json,.yaml,.yml,application/json',
};

/** The whole upload, as the server limits it. */
export const MAX_UPLOAD_BYTES = 20 * 1024 * 1024;

/** True for an entry that writes something, and so can be left out. */
export const writes = (e: ImportEntry) => e.action === 'create' || e.action === 'update';

/**
 * Leaves an entry out, with every entry that requires it (directly or through another one): leaving out a new domain
 * leaves out the applications created in it.
 */
export function leaveOut(entries: ImportEntry[], skipped: ReadonlySet<number>, index: number): Set<number> {
  const out = new Set(skipped);
  const queue = [index];
  while (queue.length) {
    const i = queue.pop()!;
    if (out.has(i) || !entries[i] || !writes(entries[i])) continue;
    out.add(i);
    for (const e of entries) if (e.requires.includes(i)) queue.push(e.index);
  }
  return out;
}

/** Takes an entry back, with every entry it requires. */
export function takeBack(entries: ImportEntry[], skipped: ReadonlySet<number>, index: number): Set<number> {
  const out = new Set(skipped);
  const queue = [index];
  while (queue.length) {
    const i = queue.pop()!;
    if (!out.has(i)) continue;
    out.delete(i);
    queue.push(...(entries[i]?.requires ?? []));
  }
  return out;
}

/** The entries left out that made this one go too (those it requires, left out). */
export const leftOutBecause = (e: ImportEntry, skipped: ReadonlySet<number>) =>
  e.requires.filter((r) => skipped.has(r));

/** What applying the plan writes, once the entries in `skipped` are left out. */
export function tally(plan: Pick<ImportPlan, 'entries'>, skipped: ReadonlySet<number>) {
  let create = 0;
  let update = 0;
  let unchanged = 0;
  let left = 0;
  for (const e of plan.entries) {
    if (e.action === 'unchanged') unchanged++;
    else if (skipped.has(e.index)) left++;
    else if (e.action === 'create') create++;
    else update++;
  }
  return { create, update, unchanged, left, changes: create + update };
}

/** "catalog-info.yaml:49 · documents[4]" */
export function sourceText(s: ImportSource): string {
  const at = s.file ? s.file + (s.line ? `:${s.line}` : '') : '';
  return [at, s.place].filter(Boolean).join(' · ');
}

export type RowFilter = 'all' | 'create' | 'update' | 'unchanged' | 'warning' | 'error';

/** One line of the preview: an entry of the plan, or a warning or an error about the files. */
export type Row =
  | { type: 'entry'; entry: ImportEntry; warnings: ImportMessage[]; errors: ImportMessage[] }
  | { type: 'warning' | 'error'; message: ImportMessage };

/** Lines of the preview: entries with their own messages, then the messages about no entry. */
export function rows(plan: ImportPlan): Row[] {
  const mine = (list: ImportMessage[], i: number) => list.filter((m) => m.entry === i);
  const out: Row[] = plan.entries.map((entry) => ({
    type: 'entry',
    entry,
    warnings: mine(plan.warnings, entry.index),
    errors: mine(plan.errors, entry.index),
  }));
  for (const message of plan.errors) if (message.entry == null) out.push({ type: 'error', message });
  for (const message of plan.warnings) if (message.entry == null) out.push({ type: 'warning', message });
  return out;
}

export function matches(row: Row, filter: RowFilter): boolean {
  if (filter === 'all') return true;
  if (row.type !== 'entry') return row.type === filter;
  if (filter === 'warning') return row.warnings.length > 0;
  if (filter === 'error') return row.errors.length > 0 || row.entry.hasErrors;
  return row.entry.action === filter;
}

export function filterCounts(all: Row[]): Record<RowFilter, number> {
  const filters: RowFilter[] = ['all', 'create', 'update', 'unchanged', 'warning', 'error'];
  return Object.fromEntries(filters.map((f) => [f, all.filter((r) => matches(r, f)).length])) as Record<
    RowFilter,
    number
  >;
}

const cell = (v: string | number | null | undefined) => {
  const s = v == null ? '' : String(v);
  // Formulas are neutralised so that a spreadsheet never runs a value of the files.
  const safe = /^[=+\-@\t\r]/.test(s) ? `'${s}` : s;
  return /[",\n\r;]/.test(safe) ? `"${safe.replace(/"/g, '""')}"` : safe;
};

/** The preview as a CSV report: one line per entry, then the messages about no entry. */
export function planCsv(
  plan: ImportPlan,
  skipped: ReadonlySet<number>,
  words: { header: string[]; yes: string; no: string },
): string {
  const lines = [words.header.map(cell).join(',')];
  for (const r of rows(plan)) {
    if (r.type === 'entry') {
      const e = r.entry;
      lines.push(
        [
          e.index,
          e.action,
          writes(e) ? (skipped.has(e.index) ? words.no : words.yes) : '',
          e.kind,
          e.id,
          e.name,
          e.source.file,
          e.source.line,
          e.source.place,
          e.changes.map((c) => c.field).join(' '),
          e.possibleMatches.map((m) => `${m.name} (${m.id})`).join('; '),
          [...r.errors, ...r.warnings].map((m) => m.message).join(' | '),
        ]
          .map(cell)
          .join(','),
      );
    } else {
      const m = r.message;
      lines.push(
        ['', r.type, '', '', '', '', m.source.file, m.source.line, m.source.place, m.field, '', m.message]
          .map(cell)
          .join(','),
      );
    }
  }
  return '﻿' + lines.join('\r\n') + '\r\n';
}

/** Saves a text as a downloaded file. */
export function saveText(text: string, name: string, type = 'text/csv;charset=utf-8') {
  const url = URL.createObjectURL(new Blob([text], { type }));
  const a = document.createElement('a');
  a.href = url;
  a.download = name;
  document.body.appendChild(a);
  a.click();
  a.remove();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}
