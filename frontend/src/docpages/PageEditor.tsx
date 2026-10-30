import {
  useCallback,
  useDeferredValue,
  useEffect,
  useLayoutEffect,
  useMemo,
  useRef,
  useState,
  type FormEvent,
  type KeyboardEvent,
} from 'react';
import { Link, useLocation, useParams, useSearchParams } from 'react-router-dom';
import { api, ApiError } from '../api/client';
import {
  isRestrictedPage,
  type AttachedTo,
  type PageDetail,
  type PageInput,
  type RecordKind,
  type RestrictedPage,
} from '../api/types';
import { useDocumentTitle } from '../app/settings';
import { useAuth } from '../auth/auth';
import { teamName } from '../explorer/bits';
import { useLandscape } from '../explorer/landscape';
import { useI18n } from '../i18n';
import type { Dict } from '../i18n/en';
import { ConflictBanner, serverErrors, useRecord, useUnsavedGuard } from '../records/common';
import { useInbox } from '../review/inbox';
import {
  IconCallout,
  IconCode,
  IconEdit,
  IconEye,
  IconInfo,
  IconLink,
  IconList,
  IconLock,
  IconOList,
  IconSend,
  IconTable,
  IconTrash,
  IconWarn,
} from '../ui/icons';
import { useModal } from '../ui/modal';
import { useToast } from '../ui/Toast';
import { useGo } from '../ui/useGo';
import { DocMarkdown, parseDoc } from './DocMarkdown';
import { LinkOptions, useLinkSearch, type LinkOption } from './LinkSearch';
import {
  ATTACH_KINDS,
  canPublishPage,
  LINK_KINDS,
  pageOwner,
  pagePaths,
  slugify,
  TEMPLATE_TAG,
  wikiLinks,
  type TemplateId,
} from './model';
import { PageSettings, validateDraft, type PageDraft } from './PageSettings';
import { PagesLayout } from './PagesLayout';
import { useResolver } from './resolve';
import { usePages } from './store';

/* ---------- local drafts: kept 7 days on this device ---------- */

const DRAFT_DAYS = 7;
const draftKey = (id: string | null) => 'noos-page-draft:' + (id ?? 'new');
interface StoredDraft {
  draft: PageDraft;
  savedAt: number;
  baseVersion: number | null;
}
export function readDraft(id: string | null): StoredDraft | null {
  try {
    const v = JSON.parse(localStorage.getItem(draftKey(id)) ?? 'null') as StoredDraft | null;
    if (!v || typeof v !== 'object' || !v.draft || Date.now() - v.savedAt > DRAFT_DAYS * 86400_000) return null;
    return v;
  } catch {
    return null;
  }
}
function writeDraft(id: string | null, v: StoredDraft | null) {
  try {
    if (v) localStorage.setItem(draftKey(id), JSON.stringify(v));
    else localStorage.removeItem(draftKey(id));
  } catch {
    /* storage blocked or full */
  }
}

/* ---------- Markdown snippets of the toolbar ---------- */

export type Snippet = 'h2' | 'h3' | 'b' | 'i' | 'ul' | 'ol' | 'table' | 'code' | 'callout' | 'link';

/** What a toolbar button inserts around the selection: [before, default text, after]. */
export function snippet(t: Dict, kind: Snippet): [string, string, string] {
  const s = t.pages.snippets;
  switch (kind) {
    case 'h2':
      return ['\n## ', s.heading, '\n'];
    case 'h3':
      return ['\n### ', s.subheading, '\n'];
    case 'b':
      return ['**', s.bold, '**'];
    case 'i':
      return ['*', s.italic, '*'];
    case 'ul':
      return ['\n- ', s.item, `\n- ${s.item}\n`];
    case 'ol':
      return ['\n1. ', s.first, `\n2. ${s.second}\n`];
    case 'table':
      return [`\n| ${s.column} | ${s.column} |\n|---|---|\n| `, s.cell, ` | ${s.cell} |\n`];
    case 'code':
      return ['\n```bash\n', s.command, '\n```\n'];
    case 'callout':
      return ['\n> [!NOTE] ', s.calloutTitle, `\n> ${s.calloutText}\n`];
    default:
      return ['[[', '', ''];
  }
}

