import { useEffect, useMemo, useRef, useState } from 'react';
import { useGo } from '../ui/useGo';
import { useLocation } from 'react-router-dom';
import { useI18n } from '../i18n';
import { useLandscape } from '../explorer/landscape';
import { applySearchFilter } from '../explorer/filterOps';
import { paths, searchBridge } from '../explorer/paths';
import { Hl, Sdot } from '../explorer/bits';
import { fold } from '../domain/model';
import { useToast } from '../ui/Toast';
import { IconSearch, TypeIcon } from '../ui/icons';
import { buildIndex, pageItems, runSearch, withPages, type SearchItem } from './searchIndex';
import { api } from '../api/client';

const BADGE_CLS = { app: 'app', flow: 'flow', object: 'data', team: 'team', page: 'page', filter: 'filter' } as const;

export function Search() {
  const { t } = useI18n();
  const { model, setFilters } = useLandscape();
  const nav = useGo();
  const loc = useLocation();
  const toast = useToast();
  const input = useRef<HTMLInputElement>(null);
  const [q, setQ] = useState('');
  const [open, setOpen] = useState(false);
  const [active, setActive] = useState(0);
  const index = useMemo(() => (model ? buildIndex(model, t) : []), [model, t]);
  const fq = fold(q.trim());
  const [pages, setPages] = useState<{ q: string; items: SearchItem[] }>({ q: '', items: [] });
  useEffect(() => {
    const text = q.trim();
    if (!open || !text) return;
    let alive = true;
    const timer = setTimeout(() => {
      api.search(text).then(
        (hits) => alive && setPages({ q: text, items: pageItems(hits) }),
        () => alive && setPages({ q: text, items: [] }),
      );
    }, 180);
    return () => {
      alive = false;
      clearTimeout(timer);
    };
  }, [q, open]);
  const groups = useMemo(
    () =>
      model && open ? withPages(runSearch(index, model, q), pages.q === q.trim() && q.trim() ? pages.items : []) : [],
    [index, model, q, open, pages],
  );
  const flat = useMemo(() => groups.flatMap((g) => g.items), [groups]);

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      const el = document.activeElement;
      const typing =
        el instanceof HTMLElement &&
        (/INPUT|TEXTAREA|SELECT/.test(el.tagName) || el.isContentEditable) &&
        (el as HTMLInputElement).type !== 'range';
      if ((e.key === 'k' || e.key === 'K') && (e.ctrlKey || e.metaKey)) {
        e.preventDefault();
        input.current?.focus();
        input.current?.select();
      } else if (e.key === '/' && !typing) {
        e.preventDefault();
        input.current?.focus();
      }
    };
    document.addEventListener('keydown', onKey);
    return () => document.removeEventListener('keydown', onKey);
  }, []);

  useEffect(() => {
    document.getElementById('opt' + active)?.scrollIntoView({ block: 'nearest' });
  }, [active]);

  const close = () => {
    setOpen(false);
    setActive(0);
  };
  const choose = (it: SearchItem | undefined) => {
    if (!it) return;
    close();
    setQ('');
    input.current?.blur();
    const onExplorer = loc.pathname === '/' || /\/lens$|^\/data-objects\//.test(loc.pathname);
    if (it.kind === 'filter' && it.fk && it.fv) {
      setFilters((f) => applySearchFilter(f, it.fk!, it.fv!));
      toast(t.filters.applied(it.label));
      if (!onExplorer) nav('/');
    } else if (it.kind === 'team') nav(paths.team(it.id));
    else if (it.kind === 'page') nav(`/pages/${encodeURIComponent(it.id)}`);
    else if (it.kind === 'app') {
      if (searchBridge.pickApp) searchBridge.pickApp(it.id);
      else nav(paths.appLens(it.id));
    } else if (it.kind === 'flow') nav(paths.flowLens(it.id));
    else nav(paths.lineage(it.id));
  };

  const onKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === 'ArrowDown') {
      e.preventDefault();
      setOpen(true);
      setActive((a) => (flat.length ? (a + 1) % flat.length : 0));
    } else if (e.key === 'ArrowUp') {
      e.preventDefault();
      setActive((a) => (flat.length ? (a - 1 + flat.length) % flat.length : 0));
    } else if (e.key === 'Enter') {
      e.preventDefault();
      choose(flat[active]);
    } else if (e.key === 'Escape') {
      e.preventDefault();
      e.stopPropagation();
      if (q) {
        setQ('');
        setActive(0);
      } else {
        close();
        input.current?.blur();
      }
    }
  };

  let i = -1;
  return (
    <div className="search" role="search">
      <IconSearch className="si" />
      <input
        ref={input}
        type="text"
        autoComplete="off"
        spellCheck={false}
        placeholder={t.top.searchPlaceholder}
        role="combobox"
        aria-expanded={open}
        aria-controls="results"
        aria-autocomplete="list"
        aria-label={t.top.searchLabel}
        aria-activedescendant={open && flat.length ? 'opt' + active : undefined}
        value={q}
        onChange={(e) => {
          setQ(e.target.value);
          setActive(0);
          setOpen(true);
        }}
        onFocus={() => setOpen(true)}
        onBlur={() => setTimeout(close, 120)}
        onKeyDown={onKeyDown}
      />
      <div className="keys" aria-hidden="true">
        <kbd>/</kbd>
        <kbd>Ctrl K</kbd>
      </div>
      {open && model && (
        <div id="results" className="results" role="listbox" aria-label={t.top.searchLabel}>
          {groups.map((g) => (
            <div key={g.kind} role="group" aria-label={t.search.groups[g.kind]}>
              <div className="rg" role="presentation">
                {!fq && g.kind === 'app' ? t.search.busy : t.search.groups[g.kind]}
              </div>
              {g.items.map((it) => {
                i++;
                const idx = i;
                return (
                  <div
                    key={it.kind + it.id}
                    id={'opt' + idx}
                    tabIndex={-1}
                    role="option"
                    aria-selected={idx === active}
                    className={'opt' + (idx === active ? ' act' : '')}
                    onMouseDown={(e) => {
                      e.preventDefault();
                      choose(it);
                    }}
                    onMouseMove={() => idx !== active && setActive(idx)}
                  >
                    <span className={'kb ' + BADGE_CLS[it.kind]}>{t.search.badge[it.kind]}</span>
                    <span>
                      <span className="ol">
                        <Hl label={it.label} q={fq} />
                      </span>
                      <span className="os">{it.sub}</span>
                    </span>
                    {it.flowType ? (
                      <span className={'oi t-' + it.flowType}>
                        <TypeIcon type={it.flowType} />
                      </span>
                    ) : it.sens ? (
                      <Sdot s={it.sens} />
                    ) : (
                      <span />
                    )}
                  </div>
                );
              })}
            </div>
          ))}
          {!flat.length && <div className="r-empty">{t.search.noMatch(q)}</div>}
          <div className="r-foot">
            <span>
              <kbd>↑</kbd> <kbd>↓</kbd> {t.search.move}
            </span>
            <span>
              <kbd>Enter</kbd> {t.search.open}
            </span>
            <span>
              <kbd>Esc</kbd> {t.search.close}
            </span>
          </div>
        </div>
      )}
    </div>
  );
}
