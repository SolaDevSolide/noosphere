import { useMemo, useState, type DragEvent } from 'react';
import { Link } from 'react-router-dom';
import { api, ApiError } from '../api/client';
import { isRestrictedPage, type PageTreeNode } from '../api/types';
import { useAuth } from '../auth/auth';
import { fold } from '../domain/model';
import { useLandscape } from '../explorer/landscape';
import { useI18n } from '../i18n';
import {
  IconChevron,
  IconDoc,
  IconFolder,
  IconGrip,
  IconLock,
  IconMenu,
  IconPlus,
  IconSearch,
  IconX,
} from '../ui/icons';
import { useToast } from '../ui/Toast';
import { canPublishPage, dropTarget, filterTree, pageOwner, pagePaths } from './model';
import { useOpenTree } from './PagesLayout';
import { usePages } from './store';

type Where = 'before' | 'after' | 'inside';

const COLLAPSED_KEY = 'noos-pages-collapsed';
function storedCollapsed(): Set<string> {
  try {
    const v = JSON.parse(localStorage.getItem(COLLAPSED_KEY) ?? '[]') as unknown;
    return new Set(Array.isArray(v) ? v.filter((x): x is string => typeof x === 'string') : []);
  } catch {
    return new Set();
  }
}

export function TreeSkeleton() {
  return (
    <div className="sk-tree" aria-busy="true">
      {[70, 55, 80, 62, 74, 48, 66].map((w, i) => (
        <span key={i} className="sk line" style={{ width: w + '%', marginLeft: i % 3 ? 18 : 0 }} />
      ))}
    </div>
  );
}

/**
 * The page tree: filter, expand and collapse, and drag and drop to reorder a page or move it under another one
 * (pages the reader may publish only; the server checks it again).
 */
