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

/** Who wrote a proposal; an agent's proposal shows the person it acts for, then the agent's name. */
export function AuthorChip({ p }: { p: Pick<Proposal, 'author' | 'authorKind' | 'onBehalfOf' | 'agentName'> }) {
  const { t } = useI18n();
  const name = p.author?.displayName ?? t.history.system;
  if (p.authorKind === 'agent')
    return (
      <>
        <span className="pchip agent">
          <IconBot size={12} />
          {t.agents.review.agentFor(p.onBehalfOf?.displayName ?? name)}
        </span>
        {p.agentName && <span className="pchip">{p.agentName}</span>}
      </>
    );
  return <span className="pchip">{t.review.by(name)}</span>;
}
