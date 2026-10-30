import { Fragment, type ReactNode } from 'react';
import type { RecordExtras, RecordKind } from '../api/types';
import { RecordPages } from '../docpages/RecordPages';

type Rec = RecordExtras & { id: string };

/** Sections that features add under a record's description: the documentation pages attached to it, for now. */
export const recordSections: ((kind: RecordKind, record: Rec) => ReactNode)[] = [
  (kind, record) => <RecordPages kind={kind} record={record} />,
];

export function RecordExtensions({ kind, record }: { kind: RecordKind; record: Rec }) {
  return (
    <>
      {recordSections.map((render, i) => (
        <Fragment key={i}>{render(kind, record)}</Fragment>
      ))}
    </>
  );
}
