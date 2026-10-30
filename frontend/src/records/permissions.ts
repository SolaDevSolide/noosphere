import type { Application, Flow, Permissions, RecordExtras, Role } from '../api/types';
import { isRestrictedApp } from '../api/types';
import type { Model } from '../domain/model';

/** The owner team of a record, following the contract when the server does not say it. */
export function ownerTeamOf(
  m: Model | null,
  kind: 'application' | 'flow',
  rec: (Application | Flow) & RecordExtras,
): string | null {
  if (rec.owningTeamId !== undefined) return rec.owningTeamId ?? null;
  if (kind === 'application') return (rec as Application).teamId ?? null;
  const f = rec as Flow;
  if (f.ownerTeamId) return f.ownerTeamId;
  const from = m?.apps.get(f.fromId);
  return from && !isRestrictedApp(from) ? (from.teamId ?? null) : null;
}

/** What the user may do on a record: the server's answer, or the same rule computed here. */
export function permissionsFor(
  role: Role | undefined,
  teamIds: string[],
  owner: string | null,
  fromServer?: Permissions,
): Permissions {
  if (fromServer) return fromServer;
  if (role === 'admin') return { publish: true, propose: true };
  if (role !== 'member') return { publish: false, propose: false };
  return { publish: !!owner && teamIds.includes(owner), propose: true };
}
