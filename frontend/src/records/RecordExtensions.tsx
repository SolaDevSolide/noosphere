import { Fragment, type ReactNode } from 'react';
import type { RecordExtras } from '../api/types';

/**
 * Sections that features add under a record's description. The documentation pages attached to a record
 * (`record.pages`, `record.mentionedIn`) plug in here once their design is validated; nothing is registered yet.
 */
export const recordSections: ((record: RecordExtras & { id: string }) => ReactNode)[] = [];

export function RecordExtensions({ record }: { record: RecordExtras & { id: string } }) {
  return (
    <>
      {recordSections.map((render, i) => (
        <Fragment key={i}>{render(record)}</Fragment>
      ))}
    </>
  );
}
