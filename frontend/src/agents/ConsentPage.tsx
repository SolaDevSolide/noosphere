import { useEffect, useState } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { api, ApiError } from '../api/client';
import type { ConsentRequest } from '../api/types';
import { useDocumentTitle, useSettings } from '../app/settings';
import { useAuth } from '../auth/auth';
import { useI18n } from '../i18n';
import { Brand, ThemeButton } from '../layout/TopBar';
import { LanguageSwitch } from '../layout/UserMenu';
import { IconBotFull, IconCheck, IconHourglass, IconInfo, IconPlug, IconWarn } from '../ui/icons';
import { useToast } from '../ui/Toast';
import { capClass, CapIcon, initials, localized, scopeNames } from './bits';

const LOOPBACK = /^(localhost|127\.\d+\.\d+\.\d+|\[::1\])(:\d+)?$/i;

/** Leaves the app for the agent's redirect URI (a separate function so tests can replace it). */
export const leave = { to: (url: string) => window.location.assign(url) };

/** The current time as HH:MM, for "requested at". */
const timeNow = (lang: string) =>
  new Intl.DateTimeFormat(lang === 'fr' ? 'fr-FR' : 'en-GB', { hour: '2-digit', minute: '2-digit' }).format(new Date());

type Phase = 'ask' | 'busy' | 'leaving' | 'waiting';

/**
 * The OAuth consent page (`/oauth/consent?request=…`): who asks, for whom, and what the agent will be allowed to do.
 * Errors about the client come as `?error=&error_description=`.
 */
