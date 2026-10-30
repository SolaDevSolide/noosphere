import { Link } from 'react-router-dom';
import { api } from '../api/client';
import { isRestrictedPage, type PageRef, type RecordExtras, type RecordKind } from '../api/types';
import { useAuth } from '../auth/auth';
import { teamName } from '../explorer/bits';
import { useLandscape } from '../explorer/landscape';
import { useI18n } from '../i18n';
import { useRecord } from '../records/common';
import { IconDoc, IconPlus } from '../ui/icons';
import { childCount, pageKind, pageOwner, pagePaths, pathOf, relativeTime } from './model';
import { usePages } from './store';

/**
 * The pages attached to a record, and the pages that mention it, shown under the record's description.
 * Members and administrators also get "New page attached to this record".
 */
export function RecordPages({
  kind,
  record,
  compact,
}: {
  kind: RecordKind;
  record: RecordExtras & { id: string };
  compact?: boolean;
}) {
  const { t, lang } = useI18n();
  const { canEdit } = useAuth();
  const { model } = useLandscape();
  const { list, byId } = usePages();
  const attached: PageRef[] = record.pages ?? [];
  const mentions: PageRef[] = (record.mentionedIn ?? []).filter((m) => !attached.some((a) => a.id === m.id));
  if (!attached.length && !mentions.length && !canEdit) return null;
  const summary = (id: string) => {
    const p = byId.get(id);
    return p && !isRestrictedPage(p) ? p : null;
  };
  const sub = (id: string) => {
    const p = summary(id);
    if (!p || !list) return '';
    const k = pageKind(p.tags);
    const n = childCount(list, id);
    return [k ? t.pages.pageKind[k] : null, n ? t.pages.subPages(n) : null].filter(Boolean).join(' · ');
  };
  const owner = (id: string) => {
    const p = summary(id);
    if (!p) return '';
    const o = pageOwner(model, p);
    return o && model ? teamName(model, o) : t.pages.admins;
  };
  const H = compact ? 'h3' : 'h2';
  return (
    <section className={compact ? 'rec-pages' : 'box rec-pages'} aria-labelledby={`pg-${kind}-h`}>
      <div className="bx-h">
        <H id={`pg-${kind}-h`} style={{ margin: 0 }}>
          {t.pages.recordPages(attached.length)}
        </H>
        <Link className="btn ghost sm" to={pagePaths.home}>
          {t.pages.allPages}
        </Link>
      </div>
      {attached.length > 0 ? (
        <div className="rows">
          {attached.map((p) => {
            const s = summary(p.id);
            const sb = sub(p.id);
            return (
              <Link key={p.id} className="row pr" to={pagePaths.page(p.id)}>
                <span className="ri">
                  <IconDoc />
                </span>
                <span className="rn">
                  {p.title}
                  {sb && <small>{sb}</small>}
                </span>
                <span className="rr">
                  {s && s.brokenLinks > 0 ? (
                    <span className="tg danger">{t.pages.brokenCount(s.brokenLinks)}</span>
                  ) : s ? (
                    relativeTime(s.updatedAt, lang)
                  ) : null}
                </span>
              </Link>
            );
          })}
        </div>
      ) : (
        <p className="muted" style={{ margin: '0 0 4px', fontSize: 13 }}>
          {t.pages.noPages}
        </p>
      )}
      {canEdit && (
        <Link className="addrow" to={pagePaths.create({ attachedTo: { kind, id: record.id } })}>
          <IconPlus />
          {t.pages.newAttached}
        </Link>
      )}
      {mentions.length > 0 && (
        <>
          <H className="sub" style={compact ? { marginTop: 14 } : undefined}>
            {t.pages.mentionedIn(mentions.length)}
          </H>
          <div className="rows">
            {mentions.map((p) => {
              const path = pathOf(byId, p.id);
              return (
                <Link key={p.id} className="row pr" to={pagePaths.page(p.id)}>
                  <span className="ri">
                    <IconDoc />
                  </span>
                  <span className="rn">
                    {p.title}
                    {path && <small>{path}</small>}
                  </span>
                  <span className="rr">{owner(p.id)}</span>
                </Link>
              );
            })}
          </div>
        </>
      )}
    </section>
  );
}

/** The pages of a data object, in the lineage side panel (the panel reads the landscape; pages come from the record). */
export function DataObjectPages({ id }: { id: string }) {
  const { data } = useRecord(api.dataObject, id);
  if (!data || data.id !== id) return <span className="sk line" style={{ width: '60%', marginTop: 16 }} />;
  return (
    <div style={{ marginTop: 16 }}>
      <RecordPages kind="dataObject" record={{ ...data, id }} compact />
    </div>
  );
}
