import { useEffect, useState } from 'react';
import { api } from '../api/client';
import type { RecordKind, SearchHit } from '../api/types';
import { Hl, Sdot, teamName } from '../explorer/bits';
import { fold } from '../domain/model';
import { useLandscape } from '../explorer/landscape';
import { useI18n } from '../i18n';
import { IconApp, IconDoc, IconDomain, IconTeam, TypeIcon } from '../ui/icons';
import type { Model } from '../domain/model';
import { flowLabel, pathOf, type PageIndex } from './model';
import { usePages } from './store';

export const KB_CLASS: Record<RecordKind, string> = {
  application: 'app',
  flow: 'flow',
  dataObject: 'data',
  team: 'team',
  domain: 'domain',
  page: 'page',
};

export interface LinkOption {
  kind: RecordKind;
  id: string;
  title: string;
  sub: string;
}

/** A short second line for a suggestion: the team of an application, the schedule of a flow, the path of a page. */
function subOf(
  m: Model | null,
  pages: PageIndex,
  sens: Record<string, string>,
  kind: RecordKind,
  id: string,
  fallback: string,
) {
  if (kind === 'page') return pages.has(id) ? pathOf(pages, id) : '';
  if (!m) return fallback;
  if (kind === 'application') {
    const a = m.apps.get(id);
    return a && 'teamId' in a ? teamName(m, a.teamId) : fallback;
  }
  if (kind === 'flow') {
    const f = m.flow.get(id);
    return f && 'schedule' in f ? (f.schedule ?? '') : fallback;
  }
  if (kind === 'dataObject') {
    const o = m.objects.get(id);
    return o ? (sens[o.sensitivity] ?? '') : fallback;
  }
  return '';
}

/** Searches records and pages through /api/search, after a short pause in typing. */
export function useLinkSearch(query: string | null, kinds?: RecordKind[]) {
  const [state, setState] = useState<{ q: string; items: LinkOption[] | null }>({ q: '', items: null });
  const { model } = useLandscape();
  const { byId } = usePages();
  const { t } = useI18n();
  const sens = t.sens;
  const kindKey = kinds?.join(',') ?? '';
  useEffect(() => {
    if (query === null) return;
    const q = query.trim();
    if (!q) {
      setState({ q, items: [] });
      return;
    }
    let alive = true;
    setState((s) => ({ q, items: s.q === q ? s.items : null }));
    const timer = setTimeout(() => {
      api.search(q).then(
        (hits: SearchHit[]) => {
          if (!alive) return;
          const allowed = kindKey ? kindKey.split(',') : null;
          const items = hits
            .filter((h) => !allowed || allowed.includes(h.kind))
            .slice(0, 6)
            .map((h) => {
              const kind = h.kind as RecordKind;
              const title = kind === 'flow' && model ? (flowLabel(model, h.id) ?? h.title) : h.title;
              return { kind, id: h.id, title, sub: subOf(model, byId, sens, kind, h.id, h.subtitle ?? '') };
            });
          setState({ q, items });
        },
        () => alive && setState({ q, items: [] }),
      );
    }, 160);
    return () => {
      alive = false;
      clearTimeout(timer);
    };
  }, [query, kindKey, model, byId, sens]);
  return state.items;
}

/** The icon shown at the end of a suggestion, as in the search box. */
export function OptionIcon({ o }: { o: LinkOption }) {
  const { model } = useLandscape();
  if (o.kind === 'flow') {
    const f = model?.flow.get(o.id);
    return (
      <span className={'oi t-' + (f?.type ?? 'batch')}>
        <TypeIcon type={f?.type ?? 'batch'} />
      </span>
    );
  }
  if (o.kind === 'dataObject') {
    const d = model?.objects.get(o.id);
    return d ? <Sdot s={d.sensitivity} /> : <span />;
  }
  const Icon =
    o.kind === 'application' ? IconApp : o.kind === 'team' ? IconTeam : o.kind === 'domain' ? IconDomain : IconDoc;
  return (
    <span className="oi" style={{ color: 'var(--fg-subtle)' }}>
      <Icon size={14} />
    </span>
  );
}

/** A list of link suggestions (records and pages), keyboard driven by its owner. */
export function LinkOptions({
  id,
  items,
  active,
  query,
  onPick,
  onHover,
}: {
  id: string;
  items: LinkOption[] | null;
  active: number;
  query: string;
  onPick: (o: LinkOption) => void;
  onHover: (i: number) => void;
}) {
  const { t } = useI18n();
  const fq = fold(query.trim());
  if (!query.trim()) return <div className="r-empty">{t.pages.ac.typeToSearch}</div>;
  if (items === null) return <div className="r-empty">{t.pages.ac.searching}</div>;
  if (!items.length) return <div className="r-empty">{t.pages.ac.none}</div>;
  return (
    <>
      {items.map((o, i) => (
        <div
          key={o.kind + ':' + o.id}
          id={`${id}-${i}`}
          role="option"
          tabIndex={-1}
          aria-selected={i === active}
          className={'opt' + (i === active ? ' act' : '')}
          onMouseDown={(e) => {
            e.preventDefault();
            onPick(o);
          }}
          onMouseMove={() => i !== active && onHover(i)}
        >
          <span className={'kb ' + KB_CLASS[o.kind]}>{t.pages.kindBadge[o.kind]}</span>
          <span>
            <span className="ol">
              <Hl label={o.title} q={fq} />
            </span>
            <span className="os">
              {o.sub ? o.sub + ' · ' : ''}
              <code>
                {o.kind}:{o.id}
              </code>
            </span>
          </span>
          <OptionIcon o={o} />
        </div>
      ))}
    </>
  );
}
