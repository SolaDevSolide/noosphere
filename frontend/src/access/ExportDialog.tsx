import { useEffect, useRef, useState } from 'react';
import { api, ApiError } from '../api/client';
import { isRestrictedApp, isRestrictedFlow, type AccessOverview } from '../api/types';
import { useAuth } from '../auth/auth';
import { useLandscape } from '../explorer/landscape';
import { useI18n } from '../i18n';
import { IconDownload, IconLock, IconWarn } from '../ui/icons';
import { useModal } from '../ui/modal';
import { useToast } from '../ui/Toast';

export const EXPORT_REASON_MAX = 200;

/** Saves a downloaded file through a temporary link. */
function save(blob: Blob, name: string) {
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = name;
  document.body.appendChild(a);
  a.click();
  a.remove();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}

/**
 * "Export the landscape": what the reader can see, or, for administrators, the full landscape with every restricted
 * record and protected note, recorded in the access log with an optional reason.
 */
export function ExportDialog({ onClose }: { onClose: () => void }) {
  const { t } = useI18n();
  const { isAdmin } = useAuth();
  const { model: m } = useLandscape();
  const toast = useToast();
  const box = useRef<HTMLDivElement>(null);
  useModal(box, onClose);
  const [scope, setScope] = useState<'visible' | 'full'>('visible');
  const [format, setFormat] = useState<'json' | 'yaml'>('json');
  const [reason, setReason] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [reasonErr, setReasonErr] = useState<string | null>(null);
  const [overview, setOverview] = useState<AccessOverview | null>(null);

  useEffect(() => {
    if (!isAdmin) return;
    let alive = true;
    api
      .accessOverview()
      .then((o) => alive && setOverview(o))
      .catch(() => {});
    return () => {
      alive = false;
    };
  }, [isAdmin]);

  const apps = m ? [...m.apps.values()].filter((a) => !isRestrictedApp(a)).length : 0;
  const flows = m ? m.flows.filter((f) => !isRestrictedFlow(f)).length : 0;
  const notes = overview ? overview.protectedNotes.length : null;
  const restrictedCount = overview ? overview.restricted.length : 0;
  const full = scope === 'full';

  const go = async () => {
    setError(null);
    setReasonErr(null);
    if (full && reason.trim().length > EXPORT_REASON_MAX) {
      setReasonErr(t.access.reasonTooLong);
      document.getElementById('ex-why')?.focus();
      return;
    }
    setBusy(true);
    try {
      const { blob, name } = await api.exportFile(format, scope, full ? reason : undefined);
      save(blob, name);
      toast(full ? t.access.downloadedFull : t.access.downloaded(name));
      onClose();
    } catch (e) {
      if (e instanceof ApiError && e.fieldErrors.reason) setReasonErr(e.fieldErrors.reason.join(' '));
      else
        setError(
          e instanceof ApiError
            ? e.status === 0
              ? t.common.networkError
              : e.detail || e.message
            : t.common.genericError,
        );
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="dlg-back" role="presentation" onMouseDown={(e) => e.target === e.currentTarget && onClose()}>
      <div ref={box} className="dlg wide" role="dialog" aria-modal="true" aria-labelledby="ex-h" tabIndex={-1}>
        <h2 id="ex-h">{t.access.exportTitle}</h2>
        <p className="dsub">{t.access.exportSub}</p>
        <div className="rcards" role="radiogroup" aria-label={t.access.exportWhat}>
          <label className="rcard">
            <input
              type="radio"
              name="scope"
              value="visible"
              checked={!full}
              onChange={() => setScope('visible')}
              data-autofocus
            />
            <b>{t.access.visibleH}</b>
            <small>{t.access.visibleText}</small>
            {m && (
              <span className="meta">
                {t.access.counts(apps, flows)}
                {notes !== null && <> · {t.access.notesCount(notes)}</>}
              </span>
            )}
          </label>
          <label className={'rcard' + (isAdmin ? '' : ' disabled')}>
            <input
              type="radio"
              name="scope"
              value="full"
              checked={full}
              disabled={!isAdmin}
              onChange={() => setScope('full')}
            />
            <b>
              {t.access.fullH} <span className="rolebadge admin">{t.access.adminsBadge}</span>
            </b>
            <small>{t.access.fullText}</small>
            {!isAdmin && (
              <span className="meta">
                <IconLock size={12} />
                {t.access.fullOnlyAdmins}
              </span>
            )}
          </label>
        </div>
        {full && (
          <div className="full-only">
            <div className="field" style={{ marginTop: 14 }}>
              <label htmlFor="ex-why">
                {t.access.reason}{' '}
                <span className="muted" style={{ fontWeight: 400 }}>
                  {t.access.reasonHint}
                </span>
              </label>
              <input
                className="input"
                id="ex-why"
                value={reason}
                maxLength={EXPORT_REASON_MAX + 20}
                aria-invalid={reasonErr ? true : undefined}
                aria-describedby={reasonErr ? 'ex-why-err' : undefined}
                onChange={(e) => setReason(e.target.value)}
                autoComplete="off"
              />
              {reasonErr && (
                <span className="err" id="ex-why-err" role="alert">
                  <IconWarn size={13} />
                  {reasonErr}
                </span>
              )}
            </div>
            {overview && (
              <div className="banner danger" role="note">
                <IconWarn />
                <span className="bt">
                  <b>{t.access.fullWarn(overview.protectedNotes.length, restrictedCount)}</b> {t.access.fullWarnText}
                </span>
              </div>
            )}
          </div>
        )}
        <span className="sec-l" id="ex-fmt">
          {t.access.format}
        </span>
        <div className="seg" role="group" aria-labelledby="ex-fmt">
          {(['json', 'yaml'] as const).map((f) => (
            <button key={f} type="button" aria-pressed={format === f} onClick={() => setFormat(f)}>
              {f.toUpperCase()}
            </button>
          ))}
        </div>
        {error && (
          <div className="banner danger" role="alert" style={{ marginTop: 14 }}>
            <IconWarn />
            <span className="bt">{error}</span>
          </div>
        )}
        <div className="form-actions">
          <button type="button" className="btn" onClick={onClose} disabled={busy}>
            {t.common.cancel}
          </button>
          <button type="button" className="btn primary" onClick={() => void go()} disabled={busy}>
            <IconDownload />
            {busy ? t.access.downloading : t.access.download}
          </button>
        </div>
      </div>
    </div>
  );
}
