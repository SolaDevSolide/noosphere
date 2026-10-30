import { useEffect, useMemo, useState, type FormEvent, type KeyboardEvent } from 'react';
import { api, ApiError } from '../api/client';
import type {
  AgentActivityKind,
  AgentCapabilities,
  AgentCeiling,
  AgentConnection,
  AgentSettings,
  AgentWhoConnects,
  StopAllResult,
} from '../api/types';
import { useAuth } from '../auth/auth';
import { useDemo } from '../demo/context';
import { useI18n } from '../i18n';
import { PageShell } from '../records/common';
import { IconBotFull, IconClock, IconEdit, IconStop, IconWarn } from '../ui/icons';
import { useToast } from '../ui/Toast';
import {
  ActivityLine,
  ActivitySkeleton,
  CapIcon,
  capClass,
  clientHost,
  clock,
  localized,
  relTime,
  scopeNames,
  shortDate,
  ScopeChips,
  StateChip,
} from './bits';
import { useActivity } from './MyAgentsPage';

export const KINDS: (AgentActivityKind | 'all')[] = ['all', 'read', 'draft', 'proposal', 'refused', 'quota', 'revoked'];
export const CEILINGS: AgentCeiling[] = ['public', 'public+protected', 'asUser'];
export const QUOTA_MIN = 1;
export const QUOTA_MAX = 100000;

/** The quota typed in the settings, or null when it is not a whole number in range. */
export function parseQuota(raw: string): number | null {
  if (!/^\d+$/.test(raw.trim())) return null;
  const n = Number(raw.trim());
  return n >= QUOTA_MIN && n <= QUOTA_MAX ? n : null;
}

