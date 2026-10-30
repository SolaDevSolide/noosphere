import { useState, type FormEvent } from 'react';
import { Link, useParams } from 'react-router-dom';
import { api, ApiError } from '../api/client';
import type { Proposal, ProposalChange, RecordKind } from '../api/types';
import { useDocumentTitle } from '../app/settings';
import { useLandscape } from '../explorer/landscape';
import { paths } from '../explorer/paths';
import { useI18n } from '../i18n';
import type { Dict } from '../i18n/en';
import { describe, Field, PageShell, RecordError, RecordSkeleton, useRecord } from '../records/common';
import { IconCheck, IconWarn } from '../ui/icons';
import { Markdown } from '../ui/Markdown';
import { useToast } from '../ui/Toast';
import { AuthorChip, StatusChip } from './bits';
import { DiffView } from './diff';
import { useInbox } from './inbox';

export const REASON_MIN = 3;
export const REASON_MAX = 500;

/** The refusal reason is required: 3 to 500 characters once trimmed. */
export function reasonError(t: Dict, reason: string): string | null {
  const r = reason.trim();
  if (!r) return t.review.reasonRequired;
  if (r.length < REASON_MIN || r.length > REASON_MAX) return t.review.reasonLength(REASON_MIN, REASON_MAX);
  return null;
}

const recordPath = (kind: RecordKind, id: string) =>
  kind === 'application'
    ? paths.app(id)
    : kind === 'flow'
      ? paths.flow(id)
      : kind === 'dataObject'
        ? paths.lineage(id)
        : null;

function ChangeBox({ c, n, total }: { c: ProposalChange; n: number; total: number }) {
  const { t } = useI18n();
  const { model: m } = useLandscape();
  const target = c.targetTitle || c.targetId || t.review.newRecord;
  const to = c.targetId && c.operation === 'update' ? recordPath(c.kind, c.targetId) : null;
  const diff = c.diff ?? [];
  return (
    <section className="box change" aria-labelledby={'ch-' + n}>
      <div className="change-h">
        <h2 id={'ch-' + n} className="big">
          {total > 1 && (
            <span className="muted">
              {n + 1}/{total} ·{' '}
            </span>
          )}
          {t.review.kind[c.kind]} · {target}
        </h2>
        <span className={'pchip ' + (c.operation === 'delete' ? 'danger' : c.operation === 'create' ? 'ok' : '')}>
          {t.review.operation[c.operation]}
        </span>
        {to && (
          <Link className="btn sm" to={to}>
            {t.review.openRecord}
          </Link>
        )}
      </div>
      {c.operation !== 'create' && (
        <p className="change-v">
          {c.outdated ? (
            <span className="pchip warn">
              <IconWarn size={12} />
              {t.review.changedSince(c.baseVersion ?? 0, c.currentVersion ?? 0)}
            </span>
          ) : (
            c.baseVersion != null && <span className="pchip ok">{t.review.basedOn(c.baseVersion)}</span>
          )}
        </p>
      )}
      {c.operation === 'delete' ? (
        <div className="banner danger" role="note">
          <span className="bt">{t.review.deleteText}</span>
        </div>
      ) : (
        <DiffView
          diff={diff}
          m={m}
          leftTitle={c.operation === 'create' ? t.review.nothingYet : t.review.published(c.baseVersion ?? 0)}
          rightTitle={c.operation === 'create' ? t.review.proposedNew : t.review.proposed}
          conflicts={c.conflicts}
          afterOnly={c.operation === 'create'}
        />
      )}
    </section>
  );
}

