import { useEffect, useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import { api, ApiError } from '../api/client';
import type { FieldDiff, HistoryEntry, RecordKind } from '../api/types';
import { useLandscape } from '../explorer/landscape';
import { paths } from '../explorer/paths';
import { useI18n } from '../i18n';
import { DiffView } from '../review/diff';
import { useInbox } from '../review/inbox';
import { IconBot, IconHistory, IconWarn, IconX } from '../ui/icons';
import { useModal } from '../ui/modal';
import { useToast } from '../ui/Toast';

type View =
  | { name: 'list' }
  | { name: 'compare'; from: number; to: number; diff: FieldDiff[] | null; error?: string }
  | { name: 'restore'; version: number };

/** The two picked versions, older first. */
export function comparePair(picked: number[]): [number, number] | null {
  if (picked.length !== 2) return null;
  const [a, b] = [...picked].sort((x, y) => x - y);
  return [a, b];
}

/** Picks a version for comparison; picking a third one drops the oldest pick. */
export function togglePick(picked: number[], v: number): number[] {
  if (picked.includes(v)) return picked.filter((x) => x !== v);
  return [...picked, v].slice(-2);
}

/**
 * Side panel listing the published versions of a record, opened only from the record's "History" button. Two picked
 * versions can be compared field by field; any older version can be restored after a confirmation.
 */
export function HistoryPanel({
  kind,
  id,
  name,
  canPublish,
  canPropose,
  notesOwner,
  onClose,
  onRestored,
}: {
  kind: RecordKind;
  id: string;
  name: string;
  canPublish: boolean;
  canPropose: boolean;
  /** The team that reads the protected notes, named under a masked change. */
  notesOwner?: string | null;
  onClose: () => void;
  onRestored: () => void;
}) {
  const { t, date } = useI18n();
  const { model: m } = useLandscape();
  const toast = useToast();
  const { refresh: refreshInbox } = useInbox();
  const [items, setItems] = useState<HistoryEntry[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [picked, setPicked] = useState<number[]>([]);
  const [view, setView] = useState<View>({ name: 'list' });
  const [busy, setBusy] = useState(false);
  const [proposed, setProposed] = useState<string | null>(null);
  const [tick, setTick] = useState(0);
  const panel = useRef<HTMLDivElement>(null);
  useModal(panel, onClose);

  useEffect(() => {
    let alive = true;
    setError(null);
    api
      .history(kind, id)
      .then((h) => alive && setItems(h))
      .catch(
        (e: unknown) =>
          alive && setError(e instanceof ApiError && e.status === 0 ? t.common.networkError : t.common.genericError),
      );
    return () => {
      alive = false;
    };
    // t is stable per language; reload on demand only
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [kind, id, tick]);

  const current = items?.[0]?.version;
  const pair = comparePair(picked);

  const compare = (from: number, to: number) => {
    setView({ name: 'compare', from, to, diff: null });
    api
      .compare(kind, id, from, to)
      .then((c) => setView((v) => (v.name === 'compare' && v.from === from ? { ...v, diff: c.diff } : v)))
      .catch((e: unknown) =>
        setView((v) =>
          v.name === 'compare'
            ? { ...v, diff: [], error: e instanceof ApiError ? e.detail || e.message : t.common.genericError }
            : v,
        ),
      );
  };

  const restore = async (version: number) => {
    setBusy(true);
    try {
      const r = await api.restore(kind, id, version);
      if (r.outcome === 'proposed' && r.proposalId) {
        setProposed(r.proposalId);
        toast(t.history.proposedToast);
        refreshInbox();
      } else {
        toast(t.history.restored(version));
        onRestored();
      }
      setView({ name: 'list' });
      setPicked([]);
      setTick((x) => x + 1);
    } catch (e) {
      toast(e instanceof ApiError ? e.detail || e.message : t.common.genericError);
    } finally {
      setBusy(false);
    }
  };

  const author = (h: HistoryEntry) => h.author?.displayName ?? t.history.system;

  return (
    <div className="drawer-back" role="presentation" onMouseDown={(e) => e.target === e.currentTarget && onClose()}>
      <div
        ref={panel}
        className="drawer"
        role="dialog"
        aria-modal="true"
        aria-labelledby="hist-h"
        aria-describedby="hist-sub"
        tabIndex={-1}
      >
        <div className="drawer-h">
          <IconHistory size={18} />
          <div className="drawer-t">
            <h2 id="hist-h">{t.history.title}</h2>
            <p id="hist-sub">{name}</p>
          </div>
          <button type="button" className="iconbtn" aria-label={t.history.close} onClick={onClose} data-autofocus>
            <IconX />
          </button>
        </div>

        <div className="drawer-b">
          {proposed && (
            <div className="banner ok" role="status">
              <span className="bt">
                <b>{t.history.proposedTitle}</b>
                {t.history.proposedText}{' '}
                <Link to={paths.proposal(proposed)} onClick={onClose}>
                  {t.propose.view}
                </Link>
              </span>
            </div>
          )}
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
            <div aria-busy="true" aria-label={t.common.loading}>
              {Array.from({ length: 4 }, (_, i) => (
                <div key={i} className="hrow-sk">
                  <span className="sk line" style={{ width: '55%' }} />
                  <span className="sk line" style={{ width: '80%', marginTop: 8 }} />
                </div>
              ))}
            </div>
          )}

          {items && view.name === 'compare' && (
            <div className="hcmp">
              <button type="button" className="btn ghost" onClick={() => setView({ name: 'list' })}>
                ← {t.history.back}
              </button>
              <h3 className="hcmp-h">{t.history.comparing(view.from, view.to)}</h3>
              {view.error && (
                <div className="banner danger" role="alert">
                  <span className="bt">{view.error}</span>
                </div>
              )}
              {view.diff === null ? (
                <div aria-busy="true">
                  <span className="sk block" style={{ height: 140 }} />
                </div>
              ) : (
                !view.error && (
                  <DiffView
                    diff={view.diff}
                    m={m}
                    leftTitle={t.history.versionN(view.from)}
                    rightTitle={t.history.versionN(view.to)}
                    notesOwner={notesOwner ?? undefined}
                  />
                )
              )}
            </div>
          )}

          {items && view.name !== 'compare' && (
            <>
              <p className="muted hhint" id="hist-hint">
                {t.history.pickTwo}
              </p>
              <ol className="hlist" aria-describedby="hist-hint">
                {items.map((h) => {
                  const isCur = h.version === current;
                  const on = picked.includes(h.version);
                  return (
                    <li key={h.version} className={on ? 'on' : undefined}>
                      <input
                        type="checkbox"
                        id={'hv-' + h.version}
                        checked={on}
                        onChange={() => setPicked((p) => togglePick(p, h.version))}
                        aria-label={t.history.pick(h.version)}
                      />
                      <label htmlFor={'hv-' + h.version} className="hmain">
                        <span className="htitle">
                          <b>v{h.version}</b>
                          {h.summary ? ` · ${h.summary}` : h.operation ? ` · ${t.history.operation[h.operation]}` : ''}
                        </span>
                        <small>
                          {h.authorKind === 'agent' && (
                            <span className="pchip agent">
                              <IconBot size={12} />
                              {t.review.agent}
                            </span>
                          )}
                          {author(h)} · {date(h.createdAt)}
                        </small>
                      </label>
                      <span className="hside">
                        {isCur ? (
                          <span className="cur">{t.history.current}</span>
                        ) : (
                          (canPublish || canPropose) && (
                            <button
                              type="button"
                              className="btn sm"
                              onClick={() => setView({ name: 'restore', version: h.version })}
                              aria-label={t.history.restoreLabel(h.version)}
                            >
                              {t.history.restore}
                            </button>
                          )
                        )}
                        {h.proposalId && (
                          <Link className="hprop" to={paths.proposal(h.proposalId)} onClick={onClose}>
                            {t.history.fromProposal}
                          </Link>
                        )}
                      </span>
                      {view.name === 'restore' && view.version === h.version && current != null && (
                        <div className="hconfirm" role="alertdialog" aria-labelledby={'hc-' + h.version}>
                          <p id={'hc-' + h.version}>
                            {canPublish
                              ? t.history.restoreConfirm(h.version, current + 1)
                              : t.history.restoreProposeConfirm(h.version)}
                          </p>
                          <div className="page-actions">
                            <button type="button" className="btn" onClick={() => setView({ name: 'list' })}>
                              {t.common.cancel}
                            </button>
                            <button
                              type="button"
                              className="btn primary"
                              disabled={busy}
                              autoFocus
                              onClick={() => void restore(h.version)}
                            >
                              {busy ? t.common.saving : canPublish ? t.history.restoreDo : t.history.proposeDo}
                            </button>
                          </div>
                        </div>
                      )}
                    </li>
                  );
                })}
              </ol>
            </>
          )}
        </div>

        {items && view.name !== 'compare' && (
          <div className="drawer-f">
            <button
              type="button"
              className="btn primary"
              disabled={!pair}
              onClick={() => pair && compare(pair[0], pair[1])}
            >
              {pair ? t.history.compareTwo(pair[0], pair[1]) : t.history.compare}
            </button>
          </div>
        )}
      </div>
    </div>
  );
}
