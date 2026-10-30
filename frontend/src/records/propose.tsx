import { useEffect, useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import { api } from '../api/client';
import type { ChangeKind, Proposal } from '../api/types';
import { useI18n } from '../i18n';
import { useInbox } from '../review/inbox';
import { IconReview } from '../ui/icons';
import { describe, Field } from './common';

/** How the edit form sends its changes: published directly, or as a proposal (forced for non-owners). */
export type EditMode = 'publish' | 'propose';

export interface ProposeDraft {
  proposing: boolean;
  title: string;
  comment: string;
}

export function useProposeDraft(mode: EditMode, name: string) {
  const { t } = useI18n();
  return useState<ProposeDraft>(() => ({
    proposing: mode === 'propose',
    title: t.propose.titleDefault(name),
    comment: '',
  }));
}

export function validatePropose(t: ReturnType<typeof useI18n>['t'], p: ProposeDraft): Record<string, string> {
  if (!p.proposing) return {};
  const e: Record<string, string> = {};
  if (!p.title.trim()) e['p-title'] = t.record.errors.required;
  else if (p.title.length > 160) e['p-title'] = t.record.errors.tooLong(160);
  if (p.comment.length > 2000) e['p-comment'] = t.record.errors.tooLong(2000);
  return e;
}

/** Sends one update of a record as a proposal, submitted for validation straight away. */
export function proposeUpdate(
  kind: ChangeKind,
  id: string,
  baseVersion: number,
  after: object,
  p: ProposeDraft,
): Promise<Proposal> {
  return api.createProposal({
    title: p.title.trim(),
    comment: p.comment.trim() || null,
    submit: true,
    changes: [{ kind, targetId: id, operation: 'update', baseVersion, after: after as Record<string, unknown> }],
  });
}

/**
 * The proposal part of an edit form. Owners see an optional "Propose instead" box; other members always propose and
 * see who validates their change.
 */
export function ProposeFields({
  mode,
  ownerName,
  value,
  onChange,
  errors,
}: {
  mode: EditMode;
  ownerName: string | null;
  value: ProposeDraft;
  onChange: (p: ProposeDraft) => void;
  errors: Record<string, string>;
}) {
  const { t } = useI18n();
  const set = <K extends keyof ProposeDraft>(k: K, v: ProposeDraft[K]) => onChange({ ...value, [k]: v });
  return (
    <div className="propose">
      {mode === 'publish' ? (
        <div className="checkrow">
          <input
            type="checkbox"
            id="p-instead"
            aria-describedby="p-instead-hint"
            checked={value.proposing}
            onChange={(e) => set('proposing', e.target.checked)}
          />
          <span>
            <label htmlFor="p-instead">{t.propose.proposeInstead}</label>
            <small id="p-instead-hint">{t.propose.proposeInsteadHint}</small>
          </span>
        </div>
      ) : (
        <div className="banner info" role="note">
          <IconReview />
          <span className="bt">
            <b>{t.propose.suggesting}</b>
            {ownerName ? t.propose.ownerNote(ownerName) : t.propose.adminNote}
          </span>
        </div>
      )}
      {value.proposing && (
        <div className="fgrid">
          <Field id="p-title" label={t.propose.title} required error={errors['p-title']} full>
            <input
              className="input"
              {...describe('p-title', undefined, errors['p-title'])}
              value={value.title}
              onChange={(e) => set('title', e.target.value)}
              maxLength={170}
              autoComplete="off"
            />
          </Field>
          <Field id="p-comment" label={t.propose.comment} hint={t.propose.commentHint} error={errors['p-comment']} full>
            <textarea
              className="textarea"
              {...describe('p-comment', t.propose.commentHint, errors['p-comment'])}
              rows={3}
              value={value.comment}
              onChange={(e) => set('comment', e.target.value)}
            />
          </Field>
        </div>
      )}
    </div>
  );
}

/** Shown on the record after a proposal is sent. */
export function ProposalSentBanner({ proposal, onClose }: { proposal: Proposal; onClose: () => void }) {
  const { t } = useI18n();
  const { refresh } = useInbox();
  const ref = useRef<HTMLDivElement>(null);
  useEffect(() => {
    refresh();
    ref.current?.scrollIntoView?.({ block: 'center' });
    ref.current?.focus();
  }, [proposal.id, refresh]);
  return (
    <div className="banner ok" role="status" ref={ref} tabIndex={-1}>
      <IconReview />
      <div className="bt">
        <b>{t.propose.sent}</b>
        {t.propose.sentText}
        <div className="page-actions" style={{ marginTop: 10 }}>
          <Link className="btn" to={`/review/${encodeURIComponent(proposal.id)}`}>
            {t.propose.view}
          </Link>
          <button type="button" className="btn ghost" onClick={onClose}>
            {t.common.close}
          </button>
        </div>
      </div>
    </div>
  );
}
