import { useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { api } from '../api/client';
import { isRestrictedApp, type DomainDetail } from '../api/types';
import { useAuth } from '../auth/auth';
import { teamName } from '../explorer/bits';
import { useLandscape } from '../explorer/landscape';
import { paths } from '../explorer/paths';
import { useI18n } from '../i18n';
import { PageShell, RecordError, RecordSkeleton, useRecord } from '../records/common';
import { HistoryPanel } from '../records/HistoryPanel';
import { RecordExtensions } from '../records/RecordExtensions';
import { IconHistory } from '../ui/icons';
import { Markdown } from '../ui/Markdown';

/** A business domain: its description, its applications and the pages about it. */
export function DomainRecord() {
  const { id = '' } = useParams();
  const { t, date } = useI18n();
  const { isAdmin } = useAuth();
  const { model: m } = useLandscape();
  const { data, error, reload } = useRecord<DomainDetail>(api.domain, id);
  const [historyOpen, setHistoryOpen] = useState(false);
  if (error)
    return (
      <PageShell>
        <RecordError error={error} onRetry={reload} />
      </PageShell>
    );
  if (!data || data.id !== id || !m)
    return (
      <PageShell>
        <RecordSkeleton />
      </PageShell>
    );
  const name = data.name ?? id;
  const apps = m.appIds.filter((a) => m.apps.get(a)?.domainId === id);
  return (
    <PageShell title={name}>
      <div className="page-h">
        <div className="ph-t">
          <span className="kb dom">{t.domainRecord.badge}</span> <span className="muted">{t.domainRecord.label}</span>
          <h1>{name}</h1>
          <p>{t.domainRecord.apps(apps.length)}</p>
        </div>
        <div className="page-actions">
          <Link className="btn" to={paths.explorer()}>
            {t.domainRecord.showOnMap}
          </Link>
          <button
            type="button"
            className="btn hist-btn"
            aria-pressed={historyOpen}
            aria-haspopup="dialog"
            onClick={() => setHistoryOpen(true)}
          >
            <IconHistory />
            {t.history.button}
          </button>
        </div>
      </div>
      {historyOpen && (
        <HistoryPanel
          kind="domain"
          id={id}
          name={name}
          canPublish={data.permissions?.publish ?? isAdmin}
          canPropose={false}
          onClose={() => setHistoryOpen(false)}
          onRestored={reload}
        />
      )}
      <div className="grid2">
        <div>
          <section className="box" aria-labelledby="ddesc-h">
            <h2 id="ddesc-h">{t.record.fields.description}</h2>
            {data.description ? (
              <Markdown text={data.description} />
            ) : (
              <p className="muted">{t.record.noDescription}</p>
            )}
          </section>
          <RecordExtensions kind="domain" record={{ ...data, id }} />
          <section className="box" aria-labelledby="dapps-h">
            <h2 id="dapps-h">{t.domainRecord.apps(apps.length)}</h2>
            {apps.length ? (
              <ul className="tlinks">
                {apps.map((a) => {
                  const app = m.apps.get(a)!;
                  if (isRestrictedApp(app))
                    return (
                      <li key={a}>
                        <span className="muted">{t.common.restrictedApp}</span>
                      </li>
                    );
                  return (
                    <li key={a}>
                      <Link to={paths.app(a)}>{app.name}</Link>
                      {app.teamId && <span className="muted"> · {teamName(m, app.teamId)}</span>}
                    </li>
                  );
                })}
              </ul>
            ) : (
              <p className="muted">{t.domainRecord.noApps}</p>
            )}
          </section>
        </div>
        <section className="box" aria-labelledby="dfields-h">
          <h2 id="dfields-h">{t.panel.details}</h2>
          <dl className="fields">
            <dt>{t.record.fields.id}</dt>
            <dd>
              <code>{id}</code>
            </dd>
            <dt>{t.domainRecord.order}</dt>
            <dd>{data.order ?? '—'}</dd>
            <dt>{t.record.fields.updatedAt}</dt>
            <dd>
              {date(data.updatedAt)} · {t.record.fields.version} {data.version}
            </dd>
          </dl>
        </section>
      </div>
    </PageShell>
  );
}
