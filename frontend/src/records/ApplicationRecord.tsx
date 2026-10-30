import { useState, type FormEvent } from 'react';
import { useGo } from '../ui/useGo';
import { Link, useParams } from 'react-router-dom';
import { api, ApiError } from '../api/client';
import { APP_KINDS, LIFECYCLES, VISIBILITIES, type Application, type ApplicationInput } from '../api/types';
import { useAuth } from '../auth/auth';
import { pct, Sdot, teamName } from '../explorer/bits';
import { useLandscape } from '../explorer/landscape';
import { FlowRow } from '../explorer/Panel';
import { paths } from '../explorer/paths';
import { useI18n } from '../i18n';
import type { Dict } from '../i18n/en';
import { IconLock } from '../ui/icons';
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

type Draft = Required<
  Pick<ApplicationInput, 'name' | 'domainId' | 'kind' | 'lifecycle' | 'visibility' | 'sourceOf'>
> & { teamId: string; tech: string; description: string };

const toDraft = (a: Application): Draft => ({
  name: a.name,
  domainId: a.domainId,
  teamId: a.teamId ?? '',
  kind: a.kind,
  tech: a.tech ?? '',
  description: a.description ?? '',
  lifecycle: a.lifecycle,
  visibility: a.visibility,
  sourceOf: [...a.sourceOf],
});

export function validateApp(t: Dict, d: Draft): Record<string, string> {
  const e: Record<string, string> = {};
  if (!d.name.trim()) e.name = t.record.errors.required;
  else if (d.name.length > 120) e.name = t.record.errors.tooLong(120);
  if (!d.domainId) e.domainId = t.record.errors.required;
  if (d.tech.length > 200) e.tech = t.record.errors.tooLong(200);
  if (d.description.length > 4000) e.description = t.record.errors.tooLong(4000);
  return e;
}

