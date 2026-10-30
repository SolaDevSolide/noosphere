import { useEffect, useState, type FormEvent } from 'react';
import { useGo } from '../ui/useGo';
import { Link, useParams } from 'react-router-dom';
import { api, ApiError } from '../api/client';
import {
  FLOW_STATUSES,
  FLOW_TYPES,
  isGuestOutline,
  isRestrictedApp,
  type Flow,
  type FlowInput,
  type NotesExtras,
  type Proposal,
  type RecordExtras,
} from '../api/types';
import { AccessChips, type AccessPop } from '../access/AccessWindow';
import { notesDetail, NOTES_MAX, ProtectedNotesField, ProtectedNotesSection } from '../access/ProtectedNotes';
import { fieldLabelOf, isFieldRefusal, SecretBanner, secretFields } from '../access/secret';
import { GuestStub, RestrictedStub, SharedBanner } from '../access/Stubs';
import { useAuth } from '../auth/auth';
import { nameOf, pct, Sdot, StatusTag, teamName } from '../explorer/bits';
import { useLandscape } from '../explorer/landscape';
import { paths } from '../explorer/paths';
import { appOrder, fullApp } from '../domain/model';
import { useI18n } from '../i18n';
import type { Dict } from '../i18n/en';
import { IconHistory, IconWarn, TypeIcon } from '../ui/icons';
import { Markdown } from '../ui/Markdown';
import { useToast } from '../ui/Toast';
import {
  ConflictBanner,
  describe,
  Field,
  Missing,
  PageShell,
  RecordError,
  RecordSkeleton,
  serverErrors,
  useRecord,
  useUnsavedGuard,
} from './common';
import { HistoryPanel } from './HistoryPanel';
import { ownerTeamOf, permissionsFor } from './permissions';
import {
  ProposalSentBanner,
  ProposeFields,
  proposeUpdate,
  useProposeDraft,
  validatePropose,
  type EditMode,
} from './propose';
import { RecordExtensions } from './RecordExtensions';

interface Draft {
  name: string;
  fromId: string;
  toId: string;
  type: Flow['type'];
  schedule: string;
  dataObjectIds: string[];
  ownerTeamId: string;
  status: Flow['status'];
  volume: string;
  description: string;
  protectedNotes: string;
  changeSummary: string;
  changeDate: string;
}

type FlowDetail = Flow & RecordExtras & NotesExtras;

const toDraft = (f: FlowDetail): Draft => ({
  protectedNotes: f.protectedNotes ?? '',
  name: f.name,
  fromId: f.fromId,
  toId: f.toId,
  type: f.type,
  schedule: f.schedule ?? '',
  dataObjectIds: [...f.dataObjectIds],
  ownerTeamId: f.ownerTeamId ?? '',
  status: f.status,
  volume: f.volume ?? '',
  description: f.description ?? '',
  changeSummary: f.plannedChange?.summary ?? '',
  changeDate: f.plannedChange?.date ?? '',
});

export function validateFlow(t: Dict, d: Draft): Record<string, string> {
  const e: Record<string, string> = {};
  if (!d.name.trim()) e.name = t.record.errors.required;
  else if (d.name.length > 120) e.name = t.record.errors.tooLong(120);
  if (!d.fromId) e.fromId = t.record.errors.required;
  if (!d.toId) e.toId = t.record.errors.required;
  else if (d.fromId && d.fromId === d.toId) e.toId = t.record.errors.sameEnds;
  if (d.changeDate && !/^\d{4}-\d{2}-\d{2}$/.test(d.changeDate)) e.changeDate = t.record.errors.dateFormat;
  if (d.changeDate && !d.changeSummary.trim()) e.changeSummary = t.record.errors.summaryRequired;
  if (d.description.length > 4000) e.description = t.record.errors.tooLong(4000);
  if (d.protectedNotes.length > NOTES_MAX) e.protectedNotes = t.access.notesTooLong;
  return e;
}

