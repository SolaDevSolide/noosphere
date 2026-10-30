import { useEffect, useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import { api } from '../api/client';
import { isRestrictedPage, type PageLink, type PageSummary } from '../api/types';
import { useAuth } from '../auth/auth';
import { teamName } from '../explorer/bits';
import { useLandscape } from '../explorer/landscape';
import { useI18n } from '../i18n';
import { RecordError } from '../records/common';
import { IconBot, IconDoc, IconWarn } from '../ui/icons';
import { pageOwner, pagePaths, pathOf, relativeTime, resolveLink } from './model';
import { NewPageMenu, PagesLayout } from './PagesLayout';
import { TreeButton } from './PageTree';
import { usePages } from './store';

const WEEK = 7 * 86400 * 1000;

function AttachedBadge({ p, lead }: { p: PageSummary; lead: boolean }) {
  const { t } = useI18n();
  const { model } = useLandscape();
  const { byId } = usePages();
  if (!p.attachedTo) return null;
  const r = resolveLink(model, byId, p.attachedTo.kind, p.attachedTo.id);
  if (!r.exists || r.restricted) return null;
  const cls = { application: 'app', flow: 'flow', dataObject: 'data', team: 'team', domain: 'domain', page: 'page' }[
    p.attachedTo.kind
  ];
  return (
    <>
      {lead && ' · '}
      <span className={'kb ' + cls}>{t.pages.kindBadge[p.attachedTo.kind]}</span> {r.title}
    </>
  );
}

function Header() {
  const { t } = useI18n();
  const { canEdit } = useAuth();
  return (
    <div className="page-h">
      <div className="ph-t">
        <span className="mob-only" style={{ marginBottom: 10 }}>
          <TreeButton label={t.pages.allPages} />
        </span>
        <h1 style={{ marginTop: 0 }}>{t.pages.title}</h1>
        <p>{t.pages.intro}</p>
      </div>
      {canEdit && (
        <div className="page-actions">
          <NewPageMenu />
        </div>
      )}
    </div>
  );
}

function HomeSkeleton() {
  return (
    <div aria-busy="true">
      <div className="stats s3">
        {[0, 1, 2].map((i) => (
          <span key={i} className="sk block" style={{ height: 70 }} />
        ))}
      </div>
      <div className="home-grid">
        <span className="sk block" style={{ height: 360 }} />
        <span className="sk block" style={{ height: 240 }} />
      </div>
    </div>
  );
}

/** Broken links of the pages that have some, read from each page (only those are fetched). */
function useBroken(pages: PageSummary[]) {
  const [links, setLinks] = useState<Record<string, PageLink[]>>({});
  const ids = pages
    .filter((p) => p.brokenLinks > 0)
    .slice(0, 12)
    .map((p) => p.id + ':' + p.version)
    .join(',');
  useEffect(() => {
    let alive = true;
    const list = ids ? ids.split(',').map((x) => x.slice(0, x.lastIndexOf(':'))) : [];
    void Promise.all(
      list.map((id) =>
        api
          .page(id)
          .then((d) => [id, isRestrictedPage(d) ? [] : d.links.filter((l) => !l.exists)] as const)
          .catch(() => [id, []] as const),
      ),
    ).then((r) => alive && setLinks(Object.fromEntries(r)));
    return () => {
      alive = false;
    };
  }, [ids]);
  return links;
}

export function PagesHome() {
  const { t, lang } = useI18n();
  const { list, byId, error, reload } = usePages();
  const { model } = useLandscape();
  const pages = useMemo(() => (list ?? []).filter((p): p is PageSummary => !isRestrictedPage(p)), [list]);
  const broken = useBroken(pages);
  const [now] = useState(() => Date.now());
  const sorted = [...pages].sort((a, b) => b.updatedAt.localeCompare(a.updatedAt));
  const week = sorted.filter((p) => now - new Date(p.updatedAt).getTime() < WEEK);
  const recent = (week.length ? week : sorted).slice(0, 6);
  const brokenPages = pages.filter((p) => p.brokenLinks > 0).sort((a, b) => b.brokenLinks - a.brokenLinks);
  const brokenTotal = brokenPages.reduce((n, p) => n + p.brokenLinks, 0);
  const ownerOf = (p: PageSummary) => {
    const o = pageOwner(model, p);
    return o && model ? teamName(model, o) : t.pages.admins;
  };

  return (
    <PagesLayout title={t.pages.title}>
      <Header />
      {error && !list ? (
        <RecordError error={error} onRetry={() => void reload()} />
      ) : !list || !model ? (
        <HomeSkeleton />
      ) : (
        <>
          <div className="stats s3">
            <div className="stat">
              <b>{pages.length}</b>
              <span>{t.pages.stats.pages}</span>
            </div>
            <div className="stat">
              <b>{pages.filter((p) => p.attachedTo).length}</b>
              <span>{t.pages.stats.attached}</span>
            </div>
            <div className={'stat' + (brokenTotal ? ' bad' : '')}>
              <b>{brokenTotal}</b>
              <span>{t.pages.stats.broken}</span>
            </div>
          </div>
          <div className="home-grid">
            <div>
              <section className="box" aria-labelledby="recent-h">
                <div className="bx-h">
                  <h2 id="recent-h">{t.pages.recent}</h2>
                  {recent.length > 0 && (
                    <span className="muted" style={{ fontSize: 12.5 }}>
                      {week.length ? t.pages.last7 : t.pages.latest}
                    </span>
                  )}
                </div>
                {recent.length ? (
                  <div className="rows">
                    {recent.map((p) => {
                      const path = pathOf(byId, p.id);
                      return (
                        <Link key={p.id} className="row pr" to={pagePaths.page(p.id)}>
                          <span className="ri">
                            <IconDoc />
                          </span>
                          <span className="rn">
                            {p.title}
                            {(path || p.attachedTo) && (
                              <small>
                                {path}
                                <AttachedBadge p={p} lead={!!path} />
                              </small>
                            )}
                          </span>
                          <span className="rr">
                            <span>{relativeTime(p.updatedAt, lang, now)}</span>
                            <span>{ownerOf(p)}</span>
                          </span>
                        </Link>
                      );
                    })}
                  </div>
                ) : (
                  <p className="muted" style={{ margin: 0 }}>
                    {t.pages.empty} {t.pages.emptyHint}
                  </p>
                )}
              </section>
            </div>
            <div>
              <section className="box" aria-labelledby="broken-h">
                <div className="bx-h">
                  <h2 id="broken-h">{t.pages.brokenTitle(brokenTotal)}</h2>
                </div>
                {brokenPages.length ? (
                  brokenPages.map((p) => {
                    const ls = broken[p.id];
                    return (
                      <Link key={p.id} className="blrow" to={pagePaths.page(p.id)}>
                        <span className="ri">
                          <IconWarn size={16} />
                        </span>
                        <span>
                          <b>{p.title}</b>
                          <br />
                          {ls?.map((l) => (
                            <code key={l.kind + l.id} style={{ marginRight: 4 }}>
                              [[{l.kind}:{l.id}]]
                            </code>
                          ))}
                          <small>
                            {ls?.length === 1
                              ? t.pages.brokenMissing(t.pages.kindNames[ls[0].kind] ?? ls[0].kind)
                              : t.pages.nLinks(p.brokenLinks)}
                            {' · '}
                            {ownerOf(p)}
                          </small>
                        </span>
                      </Link>
                    );
                  })
                ) : (
                  <p className="muted" style={{ margin: 0, fontSize: 13 }}>
                    {t.pages.noBroken}
                  </p>
                )}
              </section>
              <section className="box" aria-labelledby="ai-h">
                <div className="bx-h">
                  <h2 id="ai-h">{t.pages.aiTitle}</h2>
                </div>
                <p className="ai-note" style={{ margin: '0 0 10px' }}>
                  <IconBot size={14} />
                  <span>{t.pages.aiText}</span>
                </p>
                <div className="codeblock" style={{ margin: 0 }}>
                  {'GET /api/pages/tree\nGET /api/pages/{id}/raw'}
                </div>
              </section>
            </div>
          </div>
        </>
      )}
    </PagesLayout>
  );
}
