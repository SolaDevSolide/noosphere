import { useEffect, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { api, ApiError } from '../api/client';
import type { Application, RecordExtras, Team, TeamMember, User } from '../api/types';
import { isRestrictedApp, isRestrictedFlow } from '../api/types';
import { useAuth } from '../auth/auth';
import { useLandscape } from '../explorer/landscape';
import { FlowRow } from '../explorer/Panel';
import { paths } from '../explorer/paths';
import { useI18n } from '../i18n';
import { IconHistory, IconTeam } from '../ui/icons';
import { useToast } from '../ui/Toast';
import { PageShell, RecordError, RecordSkeleton, useRecord } from './common';
import { HistoryPanel } from './HistoryPanel';
import { RecordExtensions } from './RecordExtensions';

/** Administrators pick the members of a team among all users. */
function MembersEditor({
  teamId,
  members,
  onSaved,
  onCancel,
}: {
  teamId: string;
  members: TeamMember[];
  onSaved: (m: TeamMember[]) => void;
  onCancel: () => void;
}) {
  const { t, lang } = useI18n();
  const [users, setUsers] = useState<User[] | null>(null);
  const [picked, setPicked] = useState<Set<string>>(() => new Set(members.map((x) => x.username)));
  const [busy, setBusy] = useState(false);
  const [failure, setFailure] = useState<string | null>(null);
  useEffect(() => {
    api
      .users()
      .then(setUsers)
      .catch(() => setFailure(t.common.genericError));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);
  const toggle = (u: string) =>
    setPicked((s) => {
      const n = new Set(s);
      if (n.has(u)) n.delete(u);
      else n.add(u);
      return n;
    });
  const save = async () => {
    setBusy(true);
    setFailure(null);
    try {
      onSaved(await api.setTeamMembers(teamId, [...picked]));
    } catch (e) {
      setFailure(e instanceof ApiError ? e.detail || e.message : t.common.genericError);
      setBusy(false);
    }
  };
  return (
    <fieldset className="members-ed">
      <legend className="sr">{t.team.manage}</legend>
      {failure && (
        <div className="banner danger" role="alert">
          <span className="bt">{failure}</span>
        </div>
      )}
      {!users && !failure && (
        <div aria-busy="true">
          {Array.from({ length: 4 }, (_, i) => (
            <span key={i} className="sk line" style={{ width: '70%', margin: '10px 0' }} />
          ))}
        </div>
      )}
      {users &&
        [...users]
          .sort((a, b) => a.displayName.localeCompare(b.displayName, lang))
          .map((u) => (
            <div key={u.username} className="checkrow">
              <input
                type="checkbox"
                id={'mem-' + u.username}
                aria-describedby={'mem-' + u.username + '-d'}
                checked={picked.has(u.username)}
                onChange={() => toggle(u.username)}
              />
              <span>
                <label htmlFor={'mem-' + u.username}>{u.displayName}</label>
                <small id={'mem-' + u.username + '-d'}>
                  {u.username} · {t.top.role[u.role]}
                </small>
              </span>
            </div>
          ))}
      <div className="form-actions">
        <button type="button" className="btn" onClick={onCancel} disabled={busy}>
          {t.common.cancel}
        </button>
        <button type="button" className="btn primary" onClick={() => void save()} disabled={busy || !users}>
          {busy ? t.common.saving : t.common.save}
        </button>
      </div>
    </fieldset>
  );
}

/** A team: what it owns, and who belongs to it. Administrators manage the members here. */
export function TeamRecord() {
  const { id = '' } = useParams();
  const { t } = useI18n();
  const { isAdmin } = useAuth();
  const { model: m } = useLandscape();
  const toast = useToast();
  const { data: team, error, reload } = useRecord<Team & RecordExtras>(api.team, id);
  const [members, setMembers] = useState<TeamMember[] | null>(null);
  const [membersError, setMembersError] = useState(false);
  const [editing, setEditing] = useState(false);
  const [historyOpen, setHistoryOpen] = useState(false);

  useEffect(() => {
    let alive = true;
    setMembers(null);
    setMembersError(false);
    api
      .teamMembers(id)
      .then((x) => alive && setMembers(x))
      .catch(() => alive && setMembersError(true));
    return () => {
      alive = false;
    };
  }, [id]);

  if (error)
    return (
      <PageShell>
        <RecordError error={error} onRetry={reload} />
      </PageShell>
    );
  if (!team || !m)
    return (
      <PageShell>
        <RecordSkeleton />
      </PageShell>
    );

  const apps = m.appIds
    .map((x) => m.apps.get(x)!)
    .filter((a): a is Application => !isRestrictedApp(a) && a.teamId === id);
  const flows = m.flows.filter((f) => !isRestrictedFlow(f) && f.ownerTeamId === id);

  return (
    <PageShell title={team.name}>
      <div className="page-h">
        <div className="ph-t">
          <span className="kb team">{t.search.badge.team}</span>
          <h1>{team.name}</h1>
          {team.contact && <p>{team.contact}</p>}
        </div>
        <div className="page-actions">
          <Link className="btn" to={paths.team(team.id)}>
            {t.team.showOnMap}
          </Link>
          <button
            type="button"
            className="btn hist-btn"
            aria-pressed={historyOpen}
            aria-haspopup="dialog"
            onClick={() => setHistoryOpen(true)}
          >
            <IconHistory />
            {t.history.button}
          </button>
        </div>
      </div>
      {historyOpen && (
        <HistoryPanel
          kind="team"
          id={team.id}
          name={team.name}
          canPublish={team.permissions?.publish ?? isAdmin}
          canPropose={false}
          onClose={() => setHistoryOpen(false)}
          onRestored={reload}
        />
      )}
      <div className="grid2">
        <div>
          {team.description && (
            <section className="box" aria-labelledby="tdesc-h">
              <h2 id="tdesc-h">{t.record.fields.description}</h2>
              <p style={{ margin: 0 }}>{team.description}</p>
            </section>
          )}
          <RecordExtensions kind="team" record={team} />
          <section className="box" aria-labelledby="tapps-h">
            <h2 id="tapps-h">{t.panel.teamApps(apps.length)}</h2>
            {apps.length ? (
              <ul className="tlinks">
                {apps.map((a) => (
                  <li key={a.id}>
                    <Link to={paths.app(a.id)}>{a.name}</Link>
                    <span className="muted"> · {t.kindName[a.kind]}</span>
                  </li>
                ))}
              </ul>
            ) : (
              <p className="muted">{t.team.noApps}</p>
            )}
            <h2 style={{ marginTop: 18 }}>{t.panel.teamFlows(flows.length)}</h2>
            <div className="rows">
              {flows.length ? (
                flows.map((f) => <FlowRow key={f.id} f={f} />)
              ) : (
                <p className="muted">{t.record.noFlows}</p>
              )}
            </div>
          </section>
        </div>
        <section className="box" aria-labelledby="tmem-h" aria-busy={!members && !membersError}>
          <div className="box-h">
            <h2 id="tmem-h">
              <IconTeam size={14} /> {t.team.members(members?.length ?? 0)}
            </h2>
            {isAdmin && members && !editing && (
              <button type="button" className="btn sm" onClick={() => setEditing(true)}>
                {t.team.manage}
              </button>
            )}
          </div>
          <p className="muted small">{t.team.membersHint}</p>
          {membersError && <p className="muted">{t.common.genericError}</p>}
          {!members && !membersError && (
            <div>
              {Array.from({ length: 3 }, (_, i) => (
                <span key={i} className="sk line" style={{ width: '60%', margin: '12px 0' }} />
              ))}
            </div>
          )}
          {members && editing && (
            <MembersEditor
              teamId={team.id}
              members={members}
              onCancel={() => setEditing(false)}
              onSaved={(x) => {
                setMembers(x);
                setEditing(false);
                toast(t.team.saved);
              }}
            />
          )}
          {members &&
            !editing &&
            (members.length ? (
              <ul className="mlist">
                {members.map((x) => (
                  <li key={x.username}>
                    <b>{x.displayName}</b>
                    <small className="muted">
                      {x.username}
                      {x.role && <> · {t.top.role[x.role]}</>}
                    </small>
                  </li>
                ))}
              </ul>
            ) : (
              <p className="muted">{t.team.noMembers}</p>
            ))}
        </section>
      </div>
    </PageShell>
  );
}