/** The `[[query` typed just before the caret, if any. */
export function linkQuery(text: string, caret: number): { start: number; query: string } | null {
  const m = /\[\[([^\]\n|[]{0,40})$/.exec(text.slice(0, caret));
  return m ? { start: caret - m[0].length, query: m[1] } : null;
}

/** "flow:cust" searches flows for "cust"; "cust" searches everything. */
export function splitKind(query: string): { kinds?: RecordKind[]; text: string } {
  const m = /^([a-zA-Z]+):(.*)$/.exec(query);
  if (m && (LINK_KINDS as string[]).includes(m[1])) return { kinds: [m[1] as RecordKind], text: m[2] };
  return { text: query };
}

/* ---------- the editor ---------- */

function toDraft(p: PageDetail): PageDraft {
  return {
    title: p.title,
    slug: p.id,
    slugTouched: true,
    body: p.body,
    parentId: p.parentId ?? '',
    attachedTo: p.attachedTo ?? null,
    ownerTeamId: p.ownerTeamId ?? '',
    tags: [...p.tags],
    visibility: p.visibility,
  };
}

function caretPoint(ta: HTMLTextAreaElement, pos: number) {
  const m = document.createElement('div');
  const cs = getComputedStyle(ta);
  m.className = 'mirror';
  for (const p of [
    'fontFamily',
    'fontSize',
    'lineHeight',
    'paddingTop',
    'paddingLeft',
    'paddingRight',
    'letterSpacing',
  ] as const)
    m.style[p] = cs[p];
  m.style.width = ta.clientWidth + 'px';
  m.textContent = ta.value.slice(0, pos);
  const s = document.createElement('span');
  s.textContent = '​';
  m.appendChild(s);
  ta.parentElement?.appendChild(m);
  const xy = { x: s.offsetLeft, y: s.offsetTop + s.offsetHeight - ta.scrollTop };
  m.remove();
  return xy;
}

function LeaveDialog({ title, onDiscard, onKeep }: { title: string; onDiscard: () => void; onKeep: () => void }) {
  const { t } = useI18n();
  const ref = useRef<HTMLDivElement>(null);
  useModal(ref, onKeep);
  return (
    <div className="dlg-back" role="presentation" onMouseDown={(e) => e.target === e.currentTarget && onKeep()}>
      <div
        className="dlg"
        role="alertdialog"
        aria-modal="true"
        aria-labelledby="lv-h"
        aria-describedby="lv-p"
        ref={ref}
      >
        <h2 id="lv-h">{t.pages.leaveTitle}</h2>
        <p className="dlg-p" id="lv-p">
          {t.pages.leaveText(title)}
        </p>
        <p className="dlg-s">{t.pages.leaveDraft}</p>
        <div className="form-actions">
          <button type="button" className="btn danger" onClick={onDiscard}>
            {t.pages.discard}
          </button>
          <button type="button" className="btn primary" onClick={onKeep} data-autofocus>
            {t.pages.keep}
          </button>
        </div>
      </div>
    </div>
  );
}

function Reviewers({ owner }: { owner: string | null }) {
  const { t } = useI18n();
  const { model } = useLandscape();
  const [names, setNames] = useState<string[] | null>(null);
  useEffect(() => {
    let alive = true;
    setNames(null);
    if (!owner) return;
    api.teamMembers(owner).then(
      (m) => alive && setNames(m.map((x) => x.displayName)),
      () => alive && setNames([]),
    );
    return () => {
      alive = false;
    };
  }, [owner]);
  const team = owner && model ? teamName(model, owner) : t.pages.admins;
  const initials = team
    .replace(/^Team\s+|^Équipe\s+/i, '')
    .split(/\s+/)
    .map((w) => w.charAt(0))
    .join('')
    .slice(0, 2)
    .toUpperCase();
  const who =
    names && names.length
      ? names.length > 2
        ? t.pages.andOthers(names.slice(0, 2).join(', '), names.length - 2)
        : names.join(', ')
      : null;
  return (
    <>
      <span className="av" aria-hidden="true">
        {initials || 'A'}
      </span>
      <div>
        <b>{t.pages.willReview(team)}</b>
        <p>{who ? t.pages.reviewers(who, team) : owner ? t.pages.reviewersNone(team) : t.pages.adminsHint}</p>
      </div>
    </>
  );
}

function EditorBody({
  page,
  initial,
  fresh,
  restored,
}: {
  page: PageDetail | null;
  /** The draft the editor starts from (a restored local draft, or the page). */
  initial: PageDraft;
  /** For a new page: the draft before any change (template applied). */
  fresh?: PageDraft;
  restored: boolean;
}) {
  const { t } = useI18n();
  const { me, teamIds, isAdmin } = useAuth();
  const { model } = useLandscape();
  const { list, byId, reload: reloadPages } = usePages();
  const { refresh: refreshInbox } = useInbox();
  const toast = useToast();
  const go = useGo();
  const isNew = !page;
  const pageId = page?.id ?? null;
  const base = useMemo(() => (page ? toDraft(page) : (fresh ?? initial)), [page, fresh, initial]);
  const [d, setD] = useState<PageDraft>(initial);
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [busy, setBusy] = useState(false);
  const [conflict, setConflict] = useState(false);
  const [failure, setFailure] = useState<string | null>(null);
  const [showRestored, setShowRestored] = useState(restored);
  const [proposeInstead, setProposeInstead] = useState(false);
  const [comment, setComment] = useState('');
  const [tab, setTab] = useState<'src' | 'prev'>('src');
  const [settingsOpen, setSettingsOpen] = useState(
    () => typeof window === 'undefined' || !window.matchMedia?.('(max-width: 720px)').matches,
  );
  const [leave, setLeave] = useState<string | null>(null);
  const done = useRef(false);
  const dirty = JSON.stringify({ ...d, slugTouched: false }) !== JSON.stringify({ ...base, slugTouched: false });
  useUnsavedGuard(dirty && !done.current);

  const set = useCallback(<K extends keyof PageDraft>(k: K, v: PageDraft[K]) => {
    setD((x) => {
      const n = { ...x, [k]: v };
      if (k === 'title' && !x.slugTouched) n.slug = slugify(typeof v === 'string' ? v : '');
      return n;
    });
  }, []);

  // Keep a draft on this device while editing.
  useEffect(() => {
    if (done.current) return;
    const timer = setTimeout(() => {
      if (dirty) writeDraft(pageId, { draft: d, savedAt: Date.now(), baseVersion: page?.version ?? null });
    }, 400);
    return () => clearTimeout(timer);
  }, [d, dirty, pageId, page?.version]);

  // Who publishes: the page's owner, or the owner it will have once created.
  const ownerNow = page
    ? page.owningTeamId
    : pageOwner(model, { ownerTeamId: d.ownerTeamId || null, attachedTo: d.attachedTo });
  const ownerAfter = pageOwner(model, { ownerTeamId: d.ownerTeamId || null, attachedTo: d.attachedTo });
  const canPublish = page ? page.permissions.publish : canPublishPage(me?.role, teamIds, ownerAfter);
  const proposing = !canPublish || proposeInstead;
  const ownerName = ownerNow && model ? teamName(model, ownerNow) : t.pages.admins;
  const ownerAfterName = ownerAfter && model ? teamName(model, ownerAfter) : null;
  const ownerHint = ownerAfterName ? t.pages.ownerHint(ownerAfterName) : t.pages.adminsHint;

  /* ----- links: preview, lint and autocomplete ----- */
  const deferred = useDeferredValue(d.body);
  const doc = useMemo(() => parseDoc(deferred), [deferred]);
  const resolve = useResolver();
  const broken = useMemo(() => wikiLinks(deferred).filter((l) => !resolve(l.kind, l.id).exists), [deferred, resolve]);
  const ta = useRef<HTMLTextAreaElement>(null);
  const pendingSel = useRef<[number, number] | null>(null);
  const [caret, setCaret] = useState(0);
  const [acClosed, setAcClosed] = useState(false);
  const [acIdx, setAcIdx] = useState(0);
  const lq = linkQuery(d.body, caret);
  const split = lq ? splitKind(lq.query) : null;
  const items = useLinkSearch(lq && !acClosed ? split!.text : null, split?.kinds ?? LINK_KINDS);
  const acOpen = !!lq && !acClosed;
  const [acPos, setAcPos] = useState({ left: 8, top: 40 });
  useLayoutEffect(() => {
    if (pendingSel.current && ta.current) {
      const [a, b] = pendingSel.current;
      pendingSel.current = null;
      ta.current.focus();
      ta.current.setSelectionRange(a, b);
      setCaret(b);
    }
  });
  useLayoutEffect(() => {
    if (!acOpen || !ta.current) return;
    const xy = caretPoint(ta.current, lq!.start);
    const w = Math.min(380, ta.current.clientWidth - 16);
    setAcPos({ left: Math.max(8, Math.min(xy.x - 12, ta.current.clientWidth - w - 8)), top: xy.y + 6 });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [acOpen, lq?.start, d.body]);

  const replace = (start: number, end: number, text: string, selFrom: number, selTo: number) => {
    const el = ta.current;
    let done2 = false;
    if (el && typeof document.execCommand === 'function') {
      el.focus();
      el.setSelectionRange(start, end);
      try {
        done2 = document.execCommand('insertText', false, text);
      } catch {
        done2 = false;
      }
      if (done2 && el.value.slice(start, start + text.length) !== text) done2 = false;
    }
    if (!done2) set('body', d.body.slice(0, start) + text + d.body.slice(end));
    pendingSel.current = [selFrom, selTo];
  };

  const insertLink = (o: LinkOption) => {
    if (!lq) return;
    const link = `[[${o.kind}:${o.id}]]`;
    replace(lq.start, caret, link, lq.start + link.length, lq.start + link.length);
    setAcIdx(0);
  };

  const applySnippet = (kind: Snippet) => {
    const el = ta.current;
    const a = el?.selectionStart ?? d.body.length;
    const b = el?.selectionEnd ?? a;
    const [before, def, after] = snippet(t, kind);
    const sel = d.body.slice(a, b) || def;
    const from = a + before.length;
    replace(a, b, before + sel + after, kind === 'link' ? a + 2 : from, kind === 'link' ? a + 2 : from + sel.length);
    setAcClosed(false);
  };

  const onKey = (e: KeyboardEvent<HTMLTextAreaElement>) => {
    if (acOpen) {
      const n = items?.length ?? 0;
      if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
        e.preventDefault();
        if (n) setAcIdx((i) => (i + (e.key === 'ArrowDown' ? 1 : n - 1)) % n);
        return;
      }
      if ((e.key === 'Enter' || e.key === 'Tab') && n && items?.[acIdx]) {
        e.preventDefault();
        insertLink(items[acIdx]);
        return;
      }
      if (e.key === 'Escape') {
        e.preventDefault();
        e.stopPropagation();
        setAcClosed(true);
        return;
      }
    }
    if ((e.ctrlKey || e.metaKey) && (e.key === 'b' || e.key === 'i')) {
      e.preventDefault();
      applySnippet(e.key === 'b' ? 'b' : 'i');
    }
  };

  /* ----- leaving ----- */
  const leaveTo = useCallback(
    (to: string) => {
      done.current = true;
      writeDraft(pageId, null);
      setLeave(null);
      go(to);
    },
    [go, pageId],
  );
  const backTo = page ? pagePaths.page(page.id) : pagePaths.home;
  useEffect(() => {
    if (!dirty) return;
    const click = (e: MouseEvent) => {
      if (done.current || e.defaultPrevented || e.button !== 0 || e.metaKey || e.ctrlKey || e.shiftKey) return;
      const a = (e.target as Element | null)?.closest?.('a[href]');
      if (!a || a.getAttribute('target') === '_blank') return;
      const href = a.getAttribute('href') ?? '';
      if (!href.startsWith('/') || href.startsWith('//')) return;
      e.preventDefault();
      e.stopPropagation();
      setLeave(href);
    };
    document.addEventListener('click', click, true);
    return () => document.removeEventListener('click', click, true);
  }, [dirty]);

  /* ----- saving ----- */
  const takenIds = useMemo(() => new Set((list ?? []).map((p) => p.id)), [list]);
  const submit = async (ev?: FormEvent) => {
    ev?.preventDefault();
    const e = validateDraft(t, d, { isNew, ownerRequired: isNew && !isAdmin, takenIds });
    setErrors(e);
    setFailure(null);
    if (Object.keys(e).length) {
      setSettingsOpen(true);
      setTimeout(() => document.getElementById(Object.keys(e)[0])?.focus(), 0);
      return;
    }
    const siblings = (list ?? []).filter((p) => (p.parentId ?? '') === d.parentId && p.id !== pageId);
    const order =
      page && (page.parentId ?? '') === d.parentId
        ? page.order
        : siblings.reduce((n, p) => Math.max(n, p.order + 10), 0);
    const input: PageInput = {
      id: page ? page.id : d.slug,
      title: d.title.trim(),
      body: d.body,
      parentId: d.parentId || null,
      order,
      attachedTo: d.attachedTo,
      ownerTeamId: d.ownerTeamId || null,
      tags: d.tags,
      visibility: d.visibility,
      version: page?.version ?? null,
    };
    setBusy(true);
    try {
      if (proposing) {
        const p = await api.createProposal({
          title: page ? t.pages.proposalUpdate(input.title!) : t.pages.proposalNew(input.title!),
          comment: comment.trim() || null,
          submit: true,
          changes: [
            {
              kind: 'page',
              targetId: page ? page.id : null,
              operation: page ? 'update' : 'create',
              baseVersion: page?.version ?? null,
              after: input as unknown as Record<string, unknown>,
            },
          ],
        });
        refreshInbox();
        toast(t.pages.sent(ownerName));
        leaveTo(`/review/${encodeURIComponent(p.id)}`);
      } else {
        const saved = page ? await api.updatePage(page.id, input) : await api.createPage(input);
        await reloadPages();
        toast(page ? t.pages.published(saved.version) : t.pages.created);
        leaveTo(pagePaths.page(saved.id));
      }
    } catch (err) {
      if (err instanceof ApiError && err.isConflict && !isNew) setConflict(true);
      else if (err instanceof ApiError && err.isConflict && isNew) setErrors({ 'ps-slug': t.pages.errors.slugTaken });
      else if (err instanceof ApiError && err.status === 400 && Object.keys(err.fieldErrors).length) {
        const se = serverErrors(err);
        const map: Record<string, string> = {};
        for (const [k, v] of Object.entries(se)) {
          const key =
            k === 'title'
              ? 'ps-title'
              : k === 'id'
                ? 'ps-slug'
                : k === 'parentId'
                  ? 'ps-parent'
                  : k.startsWith('attachedTo')
                    ? 'ps-att'
                    : k === 'ownerTeamId'
                      ? 'ps-team'
                      : 'ps-other';
          map[key] = map[key] ? map[key] + ' ' + v : v;
        }
        setErrors(map);
        if (map['ps-other']) setFailure(map['ps-other']);
        setSettingsOpen(true);
      } else
        setFailure(
          err instanceof ApiError && err.status === 0
            ? t.common.networkError
            : err instanceof ApiError
              ? err.detail || err.message
              : t.common.genericError,
        );
    } finally {
      setBusy(false);
    }
  };

  const remove = async () => {
    if (!page || !window.confirm(t.pages.deleteConfirm(page.title))) return;
    try {
      await api.deletePage(page.id);
      await reloadPages();
      toast(t.pages.deleted(page.title));
      leaveTo(pagePaths.home);
    } catch (e) {
      setFailure(e instanceof ApiError ? e.detail || e.message : t.common.genericError);
    }
  };

  const lines = d.body ? d.body.split('\n').length : 0;
  const ancestors = page?.ancestors ?? [];
  const parentTrail = !page && d.parentId ? byId.get(d.parentId) : null;
  const heading = d.title.trim() || t.pages.untitled;

  return (
    <form onSubmit={(e) => void submit(e)} noValidate aria-labelledby="ed-h">
      <nav className="crumbs2" aria-label={t.pages.breadcrumb}>
        <Link to={pagePaths.home}>{t.pages.title}</Link>
        {ancestors.map((a) => (
          <span key={a.id} style={{ display: 'contents' }}>
            <span className="sep" aria-hidden="true">
              /
            </span>
            <Link to={pagePaths.page(a.id)}>{a.title || t.pages.restrictedPage}</Link>
          </span>
        ))}
        {parentTrail && !isRestrictedPage(parentTrail) && (
          <>
            <span className="sep" aria-hidden="true">
              /
            </span>
            <Link to={pagePaths.page(parentTrail.id)}>{parentTrail.title}</Link>
          </>
        )}
        {page && (
          <>
            <span className="sep" aria-hidden="true">
              /
            </span>
            <Link to={pagePaths.page(page.id)}>{page.title}</Link>
          </>
        )}
      </nav>
      <div className="ed-head">
        <div>
          <span className="kb page">{t.pages.badge}</span>{' '}
          <span className="muted">{isNew ? t.pages.creating : proposing ? t.pages.suggesting : t.pages.editing}</span>
          <h1 id="ed-h">{heading}</h1>
        </div>
        {dirty && (
          <span className="dirty" role="status">
            {t.pages.unsaved}
          </span>
        )}
      </div>
      {!canPublish && (
        <div className="banner warn" style={{ marginBottom: 14 }} role="note">
          <IconInfo />
          <span className="bt">
            <b>{t.pages.ownerBanner(ownerName)}</b>
            {t.pages.ownerBannerText(ownerName)}
          </span>
        </div>
      )}
      {showRestored && (
        <div className="banner info" style={{ marginBottom: 14 }} role="status">
          <IconInfo />
          <span className="bt">
            <b>{t.pages.draftRestored}</b>
            <div className="page-actions" style={{ marginTop: 8 }}>
              <button
                type="button"
                className="btn sm"
                onClick={() => {
                  writeDraft(pageId, null);
                  setD(base);
                  setShowRestored(false);
                }}
              >
                {t.pages.discardDraft}
              </button>
            </div>
          </span>
        </div>
      )}
      {conflict && (
        <div style={{ marginBottom: 14 }}>
          <ConflictBanner onReload={() => leaveTo(pagePaths.edit(page!.id))} />
        </div>
      )}
      {failure && (
        <div className="banner danger" role="alert" style={{ marginBottom: 14 }}>
          <IconWarn />
          <span className="bt">{failure}</span>
        </div>
      )}

      <div className="ed-grid">
        <div className="box ed-main">
          <div className="tbar" role="toolbar" aria-label={t.pages.toolbar.label}>
            <div className="g">
              <button
                type="button"
                className="tbtn"
                title={t.pages.toolbar.h2}
                aria-label={t.pages.toolbar.h2}
                onClick={() => applySnippet('h2')}
              >
                H2
              </button>
              <button
                type="button"
                className="tbtn"
                title={t.pages.toolbar.h3}
                aria-label={t.pages.toolbar.h3}
                onClick={() => applySnippet('h3')}
              >
                H3
              </button>
            </div>
            <div className="g">
              <button
                type="button"
                className="tbtn b"
                title={t.pages.toolbar.bold}
                aria-label={t.pages.toolbar.bold}
                onClick={() => applySnippet('b')}
              >
                B
              </button>
              <button
                type="button"
                className="tbtn i"
                title={t.pages.toolbar.italic}
                aria-label={t.pages.toolbar.italic}
                onClick={() => applySnippet('i')}
              >
                I
              </button>
            </div>
            <div className="g">
              <button
                type="button"
                className="tbtn"
                title={t.pages.toolbar.ul}
                aria-label={t.pages.toolbar.ul}
                onClick={() => applySnippet('ul')}
              >
                <IconList />
              </button>
              <button
                type="button"
                className="tbtn"
                title={t.pages.toolbar.ol}
                aria-label={t.pages.toolbar.ol}
                onClick={() => applySnippet('ol')}
              >
                <IconOList />
              </button>
            </div>
            <div className="g">
              <button
                type="button"
                className="tbtn"
                title={t.pages.toolbar.table}
                aria-label={t.pages.toolbar.table}
                onClick={() => applySnippet('table')}
              >
                <IconTable />
              </button>
              <button
                type="button"
                className="tbtn"
                title={t.pages.toolbar.code}
                aria-label={t.pages.toolbar.code}
                onClick={() => applySnippet('code')}
              >
                <IconCode />
              </button>
              <button
                type="button"
                className="tbtn"
                title={t.pages.toolbar.callout}
                aria-label={t.pages.toolbar.callout}
                onClick={() => applySnippet('callout')}
              >
                <IconCallout />
              </button>
            </div>
            <div className="g glink">
              <button
                type="button"
                className="tbtn lnk"
                title={t.pages.toolbar.link}
                onClick={() => applySnippet('link')}
              >
                <IconLink />
                <span className="lbl-d">{t.pages.toolbar.linkShort}</span>
              </button>
            </div>
            <span className="sp" />
            <span className="thint lbl-d">
              {t.pages.toolbar.hintBefore} <code>[[</code> {t.pages.toolbar.hintAfter}
            </span>
          </div>
          <div className="ed-tabs">
            <div className="seg" role="group" aria-label={t.pages.editorView}>
              <button type="button" aria-pressed={tab === 'src'} onClick={() => setTab('src')}>
                <IconEdit />
                {t.pages.write}
              </button>
              <button type="button" aria-pressed={tab === 'prev'} onClick={() => setTab('prev')}>
                <IconEye />
                {t.pages.preview}
              </button>
            </div>
          </div>
          <div className={'split' + (tab === 'prev' ? ' show-prev' : '')}>
            <div className="pane p-src">
              <div className="pane-h">
                {t.pages.markdown} <span className="n">{t.pages.lines(lines)}</span>
              </div>
              <div className="src-wrap">
                <textarea
                  ref={ta}
                  id="ps-body"
                  className="md-src"
                  spellCheck={false}
                  aria-label={t.pages.source}
                  role="combobox"
                  aria-autocomplete="list"
                  aria-expanded={acOpen}
                  aria-controls="ed-ac"
                  aria-activedescendant={acOpen && items?.length ? `ed-ac-${acIdx}` : undefined}
                  value={d.body}
                  onChange={(e) => {
                    set('body', e.target.value);
                    setCaret(e.target.selectionStart);
                    setAcClosed(false);
                    setAcIdx(0);
                  }}
                  onSelect={(e) => setCaret(e.currentTarget.selectionStart)}
                  onKeyDown={onKey}
                  onBlur={() => setTimeout(() => setAcClosed(true), 150)}
                  onFocus={() => setAcClosed(false)}
                  onScroll={() => acOpen && setCaret((c) => c)}
                />
                {acOpen && (
                  <div className="ac" role="listbox" id="ed-ac" aria-label={t.pages.ac.label} style={acPos}>
                    <div className="ach">
                      <span>{t.pages.ac.title}</span>
                      <code>[[{lq!.query}</code>
                    </div>
                    <LinkOptions
                      id="ed-ac"
                      items={items}
                      active={acIdx}
                      query={split!.text}
                      onPick={insertLink}
                      onHover={setAcIdx}
                    />
                    <div className="acf" aria-hidden="true">
                      <span>
                        <kbd>↑</kbd> <kbd>↓</kbd> {t.pages.ac.choose}
                      </span>
                      <span>
                        <kbd>Enter</kbd> {t.pages.ac.insert}
                      </span>
                      <span>
                        <kbd>Esc</kbd> {t.pages.ac.close}
                      </span>
                    </div>
                  </div>
                )}
              </div>
            </div>
            <div className="pane p-prev">
              <div className="pane-h">
                {t.pages.preview} <span className="n">{t.pages.asReaders}</span>
              </div>
              {broken.length > 0 && (
                <div className="lintbar" role="status">
                  <IconWarn />
                  <span>
                    {t.pages.lint(broken.length)}{' '}
                    {broken.map((b, i) => (
                      <span key={b.kind + b.id}>
                        {i > 0 && ', '}
                        <code>
                          [[{b.kind}:{b.id}]]
                        </code>
                      </span>
                    ))}
                    . {t.pages.lintAfter}
                  </span>
                </div>
              )}
              <div className="md-prev">
                <DocMarkdown doc={doc} resolve={resolve} />
              </div>
            </div>
          </div>
        </div>
        <div>
          <PageSettings
            draft={d}
            set={set}
            isNew={isNew}
            pageId={pageId}
            errors={errors}
            open={settingsOpen}
            onToggle={setSettingsOpen}
            ownerHint={ownerHint}
          />
          {page && page.permissions.publish && (
            <div style={{ marginTop: 12 }}>
              <button
                type="button"
                className="btn ghost sm"
                onClick={() => void remove()}
                style={{ color: 'var(--danger-fg)' }}
              >
                <IconTrash />
                {t.pages.deletePage}
              </button>
            </div>
          )}
        </div>
      </div>

      {proposing ? (
        <div className="ed-bar member">
          <div className="rv">
            <Reviewers owner={ownerNow} />
            <textarea
              className="textarea"
              aria-label={t.pages.commentLabel}
              placeholder={t.pages.commentPh(ownerName)}
              value={comment}
              maxLength={2000}
              onChange={(e) => setComment(e.target.value)}
            />
            {canPublish && (
              <label className="chk" style={{ gridColumn: 2 }}>
                <input type="checkbox" checked={proposeInstead} onChange={(e) => setProposeInstead(e.target.checked)} />
                {t.pages.proposeInstead}
              </label>
            )}
          </div>
          <div className="acts">
            <button
              type="button"
              className="btn"
              onClick={() => (dirty ? setLeave(backTo) : leaveTo(backTo))}
              disabled={busy}
            >
              {t.common.cancel}
            </button>
            <button type="submit" className="btn primary" disabled={busy || conflict}>
              <IconSend />
              {busy ? t.pages.submitting : t.pages.submit}
            </button>
          </div>
        </div>
      ) : (
        <div className="ed-bar">
          <div className="st">
            <label className="chk">
              <input type="checkbox" checked={proposeInstead} onChange={(e) => setProposeInstead(e.target.checked)} />
              {t.pages.proposeInstead}
            </label>
            {d.visibility === 'restricted' && (
              <span>
                <IconLock /> {t.pages.visibleRestricted}
              </span>
            )}
          </div>
          <div className="acts">
            <button
              type="button"
              className="btn"
              onClick={() => (dirty ? setLeave(backTo) : leaveTo(backTo))}
              disabled={busy}
            >
              {t.common.cancel}
            </button>
            <button type="submit" className="btn primary" disabled={busy || conflict}>
              <IconSend />
              {busy ? t.pages.publishing : t.pages.publish}
            </button>
          </div>
        </div>
      )}
      {leave && <LeaveDialog title={heading} onDiscard={() => leaveTo(leave)} onKeep={() => setLeave(null)} />}
    </form>
  );
}

function EditorSkeleton() {
  return (
    <div aria-busy="true">
      <span className="sk line" style={{ width: 260 }} />
      <span className="sk title" style={{ width: '45%', marginTop: 12 }} />
      <div className="ed-grid" style={{ marginTop: 18 }}>
        <span className="sk block" style={{ height: 520 }} />
        <span className="sk block" style={{ height: 420 }} />
      </div>
    </div>
  );
}

/** The editor of an existing page (/pages/:id/edit). */
export function PageEditor() {
  const { id = '' } = useParams();
  const { t } = useI18n();
  const { data, error } = useRecord<PageDetail | RestrictedPage>(api.page, id);
  const { model } = useLandscape();
  const { list } = usePages();
  const page = data && !isRestrictedPage(data) && data.id === id ? data : null;
  useDocumentTitle(page ? page.title : t.pages.title);
  const stored = useMemo(() => (page ? readDraft(page.id) : null), [page]);
  const usable = stored && page && stored.baseVersion === page.version ? stored : null;
  let body;
  if (error || (data && isRestrictedPage(data)))
    body = (
      <div className="banner danger" role="alert">
        <IconWarn />
        <div className="bt">
          <b>{error?.status === 404 ? t.pages.notFound : error ? error.message : t.pages.restrictedText}</b>
          <div className="page-actions" style={{ marginTop: 10 }}>
            <Link className="btn" to={pagePaths.home}>
              {t.pages.backToPages}
            </Link>
          </div>
        </div>
      </div>
    );
  else if (!page || !model || !list) body = <EditorSkeleton />;
  else if (!page.permissions.publish && !page.permissions.propose)
    body = (
      <div className="banner" role="note">
        <span className="bt">
          <b>{t.record.readOnly}.</b> {t.record.readOnlyHint}
        </span>
      </div>
    );
  else
    body = (
      <EditorBody
        key={page.id + ':' + page.version}
        page={page}
        initial={usable?.draft ?? toDraft(page)}
        restored={!!usable}
      />
    );
  return (
    <PagesLayout full title={page?.title}>
      {body}
    </PagesLayout>
  );
}

/** A new page (/pages/new), from a template, maybe attached to a record or under a parent. */
export function NewPageEditor() {
  const { t, lang } = useI18n();
  const { model } = useLandscape();
  const { list, byId } = usePages();
  const { canEdit, isAdmin, teamIds } = useAuth();
  const [params] = useSearchParams();
  const loc = useLocation();
  useDocumentTitle(t.pages.creating);
  const template = (params.get('template') ?? 'blank') as TemplateId;
  const kind = params.get('attachedKind') as RecordKind | null;
  const attId = params.get('attachedId');
  const parent = params.get('parentId');
  const ready = !!model && !!list;
  const initial = useMemo<PageDraft | null>(() => {
    if (!ready) return null;
    const attachedTo: AttachedTo | null =
      kind && attId && (ATTACH_KINDS as string[]).includes(kind) ? { kind, id: attId } : null;
    const link = attachedTo ? `[[${attachedTo.kind}:${attachedTo.id}]]` : '';
    const tpl = template !== 'blank' && template in TEMPLATE_TAG ? template : null;
    // A free page needs an owner: the member's first team when it has one.
    const owner = !attachedTo && !isAdmin && teamIds.length ? teamIds[0] : '';
    return {
      title: '',
      slug: '',
      slugTouched: false,
      body: tpl ? t.pages.tpl[tpl](link) : '',
      parentId: parent && byId.has(parent) ? parent : '',
      attachedTo,
      ownerTeamId: owner,
      tags: tpl ? [TEMPLATE_TAG[tpl]] : [],
      visibility: 'everyone',
    };
    // The template text follows the language at the time the editor opens.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [ready, loc.search, lang]);
  const stored = useMemo(() => (initial && !loc.search ? readDraft(null) : null), [initial, loc.search]);
  let body;
  if (!canEdit)
    body = (
      <div className="banner" role="note">
        <span className="bt">
          <b>{t.record.readOnly}.</b> {t.record.readOnlyHint}
        </span>
      </div>
    );
  else if (!initial) body = <EditorSkeleton />;
  else
    body = (
      <EditorBody key={loc.search} page={null} initial={stored?.draft ?? initial} fresh={initial} restored={!!stored} />
    );
  return (
    <PagesLayout full title={t.pages.creating}>
      {body}
    </PagesLayout>
  );
}
