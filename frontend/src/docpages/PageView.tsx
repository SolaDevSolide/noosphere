import { useEffect, useRef, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { api, ApiError } from '../api/client';
import { isRestrictedPage, type PageDetail, type RecordKind, type RestrictedPage } from '../api/types';
import { teamName } from '../explorer/bits';
import { useLandscape } from '../explorer/landscape';
import { useI18n } from '../i18n';
import { HistoryPanel } from '../records/HistoryPanel';
import { useRecord } from '../records/common';
import {
  IconChevron,
  IconCode,
  IconCopy,
  IconDoc,
  IconEdit,
  IconHistory,
  IconLock,
  IconWarn,
  TypeIcon,
} from '../ui/icons';
import { useModal } from '../ui/modal';
import { useToast } from '../ui/Toast';
import { DocMarkdown, useParsedDoc, type TocEntry } from './DocMarkdown';
import { pageKind, pageOwner, pagePaths, pathOf, recordHref } from './model';
import { PagesLayout } from './PagesLayout';
import { TreeButton } from './PageTree';
import { useResolver } from './resolve';
import { usePages } from './store';

export function copyText(text: string): Promise<void> {
  if (navigator.clipboard?.writeText) return navigator.clipboard.writeText(text);
  return Promise.reject(new Error('clipboard'));
}

function RawDialog({ id, text, onClose }: { id: string; text: string | null; onClose: () => void }) {
  const { t } = useI18n();
  const toast = useToast();
  const ref = useRef<HTMLDivElement>(null);
  useModal(ref, onClose);
  return (
    <div className="dlg-back" role="presentation" onMouseDown={(e) => e.target === e.currentTarget && onClose()}>
      <div className="dlg wide" role="dialog" aria-modal="true" aria-labelledby="raw-h" ref={ref} tabIndex={-1}>
        <h2 id="raw-h" style={{ marginBottom: 4 }}>
          {t.pages.rawH}
        </h2>
        <p style={{ margin: '0 0 12px', color: 'var(--fg-muted)', fontSize: 13 }}>
          <code style={{ font: '12.5px var(--font-mono)', overflowWrap: 'anywhere' }}>GET /api/pages/{id}/raw</code> ·{' '}
          {t.pages.rawText}
        </p>
        {text === null ? (
          <span className="sk block" style={{ height: 240 }} aria-busy="true" />
        ) : (
          <div className="codeblock" tabIndex={0}>
            {text}
          </div>
        )}
        <div className="form-actions" style={{ marginTop: 14 }}>
          <button
            type="button"
            className="btn"
            disabled={text === null}
            onClick={() =>
              text !== null &&
              void copyText(text).then(
                () => toast(t.pages.copied),
                () => toast(t.pages.copyFailed),
              )
            }
          >
            <IconCopy />
            {t.pages.copy}
          </button>
          <button type="button" className="btn primary" onClick={onClose} data-autofocus>
            {t.common.close}
          </button>
        </div>
      </div>
    </div>
  );
}

/** The table of contents, with the section in view marked. */
function Toc({ toc, active, onJump }: { toc: TocEntry[]; active: string | null; onJump: (id: string) => void }) {
  return (
    <ul className="toc">
      {toc.map((h) => (
        <li key={h.id}>
          <a
            href={'#' + h.id}
            className={active === h.id ? 'on' : undefined}
            aria-current={active === h.id ? 'location' : undefined}
            onClick={(e) => {
              e.preventDefault();
              onJump(h.id);
            }}
          >
            {h.text}
          </a>
        </li>
      ))}
    </ul>
  );
}

function useActiveHeading(toc: TocEntry[]) {
  const [active, setActive] = useState<string | null>(toc[0]?.id ?? null);
  const ids = toc.map((h) => h.id).join(' ');
  useEffect(() => {
    const scroller = document.getElementById('main');
    if (!scroller || !ids) return;
    const list = ids.split(' ');
    const onScroll = () => {
      let cur = list[0];
      for (const id of list) {
        const el = document.getElementById(id);
        if (el && el.getBoundingClientRect().top < 120) cur = id;
      }
      setActive(cur);
    };
    onScroll();
    scroller.addEventListener('scroll', onScroll, { passive: true });
    return () => scroller.removeEventListener('scroll', onScroll);
  }, [ids]);
  return [active, setActive] as const;
}

function PageSkeleton() {
  return (
    <div aria-busy="true">
      <span className="sk line" style={{ width: 200 }} />
      <span className="sk title" style={{ width: '60%', marginTop: 12 }} />
      <span className="sk line" style={{ width: '40%', marginTop: 10 }} />
      <div className="pv-grid" style={{ marginTop: 20 }}>
        <span className="sk block" style={{ height: 420 }} />
        <span className="sk block" style={{ height: 200 }} />
      </div>
    </div>
  );
}

function Article({ page, onRaw, onCopy }: { page: PageDetail; onRaw: () => void; onCopy: () => void }) {
  const { t, date } = useI18n();
  const { model } = useLandscape();
  const { byId } = usePages();
  const doc = useParsedDoc(page.body);
  const resolve = useResolver(page.links);
  const [active, setActive] = useActiveHeading(doc.toc);
  const [author, setAuthor] = useState<string | null>(null);
  useEffect(() => {
    let alive = true;
    api
      .history('page', page.id)
      .then((h) => alive && setAuthor(h[0]?.author?.displayName ?? null))
      .catch(() => {});
    return () => {
      alive = false;
    };
  }, [page.id, page.version]);
  const jump = (id: string) => {
    document.getElementById(id)?.scrollIntoView({ behavior: 'smooth', block: 'start' });
    setActive(id);
  };
  const broken = page.links.filter((l) => !l.exists).length;
  const teamOf = (id: string) => {
    const p = byId.get(id);
    if (!p || isRestrictedPage(p)) return '';
    const o = pageOwner(model, p);
    return o && model ? teamName(model, o) : t.pages.admins;
  };
  return (
    <div className="pv-grid">
      <div>
        {doc.toc.length > 0 && (
          <details className="mob-toc">
            <summary>
              {t.pages.onThisPage}
              <IconChevron size={16} />
            </summary>
            <Toc toc={doc.toc} active={active} onJump={jump} />
          </details>
        )}
        <article className="box article" aria-labelledby="page-h">
          <DocMarkdown doc={doc} resolve={resolve} />
          <div className="foot2">
            <span>
              {t.pages.updated(date(page.updatedAt))}
              {author && (
                <>
                  {' '}
                  {t.pages.by} {author}
                </>
              )}{' '}
              · {t.pages.version(page.version)}
            </span>
            <span>{page.visibility === 'restricted' ? t.pages.visibleRestricted : t.pages.visibleEveryone}</span>
            <span className="mob-only" style={{ gap: 10 }}>
              <button type="button" className="btn sm" onClick={onCopy}>
                <IconCopy />
                {t.pages.copyMd}
              </button>
              <button type="button" className="btn sm" onClick={onRaw}>
                <IconCode />
                {t.pages.raw}
              </button>
            </span>
          </div>
        </article>
      </div>
      <aside className="rail" aria-label={t.pages.onThisPage}>
        {doc.toc.length > 0 && (
          <section className="box toc-box" aria-labelledby="toc-h">
            <h2 id="toc-h">{t.pages.onThisPage}</h2>
            <Toc toc={doc.toc} active={active} onJump={jump} />
          </section>
        )}
        <section className="box" aria-labelledby="bl-h">
          <h2 id="bl-h">{t.pages.mentionedIn(page.backlinks.length)}</h2>
          {page.backlinks.length ? (
            <div className="bl">
              {page.backlinks.map((b) => (
                <Link key={b.id} to={pagePaths.page(b.id)}>
                  <IconDoc />
                  <span>
                    {b.title}
                    <small>{[pathOf(byId, b.id), teamOf(b.id)].filter(Boolean).join(' · ')}</small>
                  </span>
                </Link>
              ))}
            </div>
          ) : (
            <p className="muted" style={{ margin: 0, fontSize: 13 }}>
              {t.pages.noMentions}
            </p>
          )}
        </section>
        <section className="box" aria-labelledby="li-h">
          <h2 id="li-h">{t.pages.linksIn(page.links.length)}</h2>
          <p style={{ margin: 0, fontSize: 13, color: 'var(--fg-muted)' }}>
            {page.links.length ? (
              <>
                {t.pages.linksOk(page.links.length - broken)}
                {broken > 0 && (
                  <>
                    , <span style={{ color: 'var(--danger-fg)', fontWeight: 600 }}>{t.pages.linksBroken(broken)}</span>
                  </>
                )}
                . {t.pages.linksNote}
              </>
            ) : (
              t.pages.noLinks
            )}
          </p>
        </section>
      </aside>
    </div>
  );
}

export function PageView() {
  const { id = '' } = useParams();
  const { t } = useI18n();
  const { model, reload: reloadLandscape } = useLandscape();
  const { reload: reloadPages } = usePages();
  const toast = useToast();
  const { data, error, reload } = useRecord<PageDetail | RestrictedPage>(api.page, id);
  const [historyOpen, setHistoryOpen] = useState(false);
  const [raw, setRaw] = useState<{ open: boolean; text: string | null }>({ open: false, text: null });
  const page = data && !isRestrictedPage(data) && data.id === id ? data : null;

  const fetchRaw = () => api.pageRaw(id);
  const copy = () => {
    void fetchRaw()
      .then(copyText)
      .then(
        () => toast(t.pages.copied),
        () => toast(t.pages.copyFailed),
      );
  };
  const openRaw = () => {
    setRaw({ open: true, text: null });
    fetchRaw().then(
      (text) => setRaw({ open: true, text }),
      (e: unknown) => {
        setRaw({ open: false, text: null });
        toast(e instanceof ApiError ? e.detail || e.message : t.common.genericError);
      },
    );
  };

  let body;
  if (error)
    body = (
      <div className="banner danger" role="alert">
        <IconWarn />
        <div className="bt">
          <b>{error.status === 404 ? t.pages.notFound : error.status === 0 ? t.common.networkError : error.message}</b>
          <div className="page-actions" style={{ marginTop: 10 }}>
            {error.status !== 404 && (
              <button type="button" className="btn" onClick={reload}>
                {t.common.retry}
              </button>
            )}
            <Link className="btn" to={pagePaths.home}>
              {t.pages.backToPages}
            </Link>
          </div>
        </div>
      </div>
    );
  else if (data && isRestrictedPage(data))
    body = (
      <div className="banner" role="note">
        <IconLock />
        <span className="bt">
          <b>{t.pages.restrictedPage}</b> · {t.pages.restrictedText}
        </span>
      </div>
    );
  else if (!page || !model) body = <PageSkeleton />;
  else {
    const kind = pageKind(page.tags);
    const ownerName = page.owningTeamId ? teamName(model, page.owningTeamId) : t.pages.admins;
    const att = page.attachedTo;
    body = (
      <>
        <div className="pv-head">
          <nav className="crumbs2" aria-label={t.pages.breadcrumb}>
            <TreeButton />
            <Link to={pagePaths.home}>{t.pages.title}</Link>
            {page.ancestors.map((a) => (
              <span key={a.id} style={{ display: 'contents' }}>
                <span className="sep" aria-hidden="true">
                  /
                </span>
                <Link to={pagePaths.page(a.id)}>{a.title || t.pages.restrictedPage}</Link>
              </span>
            ))}
          </nav>
          <div className="page-h">
            <div className="ph-t">
              <span className="kb page">{t.pages.badge}</span>
              {kind && <span className="muted"> {t.pages.pageKind[kind]}</span>}
              <h1 id="page-h">{page.title}</h1>
            </div>
            <div className="pv-actions">
              <button
                type="button"
                className="btn ghost sm"
                aria-haspopup="dialog"
                aria-pressed={historyOpen}
                onClick={() => setHistoryOpen(true)}
              >
                <IconHistory />
                {t.history.button}
              </button>
              <span className="sepv" aria-hidden="true" />
              <button type="button" className="btn sm desk" title={t.pages.copyMdTitle} onClick={copy}>
                <IconCopy />
                {t.pages.copyMd}
              </button>
              <button type="button" className="btn sm desk" title={t.pages.rawTitle} onClick={openRaw}>
                <IconCode />
                {t.pages.raw}
              </button>
              {(page.permissions.publish || page.permissions.propose) && (
                <Link className="btn primary" to={pagePaths.edit(page.id)}>
                  <IconEdit />
                  {page.permissions.publish ? t.record.edit : t.propose.suggest}
                </Link>
              )}
            </div>
          </div>
          <div className="metabar">
            <span className="lab">{t.pages.owner}</span>
            {page.owningTeamId ? (
              <Link className="tchip" to={recordHref('team', page.owningTeamId)}>
                {ownerName}
              </Link>
            ) : (
              <span className="tchip">{ownerName}</span>
            )}
            {att && page.attachedTitle && (
              <>
                <span className="lab">{t.pages.attachedTo}</span>
                <AttachedChip kind={att.kind} id={att.id} />
              </>
            )}
            {page.tags.length > 0 && (
              <>
                <span className="lab">{t.pages.tags}</span>
                {page.tags.map((tag) => (
                  <span key={tag} className="tagc">
                    {tag}
                  </span>
                ))}
              </>
            )}
          </div>
        </div>
        <Article page={page} onRaw={openRaw} onCopy={copy} />
        {historyOpen && (
          <HistoryPanel
            kind="page"
            id={page.id}
            name={page.title}
            canPublish={page.permissions.publish}
            canPropose={page.permissions.propose}
            onClose={() => setHistoryOpen(false)}
            onRestored={() => {
              reload();
              void reloadPages();
              reloadLandscape();
            }}
          />
        )}
        {raw.open && <RawDialog id={page.id} text={raw.text} onClose={() => setRaw({ open: false, text: null })} />}
      </>
    );
  }
  return (
    <PagesLayout currentId={id} title={page?.title}>
      {body}
    </PagesLayout>
  );
}

/** The record a page is attached to, as a chip with its kind. */
export function AttachedChip({ kind, id }: { kind: RecordKind; id: string }) {
  const { t } = useI18n();
  const resolve = useResolver();
  const r = resolve(kind, id);
  const cls = { application: 'app', flow: 'flow', dataObject: 'data', team: 'team', domain: 'dom', page: 'page' }[kind];
  const inner = (
    <>
      <span className={'kb ' + cls} style={{ lineHeight: '16px' }}>
        {t.pages.kindBadge[kind]}
      </span>
      {r.flowType && (
        <span className="oi" style={{ display: 'inline-flex', color: 'var(--tc)' }}>
          <TypeIcon type={r.flowType} size={14} />
        </span>
      )}
      {r.exists ? r.title : id}
    </>
  );
  return r.href && r.exists && !r.restricted ? (
    <Link className={'och' + (r.flowType ? ' t-' + r.flowType : '')} to={r.href}>
      {inner}
    </Link>
  ) : (
    <span className="och">{inner}</span>
  );
}