/** One proposal: the field comparison of each change, its reason, and the decision or author actions. */
export function ProposalPage() {
  const { id = '' } = useParams();
  const { t, date } = useI18n();
  const toast = useToast();
  const { refresh: refreshInbox } = useInbox();
  const { reload: reloadLandscape } = useLandscape();
  const { data: p, setData, error, reload } = useRecord<Proposal>(api.proposal, id);
  const [reason, setReason] = useState('');
  const [reasonErr, setReasonErr] = useState<string | null>(null);
  const [busy, setBusy] = useState<null | 'accept' | 'refuse' | 'withdraw' | 'rebase' | 'submit'>(null);
  const [failure, setFailure] = useState<string | null>(null);
  const [staleOnAccept, setStaleOnAccept] = useState(false);
  useDocumentTitle(p?.title ?? t.review.title);

  if (error)
    return (
      <PageShell>
        <RecordError error={error} onRetry={reload} />
      </PageShell>
    );
  if (!p)
    return (
      <PageShell>
        <RecordSkeleton />
      </PageShell>
    );

  const run = async (kind: NonNullable<typeof busy>, call: () => Promise<Proposal>, done: string) => {
    setBusy(kind);
    setFailure(null);
    try {
      const next = await call();
      setData(next);
      toast(done);
      refreshInbox();
      if (kind === 'accept') reloadLandscape();
      if (kind === 'rebase') setStaleOnAccept(false);
    } catch (e) {
      if (e instanceof ApiError && e.isConflict && kind === 'accept') {
        setStaleOnAccept(true);
        reload();
      } else if (e instanceof ApiError && e.fieldErrors.reason) setReasonErr(e.fieldErrors.reason.join(' '));
      else
        setFailure(
          e instanceof ApiError
            ? e.status === 0
              ? t.common.networkError
              : e.detail || e.message
            : t.common.genericError,
        );
    } finally {
      setBusy(null);
    }
  };

  const refuse = (e: FormEvent) => {
    e.preventDefault();
    const err = reasonError(t, reason);
    setReasonErr(err);
    if (err) {
      document.getElementById('refuse-reason')?.focus();
      return;
    }
    void run('refuse', () => api.refuseProposal(p.id, reason.trim()), t.review.refusedToast);
  };
  const withdraw = () => {
    if (!window.confirm(t.review.withdrawConfirm)) return;
    void run('withdraw', () => api.withdrawProposal(p.id), t.review.withdrawnToast);
  };

  const open = p.status === 'submitted' || p.status === 'draft';
  const outdated = open && (p.outdated || staleOnAccept);
  const when = p.submittedAt ?? p.createdAt;

  return (
    <PageShell>
      <nav className="crumbline" aria-label={t.review.breadcrumb}>
        <Link to="/review">{t.review.title}</Link>
        <span aria-hidden="true">/</span>
        <span aria-current="page">{t.review.status[p.status]}</span>
      </nav>
      <div className="page-h">
        <div className="ph-t">
          <h1>{p.title}</h1>
          <div className="pchips" style={{ marginTop: 8 }}>
            <StatusChip status={p.status} />
            <AuthorChip p={p} />
            <span className="pchip">
              {p.submittedAt ? t.review.submittedOn(date(when)) : t.review.createdOn(date(when))}
            </span>
          </div>
        </div>
      </div>

      {failure && (
        <div className="banner danger" role="alert">
          <IconWarn />
          <span className="bt">{failure}</span>
        </div>
      )}
      {p.status === 'refused' && (
        <div className="banner danger" role="note">
          <div className="bt">
            <b>{t.review.refusedBy(p.decidedBy?.displayName ?? '—', date(p.decidedAt))}</b>
            {p.refusalReason}
          </div>
        </div>
      )}
      {p.status === 'accepted' && (
        <div className="banner ok" role="note">
          <IconCheck />
          <span className="bt">
            <b>{t.review.acceptedBy(p.decidedBy?.displayName ?? '—', date(p.decidedAt))}</b>
          </span>
        </div>
      )}
      {p.status === 'withdrawn' && (
        <div className="banner" role="note">
          <span className="bt">{t.review.withdrawnText}</span>
        </div>
      )}
      {outdated && (
        <div className="banner warn" role={staleOnAccept ? 'alert' : 'note'}>
          <IconWarn />
          <div className="bt">
            <b>{t.review.outdatedTitle}</b>
            {p.canEdit ? t.review.outdatedAuthor : t.review.outdatedValidator}
            {p.canEdit && (
              <div className="page-actions" style={{ marginTop: 10 }}>
                <button
                  type="button"
                  className="btn primary"
                  disabled={!!busy}
                  onClick={() => void run('rebase', () => api.rebaseProposal(p.id), t.review.rebasedToast)}
                >
                  {busy === 'rebase' ? t.common.saving : t.review.updateFromLatest}
                </button>
              </div>
            )}
          </div>
        </div>
      )}
      {open && p.changes.some((c) => c.conflicts?.length) && (
        <div className="banner warn" role="note">
          <IconWarn />
          <span className="bt">
            <b>{t.review.conflictsTitle}</b>
            {t.review.conflictsText}
          </span>
        </div>
      )}

      {p.changes.map((c, i) => (
        <ChangeBox key={i} c={c} n={i} total={p.changes.length} />
      ))}

      <section className="box" aria-labelledby="why-h">
        <h2 id="why-h">{t.review.why}</h2>
        {p.comment ? <Markdown text={p.comment} /> : <p className="muted">{t.review.noComment}</p>}
        {!!p.ownerTeams?.length && (
          <p className="owners">
            <span className="muted">{t.review.validatedBy}</span>{' '}
            {p.ownerTeams.map((o) => (
              <span key={o.id} className={'pchip' + (o.approvedBy ? ' ok' : '')}>
                {o.approvedBy && <IconCheck size={12} />}
                {o.name}
              </span>
            ))}
            {' · '}
            <span className="muted">{t.review.orAdmin}</span>
          </p>
        )}
        {p.requiresAdmin && <p className="owners muted">{t.review.adminOnly}</p>}
      </section>

      {p.canDecide && p.status === 'submitted' && (
        <form className="box decide" onSubmit={refuse} noValidate aria-labelledby="decide-h">
          <h2 id="decide-h">{t.review.decide}</h2>
          <Field
            id="refuse-reason"
            label={t.review.reasonLabel}
            hint={t.review.reasonHint}
            error={reasonErr ?? undefined}
          >
            <textarea
              className="textarea"
              {...describe('refuse-reason', t.review.reasonHint, reasonErr ?? undefined)}
              rows={2}
              maxLength={REASON_MAX + 20}
              value={reason}
              onChange={(e) => {
                setReason(e.target.value);
                if (reasonErr) setReasonErr(null);
              }}
            />
          </Field>
          <div className="form-actions">
            <button type="submit" className="btn danger" disabled={!!busy}>
              {busy === 'refuse' ? t.common.saving : t.review.refuse}
            </button>
            <button
              type="button"
              className="btn primary"
              disabled={!!busy || outdated}
              title={outdated ? t.review.outdatedTitle : undefined}
              onClick={() => void run('accept', () => api.acceptProposal(p.id), t.review.acceptedToast)}
            >
              <IconCheck />
              {busy === 'accept' ? t.common.saving : t.review.accept}
            </button>
          </div>
        </form>
      )}

      {p.canEdit && open && (
        <div className="page-actions author-acts">
          {p.status === 'draft' && (
            <button
              type="button"
              className="btn primary"
              disabled={!!busy}
              onClick={() => void run('submit', () => api.submitProposal(p.id), t.review.submittedToast)}
            >
              {t.review.submit}
            </button>
          )}
          <button type="button" className="btn danger" disabled={!!busy} onClick={withdraw}>
            {t.review.withdraw}
          </button>
        </div>
      )}
    </PageShell>
  );
}
