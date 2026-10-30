import { Fragment, useEffect, useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import { api, ApiError, type AccessLogQuery } from '../api/client';
import {
  isRestrictedApp,
  isRestrictedFlow,
  type AccessLogEntry,
  type AccessLogPage,
  type AccessOverview,
  type AccessRecord,
  type Person,
} from '../api/types';
import { useAuth } from '../auth/auth';
import { useLandscape } from '../explorer/landscape';
import { paths } from '../explorer/paths';
import { useI18n } from '../i18n';
import type { Dict } from '../i18n/en';
import { PageShell } from '../records/common';
import { IconClock, IconDownload, IconLock, IconSearch, IconWarn } from '../ui/icons';
import { pagePaths } from '../docpages/model';
import { usePages } from '../docpages/store';
import { ExportDialog } from './ExportDialog';

/** Days an access-log line is kept. */
export const LOG_RETENTION_DAYS = 365;
const PAGE_SIZE = 50;

/** Where a record of the access log or overview opens. */
export function recordLink(kind: string | null, id: string | null): string | null {
  if (!kind || !id) return null;
  if (kind === 'application') return paths.app(id);
  if (kind === 'flow') return paths.flow(id);
  if (kind === 'page') return pagePaths.page(id);
  if (kind === 'dataObject') return paths.lineage(id);
  if (kind === 'team') return paths.teamRecord(id);
  return null;
}

/** "Full export" for a full export (its detail says so, or carries the reason), "Export" otherwise. */
export const isFullExport = (e: Pick<AccessLogEntry, 'what' | 'detail'>) =>
  e.what === 'export' && (!e.detail || !e.detail.startsWith('scope ') || e.detail.startsWith('scope full'));

/** The "why they could" cell of a log line. */
export function whyText(t: Dict, e: AccessLogEntry): string {
  if (e.what === 'export') return e.detail && !e.detail.startsWith('scope ') ? `« ${e.detail} »` : '—';
  const team = e.whyTeam?.name ?? '';
  switch (e.why) {
    case 'admin':
      return t.access.why.admin;
    case 'owningTeam':
      return t.access.why.owningTeam(team);
    case 'listedTeam':
      return t.access.why.listedTeam(team);
    case 'listedUser':
      return t.access.why.listedUser;
    default:
      return '—';
  }
}

/** Short date and time for a log line: "30 Oct, 10:42". */
function when(lang: string, iso: string) {
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return iso;
  return new Intl.DateTimeFormat(lang === 'fr' ? 'fr-FR' : 'en-GB', {
    day: 'numeric',
    month: 'short',
    hour: '2-digit',
    minute: '2-digit',
  }).format(d);
}

function RecordLinks({ records }: { records: AccessRecord[] }) {
  const { t } = useI18n();
  if (!records.length) return <span className="none">{t.common.none}</span>;
  return (
    <div className="rlinks">
      {records.map((r) => {
        const to = recordLink(r.kind, r.id);
        return to ? (
          <Link key={r.kind + r.id} className="rlink" to={to}>
            <IconLock size={12} />
            {r.title}
          </Link>
        ) : (
          <span key={r.kind + r.id} className="rlink">
            <IconLock size={12} />
            {r.title}
          </span>
        );
      })}
    </div>
  );
}

function Overview({ o }: { o: AccessOverview }) {
  const { t } = useI18n();
  const r = t.access.rule;
  return (
    <>
      <section className="box">
        <p className="rule">{r.map((x, i) => (i % 2 ? <b key={i}>{x}</b> : <Fragment key={i}>{x}</Fragment>))}</p>
        <p className="rule-x">{t.access.ruleX}</p>
        <div className="exc">
          <div>
            <IconLock />
            <p>
              <b>{t.access.excRestricted(o.restricted.length)}</b>
              <span>{t.access.excRestrictedText}</span>
            </p>
          </div>
          <div>
            <IconLock />
            <p>
              <b>{t.access.excNotes(o.protectedNotes.length)}</b>
              <span>{t.access.excNotesText}</span>
            </p>
          </div>
        </div>
      </section>
      <div className="acc-grid">
        <section className="box tb" aria-labelledby="ac-roles">
          <div className="tb-h">
            <div>
              <h2 id="ac-roles">{t.access.roles}</h2>
            </div>
            <Link className="btn ghost sm" to="/admin/users">
              {t.access.manageUsers}
            </Link>
          </div>
          <table className="table rtable">
            <thead>
              <tr>
                <th scope="col">{t.access.cRole}</th>
                <th scope="col">{t.access.cCan}</th>
                <th scope="col" className="num">
                  {t.access.cPeople}
                </th>
              </tr>
            </thead>
            <tbody>
              {(['admin', 'member', 'guest'] as const).map((role) => (
                <tr key={role}>
                  <td className="lead">
                    <span className={'rolebadge ' + role}>{t.top.role[role]}</span>
                  </td>
                  <td>{t.access.roleCan[role]}</td>
                  <td className="num" data-l={t.access.cPeople}>
                    {o.roles[role === 'admin' ? 'admins' : role === 'member' ? 'members' : 'guests']}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </section>
        <section className="box tb" aria-labelledby="ac-guests">
          <div className="tb-h">
            <div>
              <h2 id="ac-guests">{t.access.sharedBoxH}</h2>
              <p>{t.access.sharedBoxSub}</p>
            </div>
          </div>
          {o.guests.length ? (
            <table className="table rtable">
              <thead>
                <tr>
                  <th scope="col">{t.access.cGuest}</th>
                  <th scope="col">{t.access.cShared}</th>
                </tr>
              </thead>
              <tbody>
                {o.guests.map((g) => (
                  <tr key={g.user.username}>
                    <td className="lead">
                      {g.user.displayName}
                      <small className="none" style={{ display: 'block', fontWeight: 400, fontSize: 12 }}>
                        {g.user.username}
                      </small>
                    </td>
                    <td>
                      {g.records.length ? (
                        <div className="rlinks">
                          {g.records.map((x) => {
                            const to = recordLink(x.kind, x.id);
                            return to ? (
                              <Link key={x.kind + x.id} className="och" to={to}>
                                {x.title}
                              </Link>
                            ) : (
                              <span key={x.kind + x.id} className="och">
                                {x.title}
                              </span>
                            );
                          })}
                        </div>
                      ) : (
                        <span className="none">{t.access.nothingYet}</span>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          ) : (
            <p className="log-empty">{t.access.noGuests}</p>
          )}
        </section>
      </div>
      <section className="box tb" aria-labelledby="ac-teams">
        <div className="tb-h">
          <div>
            <h2 id="ac-teams">{t.access.byTeam}</h2>
            <p>{t.access.byTeamSub}</p>
          </div>
        </div>
        <table className="table rtable">
          <thead>
            <tr>
              <th scope="col">{t.access.cTeam}</th>
              <th scope="col">{t.access.cOwns}</th>
              <th scope="col">{t.access.cRestricted}</th>
              <th scope="col">{t.access.cNotes}</th>
              <th scope="col" className="num">
                {t.access.cMembers}
              </th>
            </tr>
          </thead>
          <tbody>
            {o.teams.map((tm) => {
              const restricted = o.restricted.filter((x) => x.owningTeamId === tm.id);
              const notes = o.protectedNotes.filter((x) => x.owningTeamId === tm.id);
              return (
                <tr key={tm.id}>
                  <td className="lead">
                    <Link to={paths.teamRecord(tm.id)}>{tm.name}</Link>
                  </td>
                  <td data-l={t.access.cOwns}>{t.access.ownsText(tm.applications, tm.flows)}</td>
                  <td data-l={t.access.cRestricted}>
                    <RecordLinks records={restricted} />
                  </td>
                  <td data-l={t.access.cNotes}>
                    {notes.length ? (
                      notes.map((x) => x.title).join(', ')
                    ) : (
                      <span className="none">{t.common.none}</span>
                    )}
                  </td>
                  <td className="num" data-l={t.access.cMembers}>
                    {tm.members}
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </section>
    </>
  );
}

interface LogFilters {
  person: string;
  record: string;
  kind: string;
  what: string;
  from: string;
  to: string;
}

const iso = (d: Date) => d.toISOString().slice(0, 10);

function AccessLog({ people }: { people: Person[] }) {
  const { t, lang } = useI18n();
  const { model: m } = useLandscape();
  const { list: pages } = usePages();
  const [f, setF] = useState<LogFilters>(() => {
    const now = new Date();
    return {
      person: '',
      record: '',
      kind: '',
      what: '',
      from: iso(new Date(now.getTime() - 29 * 86400000)),
      to: iso(now),
    };
  });
  const [debounced, setDebounced] = useState(f);
  const [cursors, setCursors] = useState<(string | undefined)[]>([undefined]);
  const [data, setData] = useState<AccessLogPage | null>(null);
  const [error, setError] = useState<string | null>(null);
  const set = <K extends keyof LogFilters>(k: K, v: string) => setF((x) => ({ ...x, [k]: v }));

  useEffect(() => {
    const h = setTimeout(() => {
      setDebounced(f);
      setCursors([undefined]);
    }, 300);
    return () => clearTimeout(h);
  }, [f]);

  // names typed in the filters are matched to usernames and record ids
  const records = useMemo(() => {
    const out: { id: string; label: string }[] = [];
    if (m) {
      for (const id of m.appIds) {
        const a = m.apps.get(id)!;
        if (!isRestrictedApp(a)) out.push({ id, label: a.name });
      }
      for (const fl of m.flows) if (!isRestrictedFlow(fl)) out.push({ id: fl.id, label: fl.name });
    }
    for (const p of pages ?? []) if (!p.restricted && 'title' in p) out.push({ id: p.id, label: p.title });
    return out;
  }, [m, pages]);

  const query = useMemo<AccessLogQuery>(() => {
    const low = (s: string) => s.trim().toLowerCase();
    const who = low(debounced.person);
    const person = who
      ? (people.find((p) => p.username === who || p.displayName.toLowerCase() === who)?.username ?? who)
      : undefined;
    const rec = low(debounced.record);
    const entityId = rec ? (records.find((r) => r.id === rec || r.label.toLowerCase() === rec)?.id ?? rec) : undefined;
    return {
      username: person,
      entityId,
      kind: debounced.kind || undefined,
      what: debounced.what || undefined,
      from: debounced.from || undefined,
      to: debounced.to || undefined,
      limit: PAGE_SIZE,
    };
  }, [debounced, people, records]);
  const cursor = cursors[cursors.length - 1];

  useEffect(() => {
    let alive = true;
    setData(null);
    setError(null);
    api
      .accessLog({ ...query, cursor })
      .then((d) => alive && setData(d))
      .catch(
        (e: unknown) =>
          alive &&
          setError(
            e instanceof ApiError
              ? Object.values(e.fieldErrors).flat().join(' ') || e.detail || e.message
              : t.common.genericError,
          ),
      );
    return () => {
      alive = false;
    };
  }, [query, cursor, t]);

  const teamOf = (username: string) => {
    const p = people.find((x) => x.username === username);
    return p && m ? p.teamIds.map((id) => m.teams.get(id)?.name ?? id).join(', ') : '';
  };

  return (
    <section className="box tb" aria-labelledby="ac-log">
      <div className="tb-h">
        <div>
          <h2 id="ac-log">{t.access.log}</h2>
          <p>{t.access.logSub}</p>
        </div>
        <span className="ret muted">
          <IconClock size={14} />
          {t.access.kept(LOG_RETENTION_DAYS)}
        </span>
      </div>
      <form className="logf" onSubmit={(e) => e.preventDefault()} role="search" aria-label={t.access.log}>
        <div className="field">
          <label htmlFor="lf-who">{t.access.fPerson}</label>
          <div className="withic">
            <IconSearch size={14} />
            <input
              className="input"
              id="lf-who"
              list="lf-who-l"
              placeholder={t.access.fPersonPh}
              value={f.person}
              onChange={(e) => set('person', e.target.value)}
              autoComplete="off"
            />
            <datalist id="lf-who-l">
              {people.map((p) => (
                <option key={p.username} value={p.username}>
                  {p.displayName}
                </option>
              ))}
            </datalist>
          </div>
        </div>
        <div className="field">
          <label htmlFor="lf-rec">{t.access.fRecord}</label>
          <div className="withic">
            <IconSearch size={14} />
            <input
              className="input"
              id="lf-rec"
              list="lf-rec-l"
              placeholder={t.access.fRecordPh}
              value={f.record}
              onChange={(e) => set('record', e.target.value)}
              autoComplete="off"
            />
            <datalist id="lf-rec-l">
              {records.map((r) => (
                <option key={r.id} value={r.label} />
              ))}
            </datalist>
          </div>
        </div>
        <div className="field">
          <label htmlFor="lf-kind">{t.access.fKind}</label>
          <select className="select" id="lf-kind" value={f.kind} onChange={(e) => set('kind', e.target.value)}>
            <option value="">{t.access.kinds.all}</option>
            <option value="application">{t.access.kinds.application}</option>
            <option value="flow">{t.access.kinds.flow}</option>
            <option value="page">{t.access.kinds.page}</option>
          </select>
        </div>
        <div className="field">
          <label htmlFor="lf-what">{t.access.fWhat}</label>
          <select className="select" id="lf-what" value={f.what} onChange={(e) => set('what', e.target.value)}>
            <option value="">{t.access.whats.all}</option>
            <option value="record">{t.access.whats.record}</option>
            <option value="protectedNotes">{t.access.whats.protectedNotes}</option>
            <option value="export">{t.access.whats.export}</option>
          </select>
        </div>
        <div className="field">
          <label htmlFor="lf-from">{t.access.fFrom}</label>
          <input
            className="input"
            id="lf-from"
            type="date"
            value={f.from}
            onChange={(e) => set('from', e.target.value)}
          />
        </div>
        <div className="field">
          <label htmlFor="lf-to">{t.access.fTo}</label>
          <input className="input" id="lf-to" type="date" value={f.to} onChange={(e) => set('to', e.target.value)} />
        </div>
      </form>
      {error ? (
        <div className="banner danger" role="alert" style={{ margin: '0 20px 16px' }}>
          <IconWarn />
          <span className="bt">{error}</span>
        </div>
      ) : !data ? (
        <div aria-busy="true" style={{ padding: '0 20px 16px' }}>
          {[0, 1, 2, 3].map((i) => (
            <span key={i} className="sk line" style={{ display: 'block', height: 34, marginTop: 8 }} />
          ))}
        </div>
      ) : data.items.length === 0 ? (
        <p className="log-empty">{t.access.logEmpty}</p>
      ) : (
        <table className="table rtable logt">
          <thead>
            <tr>
              <th scope="col">{t.access.cWhen}</th>
              <th scope="col">{t.access.cWho}</th>
              <th scope="col">{t.access.cWhat}</th>
              <th scope="col">{t.access.cRecord}</th>
              <th scope="col">{t.access.cWhy}</th>
            </tr>
          </thead>
          <tbody>
            {data.items.map((e) => {
              const full = isFullExport(e);
              const to = recordLink(e.kind, e.entityId);
              const title = e.what === 'export' ? t.access.wholeLandscape : (e.title ?? t.access.deletedRecord);
              const sub =
                e.what === 'export'
                  ? e.detail?.startsWith('scope ')
                    ? e.detail
                        .replace(/^scope \w+, /, '')
                        .toUpperCase()
                        .replace(/ ITEMS/, ' items')
                    : ''
                  : [e.kind && t.access.kindLabel[e.kind], e.owningTeam?.name].filter(Boolean).join(' · ');
              return (
                <tr key={e.id} className={full ? 'full' : undefined}>
                  <td className="when lead">{when(lang, e.at)}</td>
                  <td className="who" data-l={t.access.cWho}>
                    <b>{e.user.displayName}</b>
                    {e.admin ? (
                      <span className="rolebadge admin adm-flag">{t.access.adminFlag}</span>
                    ) : (
                      <small>{teamOf(e.user.username)}</small>
                    )}
                  </td>
                  <td data-l={t.access.cWhat}>
                    {e.what === 'export' ? (
                      <span className="what export">
                        <IconDownload size={12} />
                        {full ? t.access.whatLabel.full : t.access.whatLabel.export}
                      </span>
                    ) : e.what === 'protectedNotes' ? (
                      <span className="what notes">
                        <IconLock size={12} />
                        {t.access.whatLabel.protectedNotes}
                      </span>
                    ) : (
                      <span className="what">{t.access.whatLabel.record}</span>
                    )}
                  </td>
                  <td className="rec" data-l={t.access.cRecord}>
                    {to && e.title ? <Link to={to}>{title}</Link> : title}
                    {sub && <small>{sub}</small>}
                  </td>
                  <td className="why-cell" data-l={t.access.cWhy}>
                    {whyText(t, e)}
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      )}
      <div className="tb-f">
        <span>{data ? t.access.lines(data.items.length) : ''}</span>
        <span className="pager">
          <button
            type="button"
            className="btn sm"
            disabled={cursors.length < 2}
            onClick={() => setCursors((c) => c.slice(0, -1))}
          >
            {t.access.newer}
          </button>
          <button
            type="button"
            className="btn sm"
            disabled={!data?.nextCursor}
            onClick={() => data?.nextCursor && setCursors((c) => [...c, data.nextCursor!])}
          >
            {t.access.older}
          </button>
        </span>
      </div>
    </section>
  );
}

/** The administrators' Access page: the rule, roles, guest shares, teams and the access log. */
export function AccessPage() {
  const { t } = useI18n();
  const { isAdmin } = useAuth();
  const [o, setO] = useState<AccessOverview | null>(null);
  const [people, setPeople] = useState<Person[]>([]);
  const [error, setError] = useState(false);
  const [exporting, setExporting] = useState(false);

  useEffect(() => {
    if (!isAdmin) return;
    let alive = true;
    api
      .accessOverview()
      .then((x) => alive && setO(x))
      .catch(() => alive && setError(true));
    api
      .people()
      .then((x) => alive && setPeople(x))
      .catch(() => {});
    return () => {
      alive = false;
    };
  }, [isAdmin]);

  return (
    <PageShell title={t.access.pageTitle}>
      <div className="page-h">
        <div className="ph-t">
          <h1>{t.access.pageTitle}</h1>
          <p>{t.access.pageSub}</p>
        </div>
        {isAdmin && (
          <div className="page-actions">
            <button type="button" className="btn" onClick={() => setExporting(true)}>
              <IconDownload />
              {t.access.export}
            </button>
          </div>
        )}
      </div>
      {!isAdmin ? (
        <div className="banner" role="note">
          <IconWarn />
          <span className="bt">{t.access.forbidden}</span>
        </div>
      ) : (
        <>
          {error ? (
            <div className="banner danger" role="alert">
              <IconWarn />
              <span className="bt">{t.access.loadError}</span>
            </div>
          ) : o ? (
            <Overview o={o} />
          ) : (
            <div aria-busy="true">
              <span className="sk block" style={{ height: 150 }} />
              <div className="acc-grid">
                <span className="sk block" style={{ height: 220 }} />
                <span className="sk block" style={{ height: 220 }} />
              </div>
              <span className="sk block" style={{ height: 260, marginTop: 16 }} />
            </div>
          )}
          <AccessLog people={people} />
        </>
      )}
      {exporting && <ExportDialog onClose={() => setExporting(false)} />}
    </PageShell>
  );
}
