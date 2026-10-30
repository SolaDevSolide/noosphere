import { useEffect, useState } from 'react';
import { api, ApiError } from '../api/client';
import type { TrashItem } from '../api/types';
import { useAuth } from '../auth/auth';
import { useLandscape } from '../explorer/landscape';
import { useI18n } from '../i18n';
import { PageShell } from '../records/common';
import { IconTrash, IconWarn } from '../ui/icons';
import { useToast } from '../ui/Toast';

/** Deleted records keep their history; administrators bring them back from here. */
export function TrashPage() {
  const { t, date } = useI18n();
  const { isAdmin } = useAuth();
  const { reload: reloadLandscape } = useLandscape();
  const toast = useToast();
  const [items, setItems] = useState<TrashItem[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState<string | null>(null);
  const [tick, setTick] = useState(0);

  useEffect(() => {
    if (!isAdmin) return;
    let alive = true;
    setError(null);
    api
      .trash()
      .then((x) => alive && setItems(x))
      .catch(
        (e: unknown) =>
          alive && setError(e instanceof ApiError && e.status === 0 ? t.common.networkError : t.common.genericError),
      );
    return () => {
      alive = false;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [isAdmin, tick]);

  if (!isAdmin)
    return (
      <PageShell narrow title={t.trash.title}>
        <div className="page-h">
          <div className="ph-t">
            <h1>{t.trash.title}</h1>
          </div>
        </div>
        <div className="banner" role="note">
          <IconWarn />
          <span className="bt">{t.trash.forbidden}</span>
        </div>
      </PageShell>
    );

  const restore = async (x: TrashItem) => {
    if (!window.confirm(t.trash.restoreConfirm(x.title))) return;
    const k = x.kind + ':' + x.entityId;
    setBusy(k);
    try {
      await api.restoreTrash(x.kind, x.entityId);
      setItems((l) => l?.filter((y) => y !== x) ?? null);
      toast(t.trash.restored(x.title));
      reloadLandscape();
    } catch (e) {
      toast(e instanceof ApiError ? e.detail || e.message : t.common.genericError);
    } finally {
      setBusy(null);
    }
  };

  return (
    <PageShell title={t.trash.title}>
      <div className="page-h">
        <div className="ph-t">
          <h1>{t.trash.title}</h1>
          <p>{t.trash.intro}</p>
        </div>
      </div>
      {error && (
        <div className="banner danger" role="alert">
          <IconWarn />
          <span className="bt">
            {error}{' '}
            <button type="button" className="btn" onClick={() => setTick((x) => x + 1)}>
              {t.common.retry}
            </button>
          </span>
        </div>
      )}
      {!items && !error && (
        <div aria-busy="true">
          {Array.from({ length: 3 }, (_, i) => (
            <span key={i} className="sk block" style={{ height: 64, marginBottom: 8 }} />
          ))}
        </div>
      )}
      {items && items.length === 0 && (
        <div className="pempty">
          <IconTrash size={22} />
          <p>{t.trash.empty}</p>
        </div>
      )}
      {items && items.length > 0 && (
        <ul className="plist" aria-label={t.trash.count(items.length)}>
          {items.map((x) => (
            <li key={x.kind + ':' + x.entityId} className="pcard trow">
              <div className="trow-t">
                <b>{x.title}</b>
                <small className="muted">
                  {t.review.kind[x.kind as keyof typeof t.review.kind] ?? x.kind} · <code>{x.entityId}</code> · v
                  {x.version}
                </small>
                <small className="muted">
                  {t.trash.deletedBy(x.deletedBy?.displayName ?? t.history.system, date(x.deletedAt))}
                </small>
              </div>
              <button
                type="button"
                className="btn"
                disabled={busy === x.kind + ':' + x.entityId}
                onClick={() => void restore(x)}
                aria-label={t.trash.restoreLabel(x.title)}
              >
                {t.trash.restore}
              </button>
            </li>
          ))}
        </ul>
      )}
    </PageShell>
  );
}
