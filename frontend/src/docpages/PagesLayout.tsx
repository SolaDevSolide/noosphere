import { createContext, useContext, useEffect, useRef, useState, type ReactNode } from 'react';
import { useLocation } from 'react-router-dom';
import type { AttachedTo } from '../api/types';
import { useDocumentTitle } from '../app/settings';
import { useI18n } from '../i18n';
import { TopBar } from '../layout/TopBar';
import { IconApp, IconBulb, IconCallout, IconDoc, IconPlus } from '../ui/icons';
import { useGo } from '../ui/useGo';
import { pagePaths, TEMPLATE_IDS, type TemplateId } from './model';
import { PageTree } from './PageTree';
import { usePages } from './store';

const DrawerCtx = createContext<() => void>(() => {});
/** Opens the page tree drawer (phone widths). */
export const useOpenTree = () => useContext(DrawerCtx);

/** The pages area: header, the page tree on the left (a drawer on phones) and the content. */
export function PagesLayout({
  children,
  currentId,
  title,
  full,
}: {
  children: ReactNode;
  currentId?: string;
  title?: string;
  full?: boolean;
}) {
  useDocumentTitle(title);
  const [open, setOpen] = useState(false);
  const loc = useLocation();
  const { refresh } = usePages();
  useEffect(() => setOpen(false), [loc.pathname]);
  useEffect(() => refresh(), [refresh, loc.pathname]);
  useEffect(() => {
    if (!open) return;
    const key = (e: KeyboardEvent) => {
      if (e.key === 'Escape') setOpen(false);
    };
    document.addEventListener('keydown', key);
    document.querySelector<HTMLInputElement>('.tree-col .tfilter input')?.focus();
    return () => document.removeEventListener('keydown', key);
  }, [open]);
  return (
    <div className="shell">
      <TopBar />
      <main className="page" id="main">
        <DrawerCtx.Provider value={() => setOpen(true)}>
          <div className={'pg-wrap' + (full ? ' full' : '') + (open ? ' drawer-open' : '')}>
            {!full && (
              <>
                <aside className="tree-col">
                  <PageTree currentId={currentId} onClose={() => setOpen(false)} />
                </aside>
                <div className="pdrawer-back" onClick={() => setOpen(false)} aria-hidden="true" />
              </>
            )}
            <div className="pg-main">{children}</div>
          </div>
        </DrawerCtx.Provider>
      </main>
    </div>
  );
}

const TEMPLATE_ICON: Record<TemplateId, typeof IconDoc> = {
  blank: IconDoc,
  runbook: IconBulb,
  adr: IconCallout,
  overview: IconApp,
};

/** "New page", with the choice of a template. */
export function NewPageMenu({
  attachedTo,
  parentId,
  align = 'right',
}: {
  attachedTo?: AttachedTo | null;
  parentId?: string | null;
  align?: 'left' | 'right';
}) {
  const { t } = useI18n();
  const go = useGo();
  const [open, setOpen] = useState(false);
  const wrap = useRef<HTMLDivElement>(null);
  useEffect(() => {
    if (!open) return;
    const click = (e: MouseEvent) => {
      if (!wrap.current?.contains(e.target as Node)) setOpen(false);
    };
    const key = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        setOpen(false);
        wrap.current?.querySelector<HTMLButtonElement>('button')?.focus();
      }
    };
    document.addEventListener('mousedown', click);
    document.addEventListener('keydown', key);
    wrap.current?.querySelector<HTMLButtonElement>('[role=menuitem]')?.focus();
    return () => {
      document.removeEventListener('mousedown', click);
      document.removeEventListener('keydown', key);
    };
  }, [open]);
  const onMenuKey = (e: React.KeyboardEvent) => {
    if (e.key !== 'ArrowDown' && e.key !== 'ArrowUp') return;
    e.preventDefault();
    const items = [...(wrap.current?.querySelectorAll<HTMLButtonElement>('[role=menuitem]') ?? [])];
    const i = items.indexOf(document.activeElement as HTMLButtonElement);
    items[(i + (e.key === 'ArrowDown' ? 1 : items.length - 1)) % items.length]?.focus();
  };
  return (
    <div className="newwrap" ref={wrap}>
      <button
        type="button"
        className="btn primary"
        aria-haspopup="menu"
        aria-expanded={open}
        onClick={() => setOpen((o) => !o)}
      >
        <IconPlus />
        {t.pages.newPage}
      </button>
      {open && (
        <div className={'pmenu' + (align === 'left' ? ' left' : '')} role="menu" tabIndex={-1} onKeyDown={onMenuKey}>
          <div className="rg" role="presentation">
            {t.pages.startFrom}
          </div>
          {TEMPLATE_IDS.map((id) => {
            const Icon = TEMPLATE_ICON[id];
            const [name, desc] = t.pages.templates[id];
            return (
              <button
                key={id}
                type="button"
                role="menuitem"
                onClick={() => {
                  setOpen(false);
                  go(pagePaths.create({ template: id, attachedTo, parentId }));
                }}
              >
                <Icon />
                <b>{name}</b>
                <small>{desc}</small>
              </button>
            );
          })}
        </div>
      )}
    </div>
  );
}
