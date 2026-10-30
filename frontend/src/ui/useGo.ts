import { useCallback } from 'react';
import { useNavigate } from 'react-router-dom';

/** Navigate without returning the router's promise (event handlers expect void). */
export function useGo() {
  const nav = useNavigate();
  return useCallback((to: string, opts?: { replace?: boolean }) => void nav(to, opts), [nav]);
}
