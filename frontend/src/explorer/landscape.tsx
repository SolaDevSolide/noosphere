import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';
import { api, ApiError } from '../api/client';
import type { Landscape } from '../api/types';
import { buildModel, EMPTY_FILTERS, type Filters, type Model } from '../domain/model';

interface LandscapeState {
  model: Model | null;
  error: ApiError | null;
  loading: boolean;
  reload: () => void;
  filters: Filters;
  setFilters: (f: Filters | ((f: Filters) => Filters)) => void;
  /** Lens: show the second level of neighbours. */
  second: boolean;
  setSecond: (v: boolean) => void;
}

const Ctx = createContext<LandscapeState | null>(null);

/** Loads /api/landscape once and keeps the explorer state (filters, options) across pages. */
export function LandscapeProvider({ children }: { children: ReactNode }) {
  const [data, setData] = useState<Landscape | null>(null);
  const [error, setError] = useState<ApiError | null>(null);
  const [loading, setLoading] = useState(true);
  const [tick, setTick] = useState(0);
  const [filters, setFilters] = useState<Filters>(EMPTY_FILTERS);
  const [second, setSecond] = useState(false);

  useEffect(() => {
    let alive = true;
    setLoading(true);
    api
      .landscape()
      .then((l) => {
        if (!alive) return;
        setData(l);
        setError(null);
      })
      .catch((e: unknown) => {
        if (alive) setError(e instanceof ApiError ? e : new ApiError(0, 'network'));
      })
      .finally(() => {
        if (alive) setLoading(false);
      });
    return () => {
      alive = false;
    };
  }, [tick]);

  const reload = useCallback(() => setTick((t) => t + 1), []);
  const model = useMemo(() => (data ? buildModel(data) : null), [data]);
  const value = useMemo(
    () => ({ model, error, loading, reload, filters, setFilters, second, setSecond }),
    [model, error, loading, reload, filters, second],
  );
  return <Ctx.Provider value={value}>{children}</Ctx.Provider>;
}

export function useLandscape(): LandscapeState {
  const v = useContext(Ctx);
  if (!v) throw new Error('useLandscape outside LandscapeProvider');
  return v;
}