const hostOf = (url: string) => url.replace(/^https?:\/\//, '');

function Switch({ label, checked, onChange }: { label: string; checked: boolean; onChange: (v: boolean) => void }) {
  const { t } = useI18n();
  const key = (e: KeyboardEvent) => {
    if (e.key === ' ' || e.key === 'Enter') {
      e.preventDefault();
      onChange(!checked);
    }
  };
  return (
    <span
      className="switch"
      role="switch"
      aria-checked={checked}
      aria-label={label}
      tabIndex={0}
      onClick={() => onChange(!checked)}
      onKeyDown={key}
    >
      <span className="tr" />
      <span className="sl">{checked ? t.agents.console.on : t.agents.console.off}</span>
    </span>
  );
}

function SettingsBox({ s, onSaved }: { s: AgentSettings; onSaved: (s: AgentSettings) => void }) {
  const { t } = useI18n();
  const toast = useToast();
  const c = t.agents.console;
  const [editing, setEditing] = useState(false);
  const [form, setForm] = useState(s);
  const [quota, setQuota] = useState(String(s.quotaPerHour));
  const [quotaErr, setQuotaErr] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [conflict, setConflict] = useState(false);

  const start = () => {
    setForm(s);
    setQuota(String(s.quotaPerHour));
    setQuotaErr(null);
    setConflict(false);
    setEditing(true);
  };
  const save = async (e: FormEvent) => {
    e.preventDefault();
    const q = parseQuota(quota);
    if (q === null) {
      setQuotaErr(c.quotaError(QUOTA_MIN, QUOTA_MAX));
      document.getElementById('quota')?.focus();
      return;
    }
    setBusy(true);
    try {
      const next = await api.updateAgentSettings({
        enabled: form.enabled,
        ceiling: form.ceiling,
        quotaPerHour: q,
        whoConnects: form.whoConnects,
        dynamicRegistration: form.dynamicRegistration,
        anonymousLlms: form.anonymousLlms,
        version: s.version,
      });
      onSaved(next);
      setEditing(false);
      toast(c.saved);
    } catch (err) {
      if (err instanceof ApiError && err.isConflict) setConflict(true);
      else if (err instanceof ApiError && err.fieldErrors.quotaPerHour) setQuotaErr(c.quotaError(QUOTA_MIN, QUOTA_MAX));
      else toast(err instanceof ApiError && err.detail ? err.detail : t.common.genericError);
    } finally {
      setBusy(false);
    }
  };

  if (!editing)
    return (
      <section className="box setbox" aria-labelledby="set-h">
        <div className="set-hd">
          <h2 id="set-h">{c.settingsTitle}</h2>
          <button type="button" className="btn sm" onClick={start}>
            <IconEdit size={14} />
            {c.change}
          </button>
        </div>
        <dl className="setgrid">
          <div>
            <dt>{c.setMcp}</dt>
            <dd>
              {s.enabled ? (
                <span className="pchip ok">
                  <span className="st-dot" />
                  {c.on}
                </span>
              ) : (
                <span className="pchip danger">{c.off}</span>
              )}{' '}
              <span className="mono">{s.mcpUrl}</span>
            </dd>
          </div>
          <div>
            <dt>{c.setCeiling}</dt>
            <dd>{t.agents.ceilingText[s.ceiling]}</dd>
          </div>
          <div>
            <dt>{c.setQuota}</dt>
            <dd>{c.quotaView(s.quotaPerHour)}</dd>
          </div>
          <div>
            <dt>{c.setWho}</dt>
            <dd>{s.whoConnects === 'approvalRequired' ? c.whoApprovalLong : c.whoAny}</dd>
          </div>
          <div>
            <dt>{c.setRegistration}</dt>
            <dd>{s.dynamicRegistration ? c.registrationOn : c.registrationOff}</dd>
          </div>
          <div>
            <dt>{c.setLlms}</dt>
            <dd>{s.anonymousLlms ? c.llmsOn : c.llmsOff}</dd>
          </div>
        </dl>
      </section>
    );

  const set = <K extends keyof AgentSettings>(k: K, v: AgentSettings[K]) => setForm((f) => ({ ...f, [k]: v }));
  return (
    <section className="box setbox editing" aria-labelledby="set-h">
      <form onSubmit={(e) => void save(e)} noValidate>
        <div className="set-hd">
          <h2 id="set-h">{c.settingsTitle}</h2>
          <span className="muted2">{c.editHint}</span>
        </div>
        {conflict && (
          <div className="banner warn" role="alert">
            <IconWarn />
            <div className="bt">
              <b>{c.conflict}</b>
              <div className="page-actions" style={{ marginTop: 10 }}>
                <button
                  type="button"
                  className="btn primary"
                  onClick={() =>
                    void api.agentSettings().then((n) => {
                      onSaved(n);
                      setForm(n);
                      setQuota(String(n.quotaPerHour));
                      setConflict(false);
                    })
                  }
                >
                  {c.reload}
                </button>
              </div>
            </div>
          </div>
        )}
        <div className="ecols">
          <div>
            <div className="swrow">
              <b>{c.setMcp}</b>
              <small>{c.mcpHint}</small>
              <Switch label={c.setMcp} checked={form.enabled} onChange={(v) => set('enabled', v)} />
            </div>
            <div className="sfield">
              <span className="lbl2" id="ceil-l">
                {c.setCeiling}
              </span>
              <div className="rcards" role="radiogroup" aria-labelledby="ceil-l">
                {CEILINGS.map((x) => (
                  <label key={x} className="rcard">
                    <input
                      type="radio"
                      name="ceiling"
                      checked={form.ceiling === x}
                      onChange={() => set('ceiling', x)}
                    />
                    <b>{c.ceilings[x][0]}</b>
                    <small>{c.ceilings[x][1]}</small>
                  </label>
                ))}
              </div>
            </div>
          </div>
          <div>
            <div className="sfield first">
              <label className="lbl2" htmlFor="quota">
                {c.setQuota}
              </label>
              <div className="qinput">
                <input
                  className="input"
                  id="quota"
                  inputMode="numeric"
                  value={quota}
                  aria-invalid={quotaErr ? true : undefined}
                  aria-describedby={quotaErr ? 'quota-hint quota-err' : 'quota-hint'}
                  onChange={(e) => {
                    setQuota(e.target.value);
                    setQuotaErr(null);
                  }}
                />
                <span>{c.quotaUnit}</span>
              </div>
              <small id="quota-hint">{c.quotaHint}</small>
              {quotaErr && (
                <span className="err" id="quota-err" role="alert">
                  <IconWarn size={13} />
                  {quotaErr}
                </span>
              )}
            </div>
            <div className="sfield">
              <span className="lbl2" id="who-l">
                {c.setWho}
              </span>
              <div className="seg sm wrap2" role="group" aria-labelledby="who-l">
                {(['anyMember', 'approvalRequired'] as AgentWhoConnects[]).map((w) => (
                  <button
                    key={w}
                    type="button"
                    aria-pressed={form.whoConnects === w}
                    onClick={() => set('whoConnects', w)}
                  >
                    {w === 'anyMember' ? c.whoAny : c.whoApproval}
                  </button>
                ))}
              </div>
            </div>
            <div className="sfield">
              <div className="swrow">
                <b>{c.registration}</b>
                <small>{c.registrationHint}</small>
                <Switch
                  label={c.registration}
                  checked={form.dynamicRegistration}
                  onChange={(v) => set('dynamicRegistration', v)}
                />
              </div>
              <div className="swrow">
                <b>{c.llms}</b>
                <small>{c.llmsHint}</small>
                <Switch label={c.llms} checked={form.anonymousLlms} onChange={(v) => set('anonymousLlms', v)} />
              </div>
            </div>
          </div>
        </div>
        <div className="set-f">
          <button type="button" className="btn" onClick={() => setEditing(false)}>
            {c.cancel}
          </button>
          <button type="submit" className="btn primary" disabled={busy || conflict}>
            {busy ? t.common.saving : c.save}
          </button>
        </div>
      </form>
    </section>
  );
}

function CapBox({ conn }: { conn: AgentConnection | null }) {
  const { t, lang } = useI18n();
  const c = t.agents.console;
  const [cap, setCap] = useState<AgentCapabilities | null>(null);
  const [failed, setFailed] = useState(false);
  useEffect(() => {
    if (!conn) return;
    let alive = true;
    setCap(null);
    setFailed(false);
    api
      .agentCapabilities(conn.id)
      .then((x) => alive && setCap(x))
      .catch(() => alive && setFailed(true));
    return () => {
      alive = false;
    };
  }, [conn]);
  return (
    <section className="box" aria-labelledby="cap-h">
      <h2 id="cap-h">{c.capTitle}</h2>
      {!conn ? (
        <p className="muted2">{c.capPick}</p>
      ) : (
        <>
          <div className="cap-h">
            <span className="atile" aria-hidden="true">
              <IconBotFull size={18} />
            </span>
            <span>
              <b>{conn.clientName}</b>
              <small>
                {conn.state === 'pending'
                  ? `${t.agents.forPerson(conn.person.displayName)} · ${t.agents.state.pending}`
                  : c.capFor(conn.person.displayName, scopeNames(t, conn.scopes).join(', '))}
              </small>
            </span>
          </div>
          {failed ? (
            <p className="muted2">{t.common.genericError}</p>
          ) : !cap ? (
            <div className="can-sk" aria-busy="true">
              {[80, 70, 75, 60].map((w) => (
                <span key={w} className="sk line" style={{ width: `${w}%` }} />
              ))}
            </div>
          ) : (
            <ul className="can">
              {conn.state === 'pending' && (
                <li>
                  <CapIcon cls="" />
                  <span>{c.waitingNote}</span>
                </li>
              )}
              {cap.items.map((x) => {
                const cls = capClass(x);
                const extra =
                  x.key === 'quota'
                    ? conn.state === 'paused' && conn.pausedUntil
                      ? c.pausedUntil(clock(conn.pausedUntil, lang))
                      : c.usedThisHour(conn.callsLastHour)
                    : null;
                return (
                  <li key={x.key} className={cls}>
                    <CapIcon cls={cls} />
                    <span>
                      {localized(x.text, lang)}
                      {extra && <small>{extra}</small>}
                    </span>
                  </li>
                );
              })}
            </ul>
          )}
        </>
      )}
    </section>
  );
}

type StopPhase = null | 'confirm' | 'busy' | StopAllResult;

function StopBar({ conns, onStopped }: { conns: AgentConnection[]; onStopped: () => void }) {
  const { t, lang } = useI18n();
  const c = t.agents.console;
  const [phase, setPhase] = useState<StopPhase>(null);
  const live = conns.filter((x) => x.state !== 'revoked');
  const pending = live.filter((x) => x.state === 'pending').length;
  const names = live
    .filter((x) => x.state !== 'pending')
    .map((x) => `${x.clientName} (${x.person.displayName})`)
    .join(', ');
  const stop = async () => {
    setPhase('busy');
    try {
      setPhase(await api.stopAllAgents());
      onStopped();
    } catch {
      setPhase('confirm');
    }
  };
  const done = phase && typeof phase === 'object';
  return (
    <div
      className={'stopbar' + (phase === 'confirm' || phase === 'busy' ? ' confirm' : done ? ' done' : '')}
      role="region"
      aria-label={c.stopLead.replace(/\.$/, '')}
    >
      <IconStop size={20} />
      {phase === null && (
        <>
          <span className="st-t">
            <b>{c.stopLead}</b>
            {c.stopText}
          </span>
          <span className="st-a">
            <button type="button" className="btn sm danger" onClick={() => setPhase('confirm')}>
              {c.stopAll}
            </button>
          </span>
        </>
      )}
      {(phase === 'confirm' || phase === 'busy') && (
        <>
          <span className="st-t" role="alert">
            <b>{c.stopConfirm(live.length)}</b>
            {live.length ? c.stopList(names || '—', pending) : c.stopNone}
          </span>
          <span className="st-a">
            <button
              type="button"
              className="btn sm danger-solid"
              disabled={phase === 'busy'}
              onClick={() => void stop()}
            >
              {phase === 'busy' ? t.common.saving : c.stopYes}
            </button>
            <button type="button" className="btn sm" onClick={() => setPhase(null)}>
              {c.cancel}
            </button>
          </span>
        </>
      )}
      {done && (
        <>
          <span className="st-t" role="status">
            <b>{c.stopDone(clock(phase.at, lang))}</b>
            {c.stopDoneText(phase.revokedConnections)}
          </span>
          <span className="st-a">
            <button type="button" className="btn sm" onClick={() => setPhase(null)}>
              {c.done}
            </button>
          </span>
        </>
      )}
    </div>
  );
}

function ConsoleSkeleton() {
  return (
    <div aria-busy="true">
      <span className="sk block" style={{ height: 54, marginBottom: 16 }} />
      <section className="box">
        <span className="sk line" style={{ width: 140 }} />
        <span className="sk line" style={{ width: '45%', marginTop: 10 }} />
        <span className="sk block" style={{ height: 150, marginTop: 16 }} />
      </section>
      <div className="cgrid">
        <div>
          <section className="box">
            <span className="sk line" style={{ width: 90 }} />
            <span className="sk block" style={{ height: 260, marginTop: 16 }} />
          </section>
        </div>
        <div>
          <section className="box">
            <span className="sk line" style={{ width: 150 }} />
            <span className="sk block" style={{ height: 200, marginTop: 16 }} />
          </section>
        </div>
      </div>
    </div>
  );
}

/** The AI agents console, for administrators: stop, connections, activity, capabilities and settings. */
export function ConsolePage() {
  const { t, lang } = useI18n();
  const { isAdmin } = useAuth();
  const { tick } = useDemo();
  const toast = useToast();
  const c = t.agents.console;
  const [conns, setConns] = useState<AgentConnection[] | null>(null);
  const [settings, setSettings] = useState<AgentSettings | null>(null);
  const [error, setError] = useState(false);
  const [reload, setReload] = useState(0);
  const [sel, setSel] = useState<string | null>(null);
  const [kind, setKind] = useState<AgentActivityKind | 'all'>('all');
  const [agent, setAgent] = useState('');
  const [showRevoked, setShowRevoked] = useState(false);
  const [busy, setBusy] = useState<string | null>(null);
  const activity = useActivity(agent || undefined, kind === 'all' ? undefined : kind, `${tick}-${reload}`);

  useEffect(() => {
    if (!isAdmin) return;
    let alive = true;
    setError(false);
    Promise.all([api.agents(), api.agentSettings()])
      .then(([a, s]) => {
        if (!alive) return;
        setConns(a);
        setSettings(s);
      })
      .catch(() => alive && setError(true));
    return () => {
      alive = false;
    };
  }, [tick, reload, isAdmin]);

  const live = useMemo(() => conns?.filter((x) => x.state !== 'revoked') ?? [], [conns]);
  const gone = conns?.filter((x) => x.state === 'revoked') ?? [];
  const shown = showRevoked ? (conns ?? []) : live;
  const selected = conns?.find((x) => x.id === sel) ?? live.find((x) => x.state === 'pending') ?? live[0] ?? null;

  if (!isAdmin)
    return (
      <PageShell title={c.title}>
        <div className="banner danger" role="alert">
          <IconWarn />
          <span className="bt">{t.common.noAccess}</span>
        </div>
      </PageShell>
    );

  const act = async (x: AgentConnection, call: () => Promise<AgentConnection>, msg: string) => {
    setBusy(x.id);
    try {
      const n = await call();
      setConns((cs) => cs?.map((y) => (y.id === n.id ? n : y)) ?? cs);
      toast(msg);
      setReload((r) => r + 1);
    } catch (e) {
      toast(e instanceof ApiError && e.detail ? e.detail : t.common.genericError);
    } finally {
      setBusy(null);
    }
  };
  const people = new Set(live.map((x) => x.person.username)).size;
  const pending = live.filter((x) => x.state === 'pending').length;

  return (
    <PageShell wide title={c.title}>
      <div className="page-h">
        <div className="ph-t">
          <h1>{c.title}</h1>
          <p>{c.intro}</p>
        </div>
        {settings && (
          <div className="page-actions">
            <span className="mcp-on">
              {settings.enabled ? (
                <span className="pchip ok">
                  <span className="st-dot" />
                  {c.mcpOn}
                </span>
              ) : (
                <span className="pchip danger">{c.mcpOff}</span>
              )}
              <span className="mono">{hostOf(settings.mcpUrl)}</span>
            </span>
          </div>
        )}
      </div>
      {error && (
        <div className="banner danger" role="alert">
          <IconWarn />
          <div className="bt">
            <b>{t.agents.loadError}</b>
            <div className="page-actions" style={{ marginTop: 10 }}>
              <button type="button" className="btn" onClick={() => setReload((x) => x + 1)}>
                {t.common.retry}
              </button>
            </div>
          </div>
        </div>
      )}
      {!error && (!conns || !settings) && <ConsoleSkeleton />}
      {conns && settings && (
        <>
          <StopBar conns={conns} onStopped={() => setReload((x) => x + 1)} />

          <section className="box tb" aria-labelledby="ag-h">
            <div className="tb-h">
              <div>
                <h2 id="ag-h">{c.tableTitle}</h2>
                <p>{c.tableHint(live.length, people, pending)}</p>
              </div>
            </div>
            {shown.length === 0 ? (
              <p className="log-empty">{c.noAgents}</p>
            ) : (
              <table className="table rtable atable">
                <thead>
                  <tr>
                    <th scope="col">{c.colAgent}</th>
                    <th scope="col">{c.colFor}</th>
                    <th scope="col">{c.colAllowed}</th>
                    <th scope="col">{c.colLast}</th>
                    <th scope="col">{c.colState}</th>
                    <th scope="col">
                      <span className="sr">{t.users.actions}</span>
                    </th>
                  </tr>
                </thead>
                <tbody>
                  {shown.map((x) => {
                    const isSel = selected?.id === x.id;
                    return (
                      <tr
                        key={x.id}
                        className={
                          (isSel ? 'sel' : '') +
                          (x.state === 'pending' ? ' pend' : '') +
                          (x.state === 'revoked' ? ' stopped' : '')
                        }
                        aria-selected={isSel}
                        onClick={(e) => {
                          if (!(e.target as HTMLElement).closest('button')) setSel(x.id);
                        }}
                      >
                        <td className="lead">
                          <button type="button" className="an2 rowpick" onClick={() => setSel(x.id)}>
                            {x.clientName}
                          </button>
                          <span className="am2">
                            {clientHost(t, x)} ·{' '}
                            {x.state === 'pending'
                              ? t.agents.mine.requested(shortDate(x.createdAt, lang))
                              : c.connectedOn(shortDate(x.createdAt, lang))}
                          </span>
                        </td>
                        <td data-l={c.colFor}>{x.person.displayName}</td>
                        <td data-l={c.colAllowed}>
                          <ScopeChips scopes={x.scopes} />
                        </td>
                        <td data-l={c.colLast}>
                          {x.lastUsedAt ? (
                            relTime(t, lang, x.lastUsedAt)
                          ) : (
                            <span className="none">{t.agents.never}</span>
                          )}
                        </td>
                        <td data-l={c.colState}>
                          <StateChip c={x} />
                        </td>
                        <td className="acts-td">
                          <span className="acts2">
                            {x.state === 'pending' && (
                              <button
                                type="button"
                                className="btn sm primary"
                                disabled={busy === x.id}
                                onClick={() =>
                                  void act(
                                    x,
                                    () => api.approveAgent(x.id),
                                    c.approved(x.clientName, x.person.displayName),
                                  )
                                }
                              >
                                {c.approve}
                              </button>
                            )}
                            {x.state !== 'revoked' && (
                              <button
                                type="button"
                                className="btn sm"
                                disabled={busy === x.id}
                                onClick={() =>
                                  void act(
                                    x,
                                    () => api.revokeAgent(x.id),
                                    x.state === 'pending' ? c.refused(x.clientName) : c.revokedToast(x.clientName),
                                  )
                                }
                              >
                                {x.state === 'pending' ? c.refuse : t.agents.mine.revoke}
                              </button>
                            )}
                          </span>
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            )}
            {gone.length > 0 && (
              <div className="tb-f">
                <span />
                <button type="button" className="btn sm ghost" onClick={() => setShowRevoked((s) => !s)}>
                  {showRevoked ? t.agents.mine.hideRevoked : t.agents.mine.showRevoked(gone.length)}
                </button>
              </div>
            )}
          </section>

          <div className="cgrid">
            <div>
              <section className="box tb" aria-labelledby="act-h">
                <div className="tb-h">
                  <div>
                    <h2 id="act-h">{c.activityTitle}</h2>
                    <p>{c.activityHint}</p>
                  </div>
                  <span className="ret">
                    <IconClock size={14} />
                    {c.kept}
                  </span>
                </div>
                <div className="afilt">
                  <div className="seg sm wrap2" role="group" aria-label={c.kindGroup}>
                    {KINDS.map((k) => (
                      <button key={k} type="button" aria-pressed={kind === k} onClick={() => setKind(k)}>
                        {t.agents.kindFilter[k]}
                      </button>
                    ))}
                  </div>
                  <select
                    className="select"
                    aria-label={c.agentFilter}
                    value={agent}
                    onChange={(e) => setAgent(e.target.value)}
                  >
                    <option value="">{c.allAgents}</option>
                    {conns.map((x) => (
                      <option key={x.id} value={x.id}>
                        {x.clientName} · {x.person.displayName}
                      </option>
                    ))}
                  </select>
                </div>
                {activity.error ? (
                  <p className="log-empty">{t.common.genericError}</p>
                ) : !activity.items ? (
                  <ActivitySkeleton n={6} />
                ) : activity.items.length === 0 ? (
                  <p className="log-empty">{c.noLines}</p>
                ) : (
                  <ul className="alog">
                    {activity.items.map((a) => (
                      <ActivityLine key={a.id} a={a} showPerson />
                    ))}
                  </ul>
                )}
                <div className="tb-f">
                  <span>{activity.items ? c.lines(activity.items.length) : ' '}</span>
                  <span className="pager">
                    <button type="button" className="btn sm" disabled={!activity.hasNewer} onClick={activity.newer}>
                      {t.agents.mine.newer}
                    </button>
                    <button type="button" className="btn sm" disabled={!activity.hasOlder} onClick={activity.older}>
                      {t.agents.mine.older}
                    </button>
                  </span>
                </div>
              </section>
            </div>
            <div>
              <CapBox conn={selected} />
            </div>
          </div>

          <SettingsBox s={settings} onSaved={setSettings} />
        </>
      )}
    </PageShell>
  );
}
