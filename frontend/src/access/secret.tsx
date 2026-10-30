import { ApiError } from '../api/client';
import type { Dict } from '../i18n/en';
import { useI18n } from '../i18n';
import { IconWarn } from '../ui/icons';

const SECRET = /^This looks like a secret \(([^)]+)\)/;

/** True for a server message refusing a text because it looks like a secret. */
export const isSecretMessage = (m: string) => SECRET.test(m);

/** The secret refusal in the reader's language; other messages unchanged. */
export function translateMessage(t: Dict, m: string): string {
  const k = SECRET.exec(m)?.[1];
  return k ? t.access.secretMessage(t.access.secretKinds[k] ?? k) : m;
}

/**
 * Field name of a server error, as the forms name their fields: camelCase, without the JSON path (`$.`) or the proposal
 * prefix (`changes[0].after.`).
 */
export function fieldKey(k: string): string {
  const bare = k.replace(/^\$\./, '').replace(/^changes\[\d+\]\.(after\.)?/i, '');
  return bare.charAt(0).toLowerCase() + bare.slice(1);
}

/** A refused save: 400 (invalid fields) or 422 (a field looks like a secret), with field messages. */
export const isFieldRefusal = (e: unknown): e is ApiError =>
  e instanceof ApiError && (e.status === 400 || e.status === 422) && Object.keys(e.fieldErrors).length > 0;

/** The fields refused because they look like secrets. */
export function secretFields(e: ApiError): string[] {
  return Object.entries(e.fieldErrors)
    .filter(([, v]) => (Array.isArray(v) ? v : [String(v)]).some(isSecretMessage))
    .map(([k]) => fieldKey(k));
}

/** "Not saved. Protected notes look like they contain a secret. Nothing was stored." */
export function SecretBanner({ fields, label }: { fields: string[]; label: (field: string) => string }) {
  const { t } = useI18n();
  if (!fields.length) return null;
  return (
    <div className="banner danger" role="alert">
      <IconWarn />
      <span className="bt">
        <b>{t.access.notSaved}</b>
        {fields.length === 1
          ? t.access.secretOne(label(fields[0]), fields[0] === 'protectedNotes')
          : t.access.secretMany}
      </span>
    </div>
  );
}

/** Reminds which placeholders pass the secret check. */
export function PlaceholderHint({ id, lead }: { id: string; lead?: string }) {
  const { t } = useI18n();
  return (
    <small id={id} className="ph-ok">
      {lead ?? t.access.notesHint}
      {['<password>', '***', 'changeme', '${VAR}', '%VAR%', '{{var}}'].map((p) => (
        <code key={p}>{p}</code>
      ))}
    </small>
  );
}

/** The label of a form field named by the server, or the field name itself. */
export function fieldLabelOf(t: Dict, field: string): string {
  return (t.record.fields as Record<string, string>)[field] ?? field;
}
