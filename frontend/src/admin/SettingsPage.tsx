import { useEffect, useState, type FormEvent } from 'react';
import { api, ApiError } from '../api/client';
import type { Language, Settings } from '../api/types';
import { documentTitle, useSettings } from '../app/settings';
import { useAuth } from '../auth/auth';
import { useI18n } from '../i18n';
import { describe, Field, PageShell, serverErrors } from '../records/common';
import { IconLogo, IconWarn } from '../ui/icons';
import { useToast } from '../ui/Toast';

export function validateSettings(t: ReturnType<typeof useI18n>['t'], s: Settings): Record<string, string> {
  const e: Record<string, string> = {};
  if (!s.instanceName.trim()) e.instanceName = t.record.errors.required;
  else if (s.instanceName.trim().length > 60) e.instanceName = t.record.errors.tooLong(60);
  if (s.organisationName.trim().length > 80) e.organisationName = t.record.errors.tooLong(80);
  return e;
}

/** Administrators name the instance and the organisation; both show in the header and the browser tab. */
export function SettingsPage() {
  const { t } = useI18n();
  const { isAdmin } = useAuth();
  const { setSettings } = useSettings();
  const toast = useToast();
  const [d, setD] = useState<Settings | null>(null);
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [failure, setFailure] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    if (!isAdmin) return;
    api
      .settings()
      .then(setD)
      .catch((e: unknown) =>
        setFailure(e instanceof ApiError && e.status === 0 ? t.common.networkError : t.common.genericError),
      );
    // load once per visit
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [isAdmin]);

  if (!isAdmin)
    return (
      <PageShell narrow title={t.settings.title}>
        <div className="page-h">
          <div className="ph-t">
            <h1>{t.settings.title}</h1>
          </div>
        </div>
        <div className="banner" role="note">
          <IconWarn />
          <span className="bt">{t.settings.forbidden}</span>
        </div>
      </PageShell>
    );

  const set = <K extends keyof Settings>(k: K, v: Settings[K]) => setD((x) => (x ? { ...x, [k]: v } : x));
  const submit = async (e: FormEvent) => {
    e.preventDefault();
    if (!d) return;
    const er = validateSettings(t, d);
    setErrors(er);
    setFailure(null);
    if (Object.keys(er).length) {
      document.getElementById(Object.keys(er)[0])?.focus();
      return;
    }
    setBusy(true);
    try {
      const saved = await api.updateSettings({
        ...d,
        instanceName: d.instanceName.trim(),
        organisationName: d.organisationName.trim(),
      });
      setD(saved);
      setSettings(saved);
      toast(t.settings.saved);
    } catch (err) {
      if (err instanceof ApiError && err.status === 400 && Object.keys(err.fieldErrors).length)
        setErrors(serverErrors(err));
      else if (err instanceof ApiError && err.isConflict) setFailure(t.record.conflictText);
      else setFailure(err instanceof ApiError ? err.detail || err.message : t.common.genericError);
    } finally {
      setBusy(false);
    }
  };

  return (
    <PageShell narrow title={t.settings.title}>
      <div className="page-h">
        <div className="ph-t">
          <h1>{t.settings.title}</h1>
          <p>{t.settings.intro}</p>
        </div>
      </div>
      {failure && (
        <div className="banner danger" role="alert">
          <IconWarn />
          <span className="bt">{failure}</span>
        </div>
      )}
      {!d && !failure && (
        <div className="box" aria-busy="true">
          <span className="sk line" style={{ width: 140 }} />
          <span className="sk block" style={{ height: 40, marginTop: 10 }} />
          <span className="sk line" style={{ width: 160, marginTop: 18 }} />
          <span className="sk block" style={{ height: 40, marginTop: 10 }} />
        </div>
      )}
      {d && (
        <form className="form box" onSubmit={(e) => void submit(e)} noValidate aria-labelledby="set-h">
          <h2 id="set-h">{t.settings.identity}</h2>
          <div className="fgrid">
            <Field
              id="instanceName"
              label={t.settings.instanceName}
              required
              hint={t.settings.instanceHint}
              error={errors.instanceName}
            >
              <input
                className="input"
                {...describe('instanceName', t.settings.instanceHint, errors.instanceName)}
                value={d.instanceName}
                maxLength={70}
                onChange={(e) => set('instanceName', e.target.value)}
                autoComplete="off"
              />
            </Field>
            <Field
              id="organisationName"
              label={t.settings.organisationName}
              hint={t.settings.organisationHint}
              error={errors.organisationName}
            >
              <input
                className="input"
                {...describe('organisationName', t.settings.organisationHint, errors.organisationName)}
                value={d.organisationName}
                maxLength={90}
                onChange={(e) => set('organisationName', e.target.value)}
                autoComplete="organization"
              />
            </Field>
            <Field id="defaultLanguage" label={t.settings.defaultLanguage} hint={t.settings.defaultLanguageHint}>
              <select
                className="select"
                {...describe('defaultLanguage', t.settings.defaultLanguageHint)}
                value={d.defaultLanguage}
                onChange={(e) => set('defaultLanguage', e.target.value as Language)}
              >
                <option value="en">English</option>
                <option value="fr">Français</option>
              </select>
            </Field>
          </div>
          <div className="preview" aria-label={t.settings.preview}>
            <span className="muted">{t.settings.preview}</span>
            <div className="preview-top">
              <span className="logo" aria-hidden="true">
                <IconLogo />
              </span>
              <span className="brand-t">
                <b>{d.instanceName.trim() || t.brand.name}</b>
                <small>{d.organisationName.trim() || t.brand.sub}</small>
              </span>
            </div>
            <small className="muted">
              {t.settings.tab}: {documentTitle(d.instanceName)}
            </small>
          </div>
          <div className="form-actions">
            <button type="submit" className="btn primary" disabled={busy}>
              {busy ? t.common.saving : t.common.save}
            </button>
          </div>
        </form>
      )}
    </PageShell>
  );
}
