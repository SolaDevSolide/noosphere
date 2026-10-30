import type { Proposal, ProposalStatus } from '../api/types';
import { useI18n } from '../i18n';
import { IconBot } from '../ui/icons';

const STATUS_CLASS: Record<ProposalStatus, string> = {
  draft: '',
  submitted: 'info',
  accepted: 'ok',
  refused: 'danger',
  withdrawn: '',
};

export function StatusChip({ status }: { status: ProposalStatus }) {
  const { t } = useI18n();
  return <span className={'pchip st ' + STATUS_CLASS[status]}>{t.review.status[status]}</span>;
}

/** Who wrote a proposal; agents get their own chip, with the person they act for. */
export function AuthorChip({ p }: { p: Pick<Proposal, 'author' | 'authorKind' | 'onBehalfOf' | 'agentName'> }) {
  const { t } = useI18n();
  const name = p.author?.displayName ?? t.history.system;
  if (p.authorKind === 'agent')
    return (
      <span className="pchip agent">
        <IconBot size={12} />
        {p.onBehalfOf ? t.review.agentFor(p.agentName ?? name, p.onBehalfOf.displayName) : name}
      </span>
    );
  return <span className="pchip">{t.review.by(name)}</span>;
}
