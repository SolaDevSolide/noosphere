import { useCallback } from 'react';
import type { PageLink, RecordKind } from '../api/types';
import { useLandscape } from '../explorer/landscape';
import { recordHref, resolveLink, type Resolved } from './model';
import { usePages } from './store';

/**
 * Resolves wiki links against what the app already knows (the landscape and the page list). On a published page the
 * server's own answer (`links`) wins: it also knows the records the reader may not list.
 */
export function useResolver(server?: PageLink[] | null) {
  const { model } = useLandscape();
  const { byId } = usePages();
  return useCallback(
    (kind: RecordKind, id: string): Resolved => {
      const local = resolveLink(model, byId, kind, id);
      const s = server?.find((l) => l.kind === kind && l.id === id);
      if (!s) return local;
      if (!s.exists) return { exists: false, restricted: false, title: id, href: null };
      if (s.restricted) return { ...local, exists: true, restricted: true, href: null };
      return {
        ...local,
        exists: true,
        title: local.exists ? local.title : (s.title ?? id),
        href: recordHref(kind, id),
      };
    },
    [model, byId, server],
  );
}
