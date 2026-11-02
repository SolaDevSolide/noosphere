import { useEffect, useRef, useState } from 'react';
import { api } from '../api/client';
import type { DirectoryPreview } from '../api/types';
import { useI18n } from '../i18n';
import { IconCheck, IconLock, IconRotate, IconWarn } from '../ui/icons';
import { useModal } from '../ui/modal';

/** The directory preview, loaded once. */
export function useDirectoryPreview(enabled = true) {
  const [preview, setPreview] = useState<DirectoryPreview | null>(null);
  const [error, setError] = useState(false);
  useEffect(() => {
    if (!enabled) return;
    let alive = true;
    api
      .directoryPreview()
      .then((p) => alive && setPreview(p))
      .catch(() => alive && setError(true));
    return () => {
      alive = false;
    };
  }, [enabled]);
  return { preview, error };
}

/**
 * Before the directory is shared: what goes, what never does, the e-mail choice (unticked) and three people as the
 * application will read them. `name` is null at pairing, when the application is not known yet and e-mails are
 * decided later, on the link.
 */
export function DirectoryDialog({
  name,
  shareEmails: initial = false,
  busy,
  onCancel,
  onConfirm,
}: {
  name: string | null;
  shareEmails?: boolean;
  busy?: boolean;
  onCancel: () => void;
  onConfirm: (shareEmails: boolean) => void;
}) {
  const { t } = useI18n();
  const d = t.links.dir;
  const box = useRef<HTMLDivElement>(null);
  useModal(box, onCancel);
  const { preview, error } = useDirectoryPreview();
  const [emails, setEmails] = useState(initial);
  return (
    <div className="dlg-back" role="presentation" onMouseDown={(e) => e.target === e.currentTarget && onCancel()}>
      <div ref={box} className="dlg wide" role="dialog" aria-modal="true" aria-labelledby="dd-h" tabIndex={-1}>
        <h2 id="dd-h">{name ? d.title(name) : d.titlePair}</h2>
        <p className="dsub">{name ? d.sub(name) : d.subPair}</p>
        {error ? (
          <div className="banner danger" role="alert">
            <IconWarn />
            <span className="bt">{d.previewError}</span>
          </div>
        ) : !preview ? (
          <div aria-busy="true" className="dd-sk">
            <span className="sk block" style={{ height: 110 }} />
            <span className="sk block" style={{ height: 90, marginTop: 12 }} />
          </div>
        ) : (
          <>
            <div className="dd-cols">
              <div>
                <span className="lbl2">{d.shared}</span>
                <ul className="can">
                  <li className="ok">
                    <IconCheck />
                    <span>{d.teams(preview.teams)}</span>
                  </li>
                  <li className="ok">
                    <IconCheck />
                    <span>{d.people(preview.people)}</span>
                  </li>
                  <li className="ok">
                    <IconRotate />
                    <span>
                      {d.notices}
                      <small>
                        <code>kind: team</code>, <code>kind: person</code>
                      </small>
                    </span>
                  </li>
                </ul>
              </div>
              <div>
                <span className="lbl2">{d.never}</span>
                <ul className="can">
                  <li className="lock">
                    <IconLock size={16} />
                    <span>{d.roles}</span>
                  </li>
                  <li className="lock">
                    <IconLock size={16} />
                    <span>{d.guests(preview.guests)}</span>
                  </li>
                  <li className="lock">
                    <IconLock size={16} />
                    <span>{d.noTeam(preview.withoutTeam)}</span>
                  </li>
                </ul>
              </div>
            </div>
            {name ? (
              <div className="rcards dd-emails">
                <label className="rcard">
                  <input type="checkbox" checked={emails} onChange={(e) => setEmails(e.target.checked)} />
                  <b>{d.emails}</b>
                  <small>{d.emailsText}</small>
                </label>
              </div>
            ) : (
              <p className="muted2 tl-p">{d.emailsPair}</p>
            )}
            <span className="lbl2 tl-l" id="dd-pv">
              {name ? d.preview(name) : d.previewPair}
            </span>
            <table className="dd-peek" aria-labelledby="dd-pv">
              <thead>
                <tr>
                  <th scope="col">{d.person}</th>
                  <th scope="col">{d.personTeams}</th>
                </tr>
              </thead>
              <tbody>
                {preview.sample.map((p) => (
                  <tr key={p.username}>
                    <td>
                      {p.displayName} <span className="mono muted2">{p.username}</span>
                    </td>
                    <td>{p.teams.join(', ')}</td>
                  </tr>
                ))}
                <tr className="no">
                  <td>{emails && name ? d.emailYes(preview.contactEmails) : d.emailNo}</td>
                  <td>{d.roleNever}</td>
                </tr>
              </tbody>
            </table>
          </>
        )}
        <div className="form-actions tl-actions">
          <button type="button" className="btn" onClick={onCancel}>
            {d.cancel}
          </button>
          <button
            type="button"
            className="btn primary"
            data-autofocus
            disabled={!preview || busy}
            onClick={() => onConfirm(emails)}
          >
            {busy ? t.common.saving : d.confirm}
          </button>
        </div>
      </div>
    </div>
  );
}