export function PageTree({ currentId, onClose }: { currentId?: string; onClose?: () => void }) {
  const { t } = useI18n();
  const { me, teamIds, canEdit } = useAuth();
  const { model } = useLandscape();
  const { tree, list, byId, reload } = usePages();
  const toast = useToast();
  const [q, setQ] = useState('');
  const [collapsed, setCollapsed] = useState<Set<string>>(storedCollapsed);
  const [drag, setDrag] = useState<string | null>(null);
  const [over, setOver] = useState<{ id: string; where: Where } | null>(null);
  const fq = fold(q.trim());
  const shown = useMemo(() => (tree ? filterTree(tree, fq, fold) : null), [tree, fq]);

  const canMove = (id: string) => {
    const p = byId.get(id);
    if (!p || isRestrictedPage(p)) return me?.role === 'admin';
    return canPublishPage(me?.role, teamIds, pageOwner(model, p));
  };
  const anyMovable = !!list && list.some((p) => canMove(p.id));

  const toggle = (id: string) =>
    setCollapsed((c) => {
      const n = new Set(c);
      if (n.has(id)) n.delete(id);
      else n.add(id);
      try {
        localStorage.setItem(COLLAPSED_KEY, JSON.stringify([...n]));
      } catch {
        /* storage blocked */
      }
      return n;
    });

  const whereOf = (e: DragEvent<HTMLElement>): Where => {
    const r = e.currentTarget.getBoundingClientRect();
    const y = (e.clientY - r.top) / Math.max(1, r.height);
    return y < 0.28 ? 'before' : y > 0.72 ? 'after' : 'inside';
  };

  const drop = async (target: string, where: Where) => {
    const dragged = drag;
    setDrag(null);
    setOver(null);
    if (!dragged || !list) return;
    const plan = dropTarget(list, dragged, target, where);
    if (!plan) return;
    const p = byId.get(dragged);
    try {
      for (const r of plan.renumber) {
        const s = byId.get(r.id);
        if (s) await api.movePage(r.id, s.parentId, r.order);
      }
      await api.movePage(dragged, plan.parentId, plan.order);
      toast(t.pages.moved(p && !isRestrictedPage(p) ? p.title : dragged));
    } catch (e) {
      toast(e instanceof ApiError && e.detail ? e.detail : t.pages.moveFailed);
    }
    await reload();
  };

  const node = (n: PageTreeNode, depth: number) => {
    const kids = n.children.length > 0;
    const open = !!fq || !collapsed.has(n.id);
    const cur = n.id === currentId;
    const summary = byId.get(n.id);
    const broken = summary && !isRestrictedPage(summary) ? summary.brokenLinks : 0;
    const movable = !fq && canMove(n.id);
    const title = n.restricted ? t.pages.restrictedPage : (n.title ?? n.id);
    const cls =
      'ptrow' +
      (cur ? ' cur' : '') +
      (movable ? ' movable' : '') +
      (drag === n.id ? ' dragging' : '') +
      (over?.id === n.id ? ' drop-' + over.where : '');
    return (
      <li key={n.id} role="treeitem" aria-expanded={kids ? open : undefined} aria-selected={cur}>
        <div
          className={cls}
          draggable={movable}
          onDragStart={(e) => {
            setDrag(n.id);
            e.dataTransfer.effectAllowed = 'move';
            e.dataTransfer.setData('text/plain', n.id);
          }}
          onDragEnd={() => {
            setDrag(null);
            setOver(null);
          }}
          onDragOver={(e) => {
            if (!drag || drag === n.id) return;
            e.preventDefault();
            e.dataTransfer.dropEffect = 'move';
            const where = whereOf(e);
            if (over?.id !== n.id || over.where !== where) setOver({ id: n.id, where });
          }}
          onDragLeave={() => setOver((o) => (o?.id === n.id ? null : o))}
          onDrop={(e) => {
            e.preventDefault();
            void drop(n.id, whereOf(e));
          }}
          title={drag && over?.id === n.id && over.where === 'before' ? t.pages.dropBefore(title) : undefined}
        >
          <span className="grip" title={t.pages.dragHandle} aria-hidden="true">
            <IconGrip size={12} />
          </span>
          {kids ? (
            <button
              type="button"
              className="tw"
              aria-expanded={open}
              aria-label={t.pages.expand(title)}
              onClick={() => toggle(n.id)}
            >
              <IconChevron />
            </button>
          ) : (
            <span />
          )}
          <Link to={pagePaths.page(n.id)} aria-current={cur ? 'page' : undefined} onClick={onClose} draggable={false}>
            <span className="ti">
              {n.restricted ? <IconLock /> : depth === 0 && kids ? <IconFolder /> : <IconDoc />}
            </span>
            <span className={'tl' + (n.restricted ? ' muted' : '')}>{title}</span>
            {broken > 0 ? (
              <span className="cnt2" title={t.pages.brokenCount(broken)} aria-label={t.pages.brokenCount(broken)}>
                {broken}
              </span>
            ) : (
              <span />
            )}
          </Link>
        </div>
        {kids && open && <ul role="group">{n.children.map((c) => node(c, depth + 1))}</ul>}
      </li>
    );
  };

  return (
    <nav className={'tree' + (anyMovable ? ' can-move' : '')} aria-label={t.pages.allPages}>
      <div className="tree-h">
        <h2>{t.pages.allPages}</h2>
        {onClose && (
          <button type="button" className="btn ghost sm mob-only" onClick={onClose} aria-label={t.pages.closeTree}>
            <IconX />
          </button>
        )}
      </div>
      <div className="tfilter">
        <IconSearch size={14} />
        <input
          value={q}
          onChange={(e) => setQ(e.target.value)}
          placeholder={t.pages.filter}
          aria-label={t.pages.filter}
          type="search"
          autoComplete="off"
        />
      </div>
      {!shown ? (
        <TreeSkeleton />
      ) : shown.length ? (
        <ul role="tree" aria-label={t.pages.title}>
          {shown.map((n) => node(n, 0))}
        </ul>
      ) : (
        <p className="tree-empty">{fq ? t.pages.noMatch : t.pages.empty}</p>
      )}
      {anyMovable && (
        <div className="tree-f">
          <IconGrip size={12} />
          <span>
            <span className="dragtip">{t.pages.dragTip} </span>
            {me?.role !== 'admin' && t.pages.ownTeamsOnly}
          </span>
        </div>
      )}
      {canEdit && (
        <div className="tree-new">
          <Link className="btn sm" to={pagePaths.create()} onClick={onClose}>
            <IconPlus />
            {t.pages.newPage}
          </Link>
        </div>
      )}
    </nav>
  );
}

/** Opens the page tree drawer on phones; hidden on wider screens where the tree is always shown. */
export function TreeButton({ label }: { label?: string }) {
  const { t } = useI18n();
  const onOpen = useOpenTree();
  return (
    <button
      type="button"
      className="btn sm mob-only"
      onClick={onOpen}
      aria-label={label ? undefined : t.pages.openTree}
      style={{ marginRight: 4 }}
    >
      <IconMenu />
      {label}
    </button>
  );
}
