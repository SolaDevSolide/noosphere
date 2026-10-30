import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { api, ApiError } from '../api/client';
import type { PageListItem, PageTreeNode } from '../api/types';
import { indexPages, type PageIndex } from './model';

interface PagesState {
  /** Every page the reader may list (restricted ones as stubs); null until loaded. */
  list: PageListItem[] | null;
  tree: PageTreeNode[] | null;
  byId: PageIndex;
  error: ApiError | null;
  /** Loads the list and the tree the first time a view needs them. */
  ensure: () => void;
  /** Loads them again when the copy is older than a few seconds (pages change through proposals too). */
  refresh: () => void;
  reload: () => Promise<void>;
}

const Ctx = createContext<PagesState | null>(null);

/** The page list and tree, loaded once on demand and shared by the pages views, records and the editor. */
export function PagesProvider({ children }: { children: ReactNode }) {
  const [list, setList] = useState<PageListItem[] | null>(null);
  const [tree, setTree] = useState<PageTreeNode[] | null>(null);
  const [error, setError] = useState<ApiError | null>(null);
  const started = useRef(false);
  const loadedAt = useRef(0);

  const reload = useCallback(async () => {
    started.current = true;
    loadedAt.current = Date.now();
    try {
      const [l, t] = await Promise.all([api.pages(), api.pageTree()]);
      setList(l);
      setTree(t);
      setError(null);
    } catch (e) {
      setError(e instanceof ApiError ? e : new ApiError(0, 'network'));
    }
  }, []);
  const ensure = useCallback(() => {
    if (!started.current) void reload();
  }, [reload]);
  const refresh = useCallback(() => {
    if (!started.current || Date.now() - loadedAt.current > 5000) void reload();
  }, [reload]);
  const byId = useMemo(() => indexPages(list), [list]);
  const value = useMemo(
    () => ({ list, tree, byId, error, ensure, refresh, reload }),
    [list, tree, byId, error, ensure, refresh, reload],
  );
  return <Ctx.Provider value={value}>{children}</Ctx.Provider>;
}

const EMPTY: PagesState = {
  list: [],
  tree: [],
  byId: new Map(),
  error: null,
  ensure: () => {},
  refresh: () => {},
  reload: () => Promise.resolve(),
};

export function usePages(): PagesState {
  const v = useContext(Ctx) ?? EMPTY;
  const { ensure } = v;
  useEffect(() => ensure(), [ensure]);
  return v;
}
