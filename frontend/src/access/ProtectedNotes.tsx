import type { ReactNode } from 'react';
import type { NotesExtras } from '../api/types';
import { useAuth } from '../auth/auth';
import { useI18n } from '../i18n';
import { describe } from '../records/common';
import { IconInfo, IconLock, IconWarn } from '../ui/icons';
import { Markdown } from '../ui/Markdown';
import { PlaceholderHint } from './secret';

export const NOTES_MAX = 20000;

/**
 * The protected notes of an application or flow: the text for those who may read it, a lock for the others. Nothing
 * when the record has none.
 */
export function ProtectedNotesSection({
  record,
  ownerId,
  ownerName,
  restricted,
}: {
  record: NotesExtras;
  ownerId: string | null;
  ownerName: string;
  restricted: boolean;
}) {
  const { t } = useI18n();
  const { isAdmin, teamIds } = useAuth();
  const text = record.protectedNotes;
  if (record.protectedNotesHidden)
    return (
      <section className="box pnotes" aria-label={t.access.notesTitle}>
        <div className="pn-lock">
          <span className="lk">
            <IconLock size={18} />
          </span>
          <div>
            <b>{t.access.notesHiddenTitle(ownerName)}</b>
            <p>{t.access.notesHiddenText(ownerName)}</p>
          </div>
        </div>
      </section>
    );
  if (!text) return null;
  const inOwner = !!ownerId && teamIds.includes(ownerId);
  const foot =
    isAdmin && !inOwner
      ? t.access.notesFootAdmin
      : inOwner
        ? t.access.notesFootOwner(ownerName)
        : t.access.notesFootListed;
  return (
    <section className="box pnotes" aria-labelledby="pn-h">
      <div className="bx-h">
        <h2 id="pn-h">
          <IconLock />
          {t.access.notesTitle}
        </h2>
        <span className="pn-who">
          <IconLock size={12} />
          {restricted ? t.access.visibleToPeopleAbove : t.access.visibleTo(ownerName)}
        </span>
      </div>
      <Markdown text={text} />
      <p className="pn-foot">
        <IconInfo size={14} />
        {foot}
      </p>
    </section>
  );
}

/** The "Protected notes" line of the record details. */
export function notesDetail(t: ReturnType<typeof useI18n>['t'], record: NotesExtras, ownerName: string): ReactNode {
  if (record.protectedNotesHidden) return t.access.detailsHidden(ownerName);
  if (record.protectedNotes) return t.access.detailsYes(ownerName);
  return t.common.none;
}

/**
 * The protected notes field of an edit form. Readers edit the text; people who may not read the notes write
 * replacement notes (left empty, the current ones are kept).
 */
export function ProtectedNotesField({
  value,
  onChange,
  hidden,
  who,
  error,
}: {
  value: string;
  onChange: (v: string) => void;
  hidden: boolean;
  who: string;
  error?: string;
}) {
  const { t } = useI18n();
  const id = 'protectedNotes';
  return (
    <div className="field full">
      <label htmlFor={id}>
        {t.access.notesTitle}
        <span className="lockl">
          <IconLock size={11} />
          {t.access.visibleTo(who)}
        </span>
      </label>
      {hidden && (
        <small id={id + '-hidden'} style={{ marginTop: 0, marginBottom: 6 }}>
          {t.access.notesHiddenEdit}
        </small>
      )}
      <textarea
        className="textarea mono"
        {...describe(id, 'x', error)}
        aria-describedby={[hidden && id + '-hidden', error && id + '-err', id + '-hint'].filter(Boolean).join(' ')}
        rows={7}
        value={value}
        onChange={(e) => onChange(e.target.value)}
        spellCheck={false}
      />
      {error && (
        <span className="err" id={id + '-err'} role="alert">
          <IconWarn size={13} />
          {error}
        </span>
      )}
      <PlaceholderHint id={id + '-hint'} />
    </div>
  );
}