function AppForm({
  app,
  onSaved,
  onCancel,
  onReload,
}: {
  app: Application;
  onSaved: (a: Application) => void;
  onCancel: () => void;
  onReload: () => void;
}) {
  const { t } = useI18n();
  const { model: m } = useLandscape();
  const [d, setD] = useState<Draft>(() => toDraft(app));
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [saving, setSaving] = useState(false);
  const [conflict, setConflict] = useState(false);
  const [failure, setFailure] = useState<string | null>(null);
  const dirty = JSON.stringify(d) !== JSON.stringify(toDraft(app));
  useUnsavedGuard(dirty);
  const set = <K extends keyof Draft>(k: K, v: Draft[K]) => setD((x) => ({ ...x, [k]: v }));

  const submit = async (ev: FormEvent) => {
    ev.preventDefault();
    const e = validateApp(t, d);
    setErrors(e);
    setFailure(null);
    if (Object.keys(e).length) {
      document.getElementById(Object.keys(e)[0])?.focus();
      return;
    }
    setSaving(true);
    try {
      const body: ApplicationInput = {
        id: app.id,
        version: app.version,
        name: d.name.trim(),
        domainId: d.domainId,
        teamId: d.teamId || null,
        kind: d.kind,
        tech: d.tech.trim() || null,
        description: d.description.trim() || null,
        lifecycle: d.lifecycle,
        visibility: d.visibility,
        sourceOf: d.sourceOf,
      };
      onSaved(await api.updateApplication(app.id, body));
    } catch (err) {
      if (err instanceof ApiError && err.isConflict) setConflict(true);
      else if (err instanceof ApiError && err.status === 400 && Object.keys(err.fieldErrors).length)
        setErrors(serverErrors(err));
      else
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
  if (!m) return null;
  const errCount = Object.keys(errors).length;
  return (
    <form className="form box" onSubmit={(e) => void submit(e)} noValidate aria-labelledby="edit-h">
      <h2 className="big" id="edit-h">
        {t.record.editing}
      </h2>
      {conflict && <ConflictBanner onReload={onReload} />}
      {failure && (
        <div className="banner danger" role="alert">
          <span className="bt">{failure}</span>
        </div>
      )}
      {errCount > 0 && (
        <div className="banner danger" role="alert">
          <span className="bt">{t.record.errors.fixBelow}</span>
        </div>
      )}
      <div className="fgrid">
        <Field id="name" label={t.record.fields.name} required error={errors.name}>
          <input
            className="input"
            {...describe('name', undefined, errors.name)}
            value={d.name}
            onChange={(e) => set('name', e.target.value)}
            maxLength={140}
            autoComplete="off"
          />
        </Field>
        <Field id="domainId" label={t.record.fields.domain} required error={errors.domainId}>
          <select
            className="select"
            {...describe('domainId', undefined, errors.domainId)}
            value={d.domainId}
            onChange={(e) => set('domainId', e.target.value)}
          >
            {m.domains.map((x) => (
              <option key={x.id} value={x.id}>
                {x.name}
              </option>
            ))}
          </select>
        </Field>
        <Field id="teamId" label={t.record.fields.team} error={errors.teamId}>
          <select
            className="select"
            {...describe('teamId', undefined, errors.teamId)}
            value={d.teamId}
            onChange={(e) => set('teamId', e.target.value)}
          >
            <option value="">{t.record.noTeam}</option>
            {[...m.teams.values()].map((x) => (
              <option key={x.id} value={x.id}>
                {x.name}
              </option>
            ))}
          </select>
        </Field>
        <Field id="kind" label={t.record.fields.kind} required>
          <select
            className="select"
            id="kind"
            value={d.kind}
            onChange={(e) => set('kind', e.target.value as Draft['kind'])}
          >
            {APP_KINDS.map((k) => (
              <option key={k} value={k}>
                {t.kindName[k]}
              </option>
            ))}
          </select>
        </Field>
        <Field id="tech" label={t.record.fields.tech} error={errors.tech}>
          <input
            className="input"
            {...describe('tech', undefined, errors.tech)}
            value={d.tech}
            onChange={(e) => set('tech', e.target.value)}
            autoComplete="off"
          />
        </Field>
        <Field id="lifecycle" label={t.record.fields.lifecycle} required>
          <select
            className="select"
            id="lifecycle"
            value={d.lifecycle}
            onChange={(e) => set('lifecycle', e.target.value as Draft['lifecycle'])}
          >
            {LIFECYCLES.map((k) => (
              <option key={k} value={k}>
                {t.lifecycle[k]}
              </option>
            ))}
          </select>
        </Field>
        <Field id="visibility" label={t.record.fields.visibility} hint={t.record.hints.visibility}>
          <select
            className="select"
            {...describe('visibility', t.record.hints.visibility)}
            value={d.visibility}
            onChange={(e) => set('visibility', e.target.value as Draft['visibility'])}
          >
            {VISIBILITIES.map((k) => (
              <option key={k} value={k}>
                {t.visibility[k]}
              </option>
            ))}
          </select>
        </Field>
        <div className="field full">
          <span className="lbl" id="sourceOf-l">
            {t.record.fields.sourceOf}
          </span>
          <div className="checks" role="group" aria-labelledby="sourceOf-l">
            {m.objectIds.map((o) => (
              <label key={o} className="chip">
                <input
                  type="checkbox"
                  className="sr"
                  checked={d.sourceOf.includes(o)}
                  onChange={(e) =>
                    set('sourceOf', e.target.checked ? [...d.sourceOf, o] : d.sourceOf.filter((x) => x !== o))
                  }
                />
                <Sdot s={m.objects.get(o)!.sensitivity} />
                {m.objects.get(o)!.name}
              </label>
            ))}
          </div>
        </div>
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
            rows={6}
            value={d.description}
            onChange={(e) => set('description', e.target.value)}
          />
        </Field>
      </div>
      <div className="form-actions sticky">
        <span className="muted" aria-live="polite">
          {dirty ? t.record.unsaved : ''}
        </span>
        <button type="button" className="btn" onClick={cancel} disabled={saving}>
          {t.common.cancel}
        </button>
        <button type="submit" className="btn primary" disabled={saving || conflict}>
          {saving ? t.common.saving : t.common.save}
        </button>
      </div>
    </form>
  );
}

export function ApplicationRecord() {
  const { id = '' } = useParams();
  const { t, date } = useI18n();
  const { canEdit, isAdmin } = useAuth();
  const { model: m, reload: reloadLandscape } = useLandscape();
  const toast = useToast();
  const nav = useGo();
  const { data: a, setData, error, reload } = useRecord(api.application, id);
  const [editing, setEditing] = useState(false);

  if (error)
    return (
      <PageShell>
        <RecordError error={error} onRetry={reload} />
      </PageShell>
    );
  if (!a || !m)
    return (
      <PageShell>
        <RecordSkeleton />
      </PageShell>
    );
  if ((a as { restricted?: boolean }).restricted)
    return (
      <PageShell>
        <div className="banner" role="note">
          <IconLock />
          <span className="bt">
            <b>{t.common.restrictedApp}</b> · {t.common.noAccess}
          </span>
        </div>
      </PageShell>
    );
  const ins = m.flowsIn.get(id) ?? [];
  const outs = m.flowsOut.get(id) ?? [];
  const domain = m.domain.get(a.domainId);
  const missing = [
    !a.teamId && t.record.missingFields.team,
    !a.tech && t.record.missingFields.tech,
    !a.description && t.record.missingFields.description,
    !ins.length && !outs.length && t.record.missingFields.flow,
  ].filter((x): x is string => !!x);
  const remove = async () => {
    if (!window.confirm(t.record.deleteConfirm(a.name))) return;
    try {
      await api.deleteApplication(a.id);
      toast(t.record.deleted(a.name));
      reloadLandscape();
      nav('/');
    } catch (e) {
      toast(e instanceof ApiError ? e.detail || e.message : t.common.genericError);
    }
  };
  const p = pct(a.completeness);
  return (
    <PageShell>
      <div className="page-h">
        <div className="ph-t">
          <span className="kb app">{t.search.badge.app}</span> <span className="muted">{t.kindName[a.kind]}</span>
          <h1>{a.name}</h1>
          <p>
            {[domain?.name, teamName(m, a.teamId), a.tech].filter(Boolean).join(' · ')}
            {a.visibility === 'restricted' && <> · {t.visibility.restricted}</>}
          </p>
        </div>
        {!editing && (
          <div className="page-actions">
            <Link className="btn" to={paths.appLens(a.id)}>
              {t.record.openLens}
            </Link>
            {canEdit && (
              <button type="button" className="btn primary" onClick={() => setEditing(true)}>
                {t.record.edit}
              </button>
            )}
            {isAdmin && (
              <button type="button" className="btn danger" onClick={() => void remove()}>
                {t.common.delete}
              </button>
            )}
          </div>
        )}
      </div>
      {!canEdit && (
        <div className="banner" role="note">
          <span className="bt">
            <b>{t.record.readOnly}.</b> {t.record.readOnlyHint}
          </span>
        </div>
      )}
      {editing ? (
        <AppForm
          key={a.version}
          app={a}
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
        <div className="grid2">
          <div>
            <section className="box" aria-labelledby="desc-h">
              <h2 id="desc-h">{t.record.fields.description}</h2>
              {a.description ? <Markdown text={a.description} /> : <p className="muted">{t.record.noDescription}</p>}
            </section>
            <section className="box" aria-labelledby="flows-h">
              <h2 id="flows-h">{t.record.flowsIn(ins.length)}</h2>
              <div className="rows">
                {ins.length ? (
                  ins.map((f) => <FlowRow key={f.id} f={f} perspective="in" />)
                ) : (
                  <p className="muted">{t.record.noFlows}</p>
                )}
              </div>
              <h2 style={{ marginTop: 18 }}>{t.record.flowsOut(outs.length)}</h2>
              <div className="rows">
                {outs.length ? (
                  outs.map((f) => <FlowRow key={f.id} f={f} perspective="out" />)
                ) : (
                  <p className="muted">{t.record.noFlows}</p>
                )}
              </div>
            </section>
          </div>
          <section className="box" aria-labelledby="fields-h">
            <h2 id="fields-h">{t.panel.details}</h2>
            <dl className="fields">
              <dt>{t.record.fields.id}</dt>
              <dd>
                <code>{a.id}</code>
              </dd>
              <dt>{t.record.fields.domain}</dt>
              <dd>{domain?.name ?? a.domainId}</dd>
              <dt>{t.record.fields.team}</dt>
              <dd>{a.teamId ? <Link to={paths.team(a.teamId)}>{teamName(m, a.teamId)}</Link> : '—'}</dd>
              <dt>{t.record.fields.kind}</dt>
              <dd>{t.kindName[a.kind]}</dd>
              <dt>{t.record.fields.tech}</dt>
              <dd>{a.tech || '—'}</dd>
              <dt>{t.record.fields.lifecycle}</dt>
              <dd>{t.lifecycle[a.lifecycle]}</dd>
              <dt>{t.record.fields.visibility}</dt>
              <dd>{t.visibility[a.visibility]}</dd>
              <dt>{t.record.fields.sourceOf}</dt>
              <dd>
                {a.sourceOf.length
                  ? a.sourceOf.map((o) => (
                      <Link key={o} className="och" to={paths.lineage(o)} style={{ marginRight: 6 }}>
                        {m.objects.get(o) && <Sdot s={m.objects.get(o)!.sensitivity} />}
                        {m.objects.get(o)?.name ?? o}
                      </Link>
                    ))
                  : '—'}
              </dd>
              <dt>{t.record.fields.completeness}</dt>
              <dd>
                <span
                  className={'meter' + (a.completeness < 0.65 ? ' low' : '')}
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
                {date(a.updatedAt)} · {t.record.fields.version} {a.version}
              </dd>
            </dl>
          </section>
        </div>
      )}
    </PageShell>
  );
}