function FlowForm({
  flow,
  mode,
  ownerName,
  focus,
  onSaved,
  onProposed,
  onCancel,
  onReload,
}: {
  flow: FlowDetail;
  mode: EditMode;
  ownerName: string | null;
  focus?: string;
  onSaved: (f: Flow) => void;
  onProposed: (p: Proposal) => void;
  onCancel: () => void;
  onReload: () => void;
}) {
  const { t } = useI18n();
  const { model: m } = useLandscape();
  const [d, setD] = useState<Draft>(() => toDraft(flow));
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [saving, setSaving] = useState(false);
  const [conflict, setConflict] = useState(false);
  const [failure, setFailure] = useState<string | null>(null);
  const [secrets, setSecrets] = useState<string[]>([]);
  const [prop, setProp] = useProposeDraft(mode, flow.name);
  useEffect(() => {
    if (focus) document.getElementById(focus)?.focus();
  }, [focus]);
  const dirty = JSON.stringify(d) !== JSON.stringify(toDraft(flow));
  useUnsavedGuard(dirty);
  const set = <K extends keyof Draft>(k: K, v: Draft[K]) => setD((x) => ({ ...x, [k]: v }));
  if (!m) return null;
  const apps = m.appIds.filter((id) => fullApp(m, id)).sort(appOrder(m));

  const submit = async (ev: FormEvent) => {
    ev.preventDefault();
    const e = { ...validateFlow(t, d), ...validatePropose(t, prop) };
    setErrors(e);
    setFailure(null);
    setSecrets([]);
    if (Object.keys(e).length) {
      document.getElementById(Object.keys(e)[0])?.focus();
      return;
    }
    setSaving(true);
    try {
      const body: FlowInput = {
        id: flow.id,
        version: flow.version,
        name: d.name.trim(),
        fromId: d.fromId,
        toId: d.toId,
        type: d.type,
        schedule: d.schedule.trim() || null,
        dataObjectIds: d.dataObjectIds,
        ownerTeamId: d.ownerTeamId || null,
        status: d.status,
        volume: d.volume.trim() || null,
        description: d.description.trim() || null,
        plannedChange: d.changeSummary.trim() ? { summary: d.changeSummary.trim(), date: d.changeDate || null } : null,
      };
      if (!flow.protectedNotesHidden || d.protectedNotes.trim()) body.protectedNotes = d.protectedNotes;
      if (prop.proposing) onProposed(await proposeUpdate('flow', flow.id, flow.version, body, prop));
      else onSaved(await api.updateFlow(flow.id, body));
    } catch (err) {
      if (err instanceof ApiError && err.isConflict) setConflict(true);
      else if (isFieldRefusal(err)) {
        setErrors(serverErrors(err, t));
        setSecrets(secretFields(err));
      } else
        setFailure(
          err instanceof ApiError && err.status === 0
            ? t.common.networkError
            : err instanceof ApiError
              ? err.detail || err.message
              : t.common.genericError,
        );
    } finally {
      setSaving(false);
    }
  };
  const cancel = () => {
    if (!dirty || window.confirm(t.record.discardConfirm)) onCancel();
  };
  const errCount = Object.keys(errors).length;
  const appSelect = (id: 'fromId' | 'toId', label: string) => (
    <Field id={id} label={label} required error={errors[id]}>
      <select
        className="select"
        {...describe(id, undefined, errors[id])}
        value={d[id]}
        onChange={(e) => set(id, e.target.value)}
      >
        <option value="">{t.record.choose}</option>
        {!fullApp(m, d[id]) && d[id] && <option value={d[id]}>{t.common.restrictedApp}</option>}
        {apps.map((a) => (
          <option key={a} value={a}>
            {nameOf(t, m, a)}
          </option>
        ))}
      </select>
    </Field>
  );
  return (
    <form className="form box" onSubmit={(e) => void submit(e)} noValidate aria-labelledby="edit-h">
      <h2 className="big" id="edit-h">
        {prop.proposing ? t.propose.suggesting : t.record.editing}
      </h2>
      {conflict && <ConflictBanner onReload={onReload} />}
      {failure && (
        <div className="banner danger" role="alert">
          <span className="bt">{failure}</span>
        </div>
      )}
      {secrets.length > 0 ? (
        <SecretBanner fields={secrets} label={(f) => fieldLabelOf(t, f)} />
      ) : (
        errCount > 0 && (
          <div className="banner danger" role="alert">
            <span className="bt">{t.record.errors.fixBelow}</span>
          </div>
        )
      )}
      <div className="fgrid">
        <Field id="name" label={t.record.fields.name} required error={errors.name} full>
          <input
            className="input"
            {...describe('name', undefined, errors.name)}
            value={d.name}
            onChange={(e) => set('name', e.target.value)}
            maxLength={140}
            autoComplete="off"
          />
        </Field>
        {appSelect('fromId', t.record.fields.from)}
        {appSelect('toId', t.record.fields.to)}
        <Field id="type" label={t.record.fields.type} required>
          <select
            className="select"
            id="type"
            value={d.type}
            onChange={(e) => set('type', e.target.value as Draft['type'])}
          >
            {FLOW_TYPES.map((k) => (
              <option key={k} value={k}>
                {t.type[k]}
              </option>
            ))}
          </select>
        </Field>
        <Field id="status" label={t.record.fields.status} required>
          <select
            className="select"
            id="status"
            value={d.status}
            onChange={(e) => set('status', e.target.value as Draft['status'])}
          >
            {FLOW_STATUSES.map((k) => (
              <option key={k} value={k}>
                {t.status[k]}
              </option>
            ))}
          </select>
        </Field>
        <Field id="schedule" label={t.record.fields.schedule} hint={t.record.hints.schedule}>
          <input
            className="input"
            {...describe('schedule', t.record.hints.schedule)}
            value={d.schedule}
            onChange={(e) => set('schedule', e.target.value)}
            autoComplete="off"
          />
        </Field>
        <Field id="volume" label={t.record.fields.volume}>
          <input
            className="input"
            id="volume"
            value={d.volume}
            onChange={(e) => set('volume', e.target.value)}
            autoComplete="off"
          />
        </Field>
        <Field id="ownerTeamId" label={t.record.fields.owner}>
          <select
            className="select"
            id="ownerTeamId"
            value={d.ownerTeamId}
            onChange={(e) => set('ownerTeamId', e.target.value)}
          >
            <option value="">{t.record.noTeam}</option>
            {[...m.teams.values()].map((x) => (
              <option key={x.id} value={x.id}>
                {x.name}
              </option>
            ))}
          </select>
        </Field>
        <div className="field full">
          <span className="lbl" id="objs-l">
            {t.record.fields.dataObjects}
          </span>
          <div className="checks" role="group" aria-labelledby="objs-l" aria-describedby="objs-hint">
            {m.objectIds.map((o) => (
              <label key={o} className="chip">
                <input
                  type="checkbox"
                  className="sr"
                  checked={d.dataObjectIds.includes(o)}
                  onChange={(e) =>
                    set(
                      'dataObjectIds',
                      e.target.checked ? [...d.dataObjectIds, o] : d.dataObjectIds.filter((x) => x !== o),
                    )
                  }
                />
                <Sdot s={m.objects.get(o)!.sensitivity} />
                {m.objects.get(o)!.name}
              </label>
            ))}
          </div>
          <small id="objs-hint">{t.record.hints.sensitivity}</small>
        </div>
        <Field id="changeSummary" label={t.record.fields.plannedSummary} error={errors.changeSummary}>
          <input
            className="input"
            {...describe('changeSummary', undefined, errors.changeSummary)}
            value={d.changeSummary}
            onChange={(e) => set('changeSummary', e.target.value)}
            autoComplete="off"
          />
        </Field>
        <Field id="changeDate" label={t.record.fields.plannedDate} error={errors.changeDate}>
          <input
            className="input"
            type="date"
            {...describe('changeDate', undefined, errors.changeDate)}
            value={d.changeDate}
            onChange={(e) => set('changeDate', e.target.value)}
          />
        </Field>
        <Field
          id="description"
          label={t.record.fields.description}
          hint={t.record.hints.description}
          error={errors.description}
          full
        >
          <textarea
            className="textarea"
            {...describe('description', t.record.hints.description, errors.description)}
            rows={5}
            value={d.description}
            onChange={(e) => set('description', e.target.value)}
          />
        </Field>
        <ProtectedNotesField
          value={d.protectedNotes}
          onChange={(v) => set('protectedNotes', v)}
          hidden={!!flow.protectedNotesHidden}
          who={ownerName || t.access.administrators}
          error={errors.protectedNotes}
        />
      </div>
      <ProposeFields mode={mode} ownerName={ownerName} value={prop} onChange={setProp} errors={errors} />
      <div className="form-actions sticky">
        <span className={'muted' + (errCount ? ' notsaved' : '')} aria-live="polite">
          {errCount ? t.access.notSavedCount(errCount) : dirty ? t.record.unsaved : ''}
        </span>
        <button type="button" className="btn" onClick={cancel} disabled={saving}>
          {t.common.cancel}
        </button>
        <button type="submit" className="btn primary" disabled={saving || conflict}>
          {prop.proposing ? (saving ? t.propose.sending : t.propose.send) : saving ? t.common.saving : t.common.save}
        </button>
      </div>
    </form>
  );
}

