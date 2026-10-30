import { useEffect, useRef, useState, type KeyboardEvent } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { api, ApiError } from '../api/client';
import type { Proposal } from '../api/types';
import { useDocumentTitle } from '../app/settings';
import { paths } from '../explorer/paths';
import { useI18n } from '../i18n';
import { PageShell } from '../records/common';
import { IconReview, IconWarn } from '../ui/icons';
import { useInbox } from './inbox';
import { AuthorChip, StatusChip } from './bits';

export type ReviewTab = 'validate' | 'mine' | 'decided';
export const REVIEW_TABS: ReviewTab[] = ['validate', 'mine', 'decided'];

/** Loads the proposals behind one tab. */
export async function loadTab(tab: ReviewTab): Promise<Proposal[]> {
  if (tab === 'validate') return api.proposals({ toValidate: true });
  if (tab === 'mine') return api.proposals({ mine: true });
  const [a, r] = await Promise.all([api.proposals({ status: 'accepted' }), api.proposals({ status: 'refused' })]);
  return [...a, ...r].sort((x, y) => (y.decidedAt ?? y.createdAt).localeCompare(x.decidedAt ?? x.createdAt));
}

function ProposalCard({ p, tab }: { p: Proposal; tab: ReviewTab }) {
  const { t, date } = useI18n();
  const targets = p.changes.map((c) => c.targetTitle || c.targetId || t.review.newRecord);
  const when = tab === 'decided' ? p.decidedAt : (p.submittedAt ?? p.createdAt);
  return (
    <li className="pcard">
      <div className="pcard-t">
        <Link to={paths.proposal(p.id)} className="pcard-a">
          {p.title}
        </Link>
        <StatusChip status={p.status} />
      </div>
      <div className="pchips">
        <AuthorChip p={p} />
        {when && (
          <span className="pchip">
            {tab === 'decided' ? t.review.decidedOn(date(when)) : t.review.submittedOn(date(when))}
          </span>
        )}
        {p.outdated && (p.status === 'submitted' || p.status === 'draft') && (
          <span className="pchip warn">
            <IconWarn size={12} />
            {t.review.outdatedChip}
          </span>
        )}
      </div>
      <p className="pcard-s">
        {t.review.changesOn(p.changes.length, [...new Set(targets)].join(', '))}
        {p.status === 'refused' && p.refusalReason && (
          <>
            {' · '}
            <span className="muted">
              {t.review.refusedBecause}: {p.refusalReason}
            </span>
          </>
        )}
      </p>
    </li>
  );
}

/** "Review": proposals to validate, my own proposals and the decided ones, in three tabs. */
export function ReviewPage() {
  const { t } = useI18n();
  const { inbox, refresh } = useInbox();
  const [sp, setSp] = useSearchParams();
  const raw = sp.get('tab') as ReviewTab | null;
  const tab: ReviewTab = raw && REVIEW_TABS.includes(raw) ? raw : 'validate';
  const [items, setItems] = useState<Proposal[] | null>(null);
  const [error, setError] = useState<ApiError | null>(null);
  const [tick, setTick] = useState(0);
  const tabs = useRef<(HTMLButtonElement | null)[]>([]);
  useDocumentTitle(t.review.title);

  useEffect(() => {
    let alive = true;
    setItems(null);
    setError(null);
    loadTab(tab)
      .then((x) => alive && setItems(x))
      .catch((e: unknown) => alive && setError(e instanceof ApiError ? e : new ApiError(0, 'network')));
    refresh();
    return () => {
      alive = false;
    };
  }, [tab, tick, refresh]);

  const go = (x: ReviewTab) => setSp(x === 'validate' ? {} : { tab: x }, { replace: true });
  const onKey = (e: KeyboardEvent, i: number) => {
    const d = e.key === 'ArrowRight' ? 1 : e.key === 'ArrowLeft' ? -1 : 0;
    if (!d) return;
    e.preventDefault();
    const n = (i + d + REVIEW_TABS.length) % REVIEW_TABS.length;
    go(REVIEW_TABS[n]);
    tabs.current[n]?.focus();
  };
  const count = (x: ReviewTab) => (x === 'validate' ? inbox?.toValidate : x === 'mine' ? inbox?.mineOpen : undefined);
  const empty = { validate: t.review.emptyValidate, mine: t.review.emptyMine, decided: t.review.emptyDecided }[tab];

  return (
    <PageShell>
      <div className="page-h">
        <div className="ph-t">
          <h1>{t.review.title}</h1>
          <p>{t.review.intro}</p>
        </div>
      </div>
      <div className="tabs" role="tablist" aria-label={t.review.title}>
        {REVIEW_TABS.map((x, i) => (
          <button
            key={x}
            ref={(el) => void (tabs.current[i] = el)}
            type="button"
            role="tab"
            id={'tab-' + x}
            aria-selected={tab === x}
            aria-controls="review-panel"
            tabIndex={tab === x ? 0 : -1}
            onClick={() => go(x)}
            onKeyDown={(e) => onKey(e, i)}
          >
            {t.review.tabs[x]}
            {!!count(x) && <span className="cnt">{count(x)}</span>}
          </button>
        ))}
      </div>
      <div id="review-panel" role="tabpanel" aria-labelledby={'tab-' + tab} aria-busy={!items && !error}>
        {error && (
          <div className="banner danger" role="alert">
            <IconWarn />
            <div className="bt">
              <b>{error.status === 0 ? t.common.networkError : error.detail || t.common.genericError}</b>
              <div className="page-actions" style={{ marginTop: 10 }}>
                <button type="button" className="btn" onClick={() => setTick((x) => x + 1)}>
                  {t.common.retry}
                </button>
              </div>
            </div>
          </div>
        )}
        {!items && !error && (
          <ul className="plist">
            {Array.from({ length: 3 }, (_, i) => (
              <li key={i} className="pcard">
                <span className="sk title" style={{ width: '55%' }} />
                <span className="sk line" style={{ width: '35%', marginTop: 12 }} />
                <span className="sk line" style={{ width: '70%', marginTop: 10 }} />
              </li>
            ))}
          </ul>
        )}
        {items && items.length === 0 && (
          <div className="pempty">
            <IconReview size={22} />
            <p>{empty}</p>
          </div>
        )}
        {items && items.length > 0 && (
          <ul className="plist">
            {items.map((p) => (
              <ProposalCard key={p.id} p={p} tab={tab} />
            ))}
          </ul>
        )}
      </div>
    </PageShell>
  );
}
