import { useEffect, useState, type ReactNode } from 'react';
import { Link } from 'react-router-dom';
import { ApiError } from '../api/client';
import { useDocumentTitle } from '../app/settings';
import { useI18n } from '../i18n';
import { TopBar } from '../layout/TopBar';
import { IconWarn } from '../ui/icons';
import type { Dict } from '../i18n/en';
import { fieldKey, translateMessage } from '../access/secret';

export function PageShell({ children, narrow, title }: { children: ReactNode; narrow?: boolean; title?: string }) {
  useDocumentTitle(title);
  return (
    <div className="shell">
      <TopBar />
      <main className="page" id="main">
        <div className={'page-in' + (narrow ? ' narrow' : '')}>{children}</div>
      </main>
    </div>
  );
}

/** Loads one record by id; `reload` fetches it again (after a conflict, for example). */
export function useRecord<T>(load: (id: string) => Promise<T>, id: string) {
  const [data, setData] = useState<T | null>(null);
  const [error, setError] = useState<ApiError | null>(null);
  const [tick, setTick] = useState(0);
  useEffect(() => {
    let alive = true;
    setError(null);
    load(id)
      .then((d) => alive && setData(d))
      .catch((e: unknown) => alive && setError(e instanceof ApiError ? e : new ApiError(0, 'network')));
    return () => {
      alive = false;
    };
    // load is a stable api function
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [id, tick]);
  return { data, setData, error, reload: () => setTick((x) => x + 1) };
}

export function RecordSkeleton() {
  return (
    <div aria-busy="true">
      <span className="sk line" style={{ width: 120 }} />
      <span className="sk title" style={{ width: '50%', marginTop: 10 }} />
      <span className="sk line" style={{ width: '30%', marginTop: 8 }} />
      <div className="grid2" style={{ marginTop: 24 }}>
        <span className="sk block" style={{ height: 220 }} />
        <span className="sk block" style={{ height: 220 }} />
      </div>
    </div>
  );
}

export function RecordError({ error, onRetry }: { error: ApiError; onRetry: () => void }) {
  const { t } = useI18n();
  return (
    <div className="banner danger" role="alert">
      <IconWarn />
      <div className="bt">
        <b>
          {error.status === 404
            ? t.common.notFound
            : error.status === 0
              ? t.common.networkError
              : error.detail || error.message || t.common.genericError}
        </b>
        <div className="page-actions" style={{ marginTop: 10 }}>
          {error.status !== 404 && (
            <button type="button" className="btn" onClick={onRetry}>
              {t.common.retry}
            </button>
          )}
          <Link className="btn" to="/">
            {t.common.backToExplorer}
          </Link>
        </div>
      </div>
    </div>
  );
}

export function ConflictBanner({ onReload }: { onReload: () => void }) {
  const { t } = useI18n();
  return (
    <div className="banner warn" role="alert">
      <IconWarn />
      <div className="bt">
        <b>{t.record.conflict}</b>
        <div>{t.record.conflictText}</div>
        <div className="page-actions" style={{ marginTop: 10 }}>
          <button type="button" className="btn primary" onClick={onReload}>
            {t.common.reload}
          </button>
        </div>
      </div>
    </div>
  );
}

export function Field({
  id,
  label,
  required,
  hint,
  error,
  full,
  children,
}: {
  id: string;
  label: string;
  required?: boolean;
  hint?: string;
  error?: string;
  full?: boolean;
  children: ReactNode;
}) {
  return (
    <div className={'field' + (full ? ' full' : '')}>
      <label htmlFor={id}>
        {label}
        {required && (
          <span className="req" aria-hidden="true">
            {' '}
            *
          </span>
        )}
      </label>
      {children}
      {hint && <small id={id + '-hint'}>{hint}</small>}
      {error && (
        <span className="err" id={id + '-err'} role="alert">
          <IconWarn size={13} />
          {error}
        </span>
      )}
    </div>
  );
}

/** aria props tying an input to its hint and error. */
export const describe = (id: string, hint?: string, error?: string) => ({
  id,
  'aria-invalid': error ? true : undefined,
  'aria-describedby': [hint && id + '-hint', error && id + '-err'].filter(Boolean).join(' ') || undefined,
});

/**
 * Server validation messages, keyed by field name in camelCase (the proposal prefix `changes[0].after.` removed). With
 * the dictionary, secret refusals are translated.
 */
export function serverErrors(e: ApiError, t?: Dict): Record<string, string> {
  const out: Record<string, string> = {};
  for (const [k, v] of Object.entries(e.fieldErrors)) {
    const key = fieldKey(k);
    const msgs = (Array.isArray(v) ? v : [String(v)]).map((m) => (t ? translateMessage(t, m) : m));
    out[key] = out[key] ? out[key] + ' ' + msgs.join(' ') : msgs.join(' ');
  }
  return out;
}

export function Missing({ items }: { items: string[] }) {
  const { t } = useI18n();
  if (!items.length) return null;
  return (
    <p className="muted" style={{ margin: '8px 0 0', fontSize: 13 }}>
      {t.record.missing}: {items.join(', ')}
    </p>
  );
}

/** Warn before leaving a form with unsaved changes. */
export function useUnsavedGuard(dirty: boolean) {
  useEffect(() => {
    if (!dirty) return;
    const h = (e: BeforeUnloadEvent) => {
      e.preventDefault();
    };
    window.addEventListener('beforeunload', h);
    return () => window.removeEventListener('beforeunload', h);
  }, [dirty]);
}