function EndCard({ id }: { id: string }) {
  const { t } = useI18n();
  const { model: m } = useLandscape();
  if (!m) return null;
  const a = fullApp(m, id);
  if (!a)
    return (
      <div className="endcard">
        <b>{t.common.restrictedApp}</b>
        <span>{t.common.noAccess}</span>
      </div>
    );
  return (
    <Link className="endcard" to={paths.app(id)}>
      <b>{a.name}</b>
      <span>
        <span className="kind">{t.kind[a.kind]}</span>
        {teamName(m, a.teamId)}
      </span>
    </Link>
  );
}

export function FlowRecord() {
  const { id = '' } = useParams();
  const { t, date } = useI18n();
  const { me, isAdmin, teamIds } = useAuth();
  const { model: m, reload: reloadLandscape } = useLandscape();
  const toast = useToast();
  const nav = useGo();
  const { data: f, setData, error, reload } = useRecord<FlowDetail>(api.flow, id);
  const [editing, setEditing] = useState<false | { focus?: string }>(false);
  const [pop, setPop] = useState<AccessPop>(null);
  const [historyOpen, setHistoryOpen] = useState(false);
  const [sent, setSent] = useState<Proposal | null>(null);
  if (error)
    return (
      <PageShell>
        <RecordError error={error} onRetry={reload} />
      </PageShell>
    );
  if (!f || !m)
    return (
      <PageShell>
        <RecordSkeleton />
      </PageShell>
    );
  if ((f as { restricted?: boolean }).restricted)
    return (
      <PageShell title={t.access.restrictedName.flow}>
        <RestrictedStub kind="flow" badge={<span className="kb flow">{t.search.badge.flow}</span>} />
      </PageShell>
    );
  if (isGuestOutline(f)) {
    const ends = `${nameOf(t, m, f.fromId)} → ${nameOf(t, m, f.toId)}`;
    return (
      <PageShell title={f.name}>
        <GuestStub
          name={f.name}
          badge={
            <>
              <span className="kb flow">{t.search.badge.flow}</span> <span className="muted">{t.type[f.type]}</span>
            </>
          }
          sub={ends}
          fields={[
            [t.record.fields.from, nameOf(t, m, f.fromId)],
            [t.record.fields.to, nameOf(t, m, f.toId)],
            [t.record.fields.type, t.type[f.type]],
          ]}
        />
      </PageShell>
    );
  }
  const missing = [
    !f.schedule && t.record.missingFields.schedule,
    !f.dataObjectIds.length && t.record.missingFields.dataObjects,
    !f.ownerTeamId && t.record.missingFields.owner,
    !f.description && t.record.missingFields.description,
  ].filter((x): x is string => !!x);
  const remove = async () => {
    if (!window.confirm(t.record.deleteConfirm(f.name))) return;
    try {
      await api.deleteFlow(f.id);
      toast(t.record.deleted(f.name));
      reloadLandscape();
      nav('/');
    } catch (e) {
      toast(e instanceof ApiError ? e.detail || e.message : t.common.genericError);
    }
  };
  const p = pct(f.completeness ?? 0);
  const owner = ownerTeamOf(m, 'flow', f);
  const perm = permissionsFor(me?.role, teamIds, owner, f.permissions);
  const ownerName = owner ? teamName(m, owner) || owner : null;
  const guest = me?.role === 'guest';
  const endRestricted = [f.fromId, f.toId].some((x) => {
    const e = m.apps.get(x);
    return !!e && !isRestrictedApp(e) && e.visibility === 'restricted';
  });
  return (
    <PageShell title={f.name}>
      <div className="page-h">
        <div className="ph-t">
          <span className="kb flow">{t.search.badge.flow}</span>{' '}
          <span
            className={`tchip t-${f.type}`}
            style={{ background: 'none', color: 'var(--tc)', display: 'inline-flex', gap: 5, alignItems: 'center' }}
          >
            <TypeIcon type={f.type} />
            {t.type[f.type]}
          </span>
          <h1>{f.name}</h1>
          <p>
            {nameOf(t, m, f.fromId)} → {nameOf(t, m, f.toId)}
            {f.schedule ? ' · ' + f.schedule : ''}
          </p>
        </div>
        {!editing && (
          <div className="page-actions">
            {!guest && (
              <Link className="btn" to={paths.flowLens(f.id)}>
                {t.record.openLens}
              </Link>
            )}
            {perm.publish ? (
              <button type="button" className="btn primary" onClick={() => setEditing({})}>
                {t.record.edit}
              </button>
            ) : (
              perm.propose && (
                <button type="button" className="btn primary" onClick={() => setEditing({})}>
                  {t.propose.suggest}
                </button>
              )
            )}
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
            {isAdmin && (
              <button type="button" className="btn danger" onClick={() => void remove()}>
                {t.common.delete}
              </button>
            )}
          </div>
        )}
        {!editing && (
          <AccessChips
            kind="flow"
            id={f.id}
            name={f.name}
            version={f.version}
            visibility={endRestricted ? 'restricted' : 'everyone'}
            owner={owner}
            pop={pop}
            setPop={setPop}
            pages={f.pages}
            canPublish={perm.publish}
            onTransfer={perm.publish ? () => setEditing({ focus: 'ownerTeamId' }) : undefined}
          />
        )}
      </div>
      {guest && <SharedBanner owner={ownerName ?? t.access.administrators} />}
      {!perm.propose && !guest && (
        <div className="banner" role="note">
          <span className="bt">
            <b>{t.record.readOnly}.</b> {t.record.readOnlyHint}
          </span>
        </div>
      )}
      {sent && !editing && <ProposalSentBanner proposal={sent} onClose={() => setSent(null)} />}
      {historyOpen && (
        <HistoryPanel
          kind="flow"
          id={f.id}
          name={f.name}
          canPublish={perm.publish}
          canPropose={perm.propose}
          notesOwner={ownerName}
          onClose={() => setHistoryOpen(false)}
          onRestored={() => {
            reload();
            reloadLandscape();
          }}
        />
      )}
      {editing ? (
        <FlowForm
          key={f.version}
          flow={f}
          mode={perm.publish ? 'publish' : 'propose'}
          ownerName={ownerName}
          focus={editing.focus}
          onProposed={(pr) => {
            setSent(pr);
            setEditing(false);
            toast(t.propose.sent);
          }}
          onReload={reload}
          onCancel={() => setEditing(false)}
          onSaved={(next) => {
            setData(next);
            setEditing(false);
            toast(t.record.saved);
            reloadLandscape();
          }}
        />
      ) : (
        <>
          <div className={`ends2 t-${f.type}`}>
            <EndCard id={f.fromId} />
            <div className="endarrow" aria-hidden="true">
              <TypeIcon type={f.type} />→
            </div>
            <EndCard id={f.toId} />
          </div>
          {f.plannedChange?.summary && (
            <div className="banner warn">
              <IconWarn />
              <span className="bt">
                <b>{t.record.fields.plannedChange}</b> {f.plannedChange.summary}
                {f.plannedChange.date ? ' · ' + t.record.plannedOn(date(f.plannedChange.date)) : ''}
              </span>
            </div>
          )}
          <div className="grid2">
            <div>
              <section className="box" aria-labelledby="desc-h">
                <h2 id="desc-h">{t.record.fields.description}</h2>
                {f.description ? <Markdown text={f.description} /> : <p className="muted">{t.record.noDescription}</p>}
              </section>
              <ProtectedNotesSection
                record={f}
                ownerId={owner}
                ownerName={ownerName ?? t.access.administrators}
                restricted={endRestricted}
              />
              <RecordExtensions kind="flow" record={f} />
            </div>
            <section className="box" aria-labelledby="fields-h">
              <h2 id="fields-h">{t.panel.details}</h2>
              <dl className="fields">
                <dt>{t.record.fields.id}</dt>
                <dd>
                  <code>{f.id}</code>
                </dd>
                <dt>{t.record.fields.type}</dt>
                <dd>{t.type[f.type]}</dd>
                <dt>{t.record.fields.schedule}</dt>
                <dd>{f.schedule || '—'}</dd>
                <dt>{t.record.fields.volume}</dt>
                <dd>{f.volume || '—'}</dd>
                <dt>{t.record.fields.dataObjects}</dt>
                <dd>
                  {f.dataObjectIds.length
                    ? f.dataObjectIds.map((o) => (
                        <Link key={o} className="och" to={paths.lineage(o)} style={{ marginRight: 6 }}>
                          {m.objects.get(o) && <Sdot s={m.objects.get(o)!.sensitivity} />}
                          {m.objects.get(o)?.name ?? o}
                        </Link>
                      ))
                    : '—'}
                </dd>
                <dt>{t.record.fields.sensitivity}</dt>
                <dd>
                  <Sdot s={f.sensitivity} />
                  {t.sens[f.sensitivity]}
                </dd>
                {!guest && (
                  <>
                    <dt>{t.record.fields.protectedNotes}</dt>
                    <dd>{notesDetail(t, f, ownerName ?? t.access.administrators)}</dd>
                  </>
                )}
                <dt>{t.record.fields.owner}</dt>
                <dd>
                  {f.ownerTeamId ? <Link to={paths.teamRecord(f.ownerTeamId)}>{teamName(m, f.ownerTeamId)}</Link> : '—'}
                </dd>
                <dt>{t.record.fields.status}</dt>
                <dd>
                  {t.status[f.status]} <StatusTag f={f} />
                </dd>
                <dt>{t.record.fields.completeness}</dt>
                <dd>
                  <span
                    className={'meter' + ((f.completeness ?? 0) < 0.65 ? ' low' : '')}
                    style={{ display: 'flex', gap: 10, alignItems: 'center' }}
                  >
                    <span
                      className="bar2"
                      role="progressbar"
                      aria-valuenow={p}
                      aria-valuemin={0}
                      aria-valuemax={100}
                      aria-label={t.record.fields.completeness}
                    >
                      <i style={{ width: p + '%' }} />
                    </span>
                    <b>{p} %</b>
                  </span>
                  <Missing items={missing} />
                </dd>
                <dt>{t.record.fields.updatedAt}</dt>
                <dd>
                  {date(f.updatedAt)} · {t.record.fields.version} {f.version}
                </dd>
              </dl>
            </section>
          </div>
        </>
      )}
    </PageShell>
  );
}