export function ConsentPage() {
  const { t, lang } = useI18n();
  const { me, logout } = useAuth();
  const { settings } = useSettings();
  const toast = useToast();
  const navigate = useNavigate();
  const [q] = useSearchParams();
  const requestId = q.get('request');
  const urlError = q.get('error');
  const [req, setReq] = useState<ConsentRequest | null>(null);
  const [error, setError] = useState<ApiError | null>(null);
  const [drafts, setDrafts] = useState(true);
  const [phase, setPhase] = useState<Phase>('ask');
  const [message, setMessage] = useState<string | null>(null);
  const [redirectTo, setRedirectTo] = useState<string | null>(null);
  const [decidedAt, setDecidedAt] = useState<string | null>(null);
  useDocumentTitle(t.agents.consent.title);
  const org = settings.organisationName.trim() || settings.instanceName.trim() || t.brand.name;

  useEffect(() => {
    if (!requestId) return;
    let alive = true;
    api
      .consentRequest(requestId)
      .then((r) => alive && setReq(r))
      .catch((e: unknown) => alive && setError(e instanceof ApiError ? e : new ApiError(0, 'network')));
    return () => {
      alive = false;
    };
  }, [requestId]);

  const fail = (title: string, detail?: string | null) => (
    <div className="banner danger" role="alert">
      <IconWarn />
      <div className="bt">
        <b>{title}</b>
        {detail}
      </div>
    </div>
  );

  let body;
  if (urlError || !requestId) {
    body = fail(
      urlError === 'invalid_client' ? t.agents.consent.errorClient : t.agents.consent.errorTitle,
      q.get('error_description') ?? t.agents.consent.missing,
    );
  } else if (error) {
    body = fail(
      t.agents.consent.errorTitle,
      error.status === 404
        ? t.agents.consent.errorExpired
        : error.status === 409
          ? t.agents.consent.errorDecided
          : error.status === 403
            ? t.agents.consent.errorOff
            : error.status === 0
              ? t.common.networkError
              : error.detail || t.common.genericError,
    );
  } else if (!req || !me) {
    body = (
      <div aria-busy="true">
        <span className="sk line" style={{ width: 140 }} />
        <span className="sk title" style={{ width: '80%', marginTop: 12 }} />
        <span className="sk line" style={{ width: '90%', marginTop: 10 }} />
        <span className="sk block" style={{ height: 74, marginTop: 18 }} />
        <span className="sk block" style={{ height: 74, marginTop: 8 }} />
        <span className="sk line" style={{ width: '60%', marginTop: 22 }} />
        <span className="sk line" style={{ width: '75%', marginTop: 10 }} />
        <span className="sk line" style={{ width: '70%', marginTop: 10 }} />
      </div>
    );
  } else {
    const draftScope = req.scopes.find((s) => s.scope === 'docs:draft' && s.grantable);
    const granted = req.scopes.filter((s) => s.grantable && (s.scope !== 'docs:draft' || drafts)).map((s) => s.scope);
    const items = req.capabilities.filter((c) => !(c.key === 'draft' && draftScope && !drafts));
    const can = items.filter((c) => capClass(c) === 'ok');
    const cannot = items.filter((c) => capClass(c) !== 'ok');
    const loopback = LOOPBACK.test(req.redirectHost);
    const site = (() => {
      for (const u of [req.clientUri, req.clientKind === 'metadataDocument' ? req.clientId : null]) {
        try {
          if (u) return new URL(u).host;
        } catch {
          /* not a URL */
        }
      }
      return null;
    })();
    const teams = me.teams?.map((x) => x.name).join(', ');

    const decide = async (allow: boolean) => {
      setPhase('busy');
      setMessage(null);
      try {
        const r = allow
          ? await api.approveConsent(req.id, draftScope && !drafts ? granted : undefined)
          : await api.denyConsent(req.id);
        setRedirectTo(r.redirectTo);
        if (allow && r.pendingApproval) {
          setDecidedAt(timeNow(lang));
          setPhase('waiting');
          return;
        }
        setPhase('leaving');
        setMessage(allow ? t.agents.consent.redirecting(req.clientName) : t.agents.consent.denied(req.clientName));
        leave.to(r.redirectTo);
      } catch (e) {
        setPhase('ask');
        setError(e instanceof ApiError ? e : new ApiError(0, 'network'));
      }
    };

    const cancel = async () => {
      setPhase('busy');
      try {
        const mine = await api.myAgents();
        const c = mine.connections
          .filter((x) => x.state === 'pending' && x.clientId === req.clientId)
          .sort((a, b) => b.createdAt.localeCompare(a.createdAt))[0];
        if (c) await api.revokeAgent(c.id);
        toast(t.agents.consent.cancelled);
        void navigate('/my-agents');
      } catch {
        setPhase('waiting');
        toast(t.common.genericError);
      }
    };

    if (phase === 'waiting' && redirectTo)
      body = (
        <div>
          <span className="eyebrow wait">
            <IconHourglass size={14} />
            {t.agents.consent.waitEyebrow}
          </span>
          <h1 id="cs-h">{t.agents.consent.waitTitle(req.clientName)}</h1>
          <p className="lede">{t.agents.consent.waitLede(org)}</p>
          <div className="idcard">
            <span className="tile">
              <IconBotFull size={22} />
            </span>
            <b>
              {req.clientName}{' '}
              <span className="pchip warn">
                <IconHourglass size={11} />
                {t.agents.consent.waiting}
              </span>
            </b>
            <span className="sub">{t.agents.consent.requestedAt(req.person.displayName, decidedAt ?? '')}</span>
          </div>
          <ol className="steps3">
            <li className="done">
              <span className="sd2">
                <IconCheck size={13} />
              </span>
              <span>
                <b>{t.agents.consent.step1}</b>
                <small>{t.agents.consent.step1Text(scopeNames(t, granted).join(', '))}</small>
              </span>
            </li>
            <li className="now">
              <span className="sd2">
                <IconHourglass size={13} />
              </span>
              <span>
                <b>{t.agents.consent.step2}</b>
                <small>{t.agents.consent.step2Text}</small>
              </span>
            </li>
            <li>
              <span className="sd2">
                <IconPlug size={13} />
              </span>
              <span>
                <b>{t.agents.consent.step3(req.clientName)}</b>
                <small>{t.agents.consent.step3Text}</small>
              </span>
            </li>
          </ol>
          <div className="consent-f">
            <button type="button" className="btn" onClick={() => void cancel()}>
              {t.agents.consent.cancel}
            </button>
            <button type="button" className="btn primary" onClick={() => leave.to(redirectTo)}>
              {t.agents.consent.continueTo(req.clientName)}
            </button>
          </div>
        </div>
      );
    else
      body = (
        <div>
          <span className="eyebrow">
            <IconBotFull size={14} />
            {t.agents.consent.eyebrow}
          </span>
          <h1 id="cs-h">{t.agents.consent.heading(req.clientName)}</h1>
          <p className="lede">{t.agents.consent.lede}</p>
          {message && (
            <div className="banner ok" role="status">
              <IconCheck />
              <span className="bt">{message}</span>
            </div>
          )}
          <div className="idcard">
            <span className="tile">
              <IconBotFull size={22} />
            </span>
            <b>
              {req.clientName}{' '}
              {req.clientKind === 'metadataDocument' ? (
                <span className="pchip ok">
                  <IconCheck size={11} />
                  {t.agents.consent.verified}
                </span>
              ) : (
                <span className="pchip muted">{t.agents.consent.selfRegistered}</span>
              )}
            </b>
            <span className="sub">
              {site && (
                <>
                  {t.agents.consent.from} <b className="inl">{site}</b> ·{' '}
                </>
              )}
              {t.agents.consent.returnsTo} <span className="mono">{req.redirectHost}</span>
              {loopback && t.agents.consent.onThisComputer}
            </span>
          </div>
          <div className="idcard">
            <span className="tile person">{initials(req.person.displayName)}</span>
            <b>
              <span className="lbl">{t.agents.consent.actingFor}</span> {req.person.displayName}
            </b>
            <span className="sub">
              {req.person.username} · {t.top.role[me.role]}
              {teams && ` · ${teams}`} ·{' '}
              <button
                type="button"
                className="linkbtn inl"
                onClick={() => {
                  const next = location.pathname + location.search;
                  void logout().then(() => navigate(`/login?next=${encodeURIComponent(next)}`));
                }}
              >
                {t.agents.consent.notYou}
              </button>
            </span>
          </div>
          {req.alreadyConnected && (
            <div className="callout agentc">
              <IconInfo size={15} />
              <span>{t.agents.consent.already(req.clientName)}</span>
            </div>
          )}
          <h2 className="can-h">{t.agents.consent.canTitle}</h2>
          <ul className="can">
            {can.map((c) => (
              <li key={c.key} className="ok">
                <CapIcon cls="ok" />
                <span>{localized(c.text, lang)}</span>
              </li>
            ))}
          </ul>
          {draftScope && (
            <label className="stay">
              <input type="checkbox" checked={drafts} onChange={(e) => setDrafts(e.target.checked)} />
              {t.agents.consent.allowDrafts}
            </label>
          )}
          <h2 className="can-h">{t.agents.consent.cannotTitle}</h2>
          <ul className="can">
            {cannot.map((c) => (
              <li key={c.key} className={capClass(c)}>
                <CapIcon cls={capClass(c)} />
                <span>{localized(c.text, lang)}</span>
              </li>
            ))}
          </ul>
          {req.approvalRequired && (
            <div className="callout">
              <IconHourglass size={15} />
              <span>{t.agents.consent.approvalNote}</span>
            </div>
          )}
          <div className="consent-f">
            <button type="button" className="btn" disabled={phase !== 'ask'} onClick={() => void decide(false)}>
              {t.agents.consent.deny}
            </button>
            <button
              type="button"
              className="btn primary"
              disabled={phase !== 'ask' || granted.length === 0}
              onClick={() => void decide(true)}
            >
              {phase === 'busy' ? t.agents.consent.working : t.agents.consent.allow}
            </button>
          </div>
          <p className="consent-note">
            <IconInfo size={14} />
            <span>{t.agents.consent.note}</span>
          </p>
        </div>
      );
  }

  return (
    <div className="login consent-wrap">
      <div className="login-top">
        <LanguageSwitch small />
        <ThemeButton />
      </div>
      <main className="login-card consent" aria-label={t.agents.consent.title}>
        <Brand to="/" />
        {body}
        {(error || urlError || !requestId) && (
          <div className="consent-f one">
            <button type="button" className="btn" onClick={() => void navigate('/my-agents')}>
              {t.agents.consent.goMine}
            </button>
          </div>
        )}
      </main>
    </div>
  );
}
