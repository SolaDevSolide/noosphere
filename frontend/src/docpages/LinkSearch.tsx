import { useEffect, useState } from 'react';
import { api } from '../api/client';
import type { RecordKind, SearchHit } from '../api/types';
import { Hl, Sdot } from '../explorer/bits';
import { fold } from '../domain/model';
import { useLandscape } from '../explorer/landscape';
import { useI18n } from '../i18n';
import { IconApp, IconDoc, IconDomain, IconTeam, TypeIcon } from '../ui/icons';
import { flowLabel } from './model';

export const KB_CLASS: Record<RecordKind, string> = {
  application: 'app',
  flow: 'flow',
  dataObject: 'data',
  team: 'team',
  domain: 'dom',
  page: 'page',
};

export interface LinkOption {
  kind: RecordKind;
  id: string;
  title: string;
  sub: string;
}

/** Searches records and pages through /api/search, after a short pause in typing. */
export function useLinkSearch(query: string | null, kinds?: RecordKind[]) {
  const [state, setState] = useState<{ q: string; items: LinkOption[] | null }>({ q: '', items: null });
  const { model } = useLandscape();
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
            .slice(0, 8)
            .map((h) => {
              const kind = h.kind as RecordKind;
              const title = kind === 'flow' && model ? (flowLabel(model, h.id) ?? h.title) : h.title;
              return { kind, id: h.id, title, sub: h.subtitle ?? '' };
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
  }, [query, kindKey, model]);
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
