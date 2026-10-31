import { useEffect, useRef, useState, type ReactNode } from 'react';
import { Link, useLocation } from 'react-router-dom';
import { api, ApiError } from '../api/client';
import {
  isRestrictedApp,
  isRestrictedFlow,
  type DemoAgentScenario,
  type DemoNotice,
  type DemoPersona,
  type Proposal,
  type Role,
} from '../api/types';
import { initials, localized } from '../agents/bits';
import { useSettings } from '../app/settings';
import { useAuth } from '../auth/auth';
import { usePages } from '../docpages/store';
import { useLandscape } from '../explorer/landscape';
import { useI18n } from '../i18n';
import { useInbox } from '../review/inbox';
import {
  IconArrow,
  IconBotFull,
  IconCheck,
  IconEdit,
  IconEye,
  IconFlask,
  IconGauge,
  IconInfo,
  IconLock,
  IconReset,
  IconReview,
  IconSend,
  IconUser,
  IconUsers,
  IconX,
} from '../ui/icons';
import { useDemo, type Notice, type NoticeIcon, type NoticeTone } from './context';

type RecordKindLite = 'application' | 'flow' | 'dataObject' | 'page';
export interface PageRecord {
  kind: RecordKindLite;
  id: string;
}

/** The record the current page shows, if any: the target of "A colleague edits…". */
export function recordOfPath(path: string): PageRecord | null {
  const m = /^\/(applications|flows|pages|data-objects)\/([^/]+)/.exec(path);
  if (!m || m[2] === 'new') return null;
  const kind: RecordKindLite =
    m[1] === 'applications' ? 'application' : m[1] === 'flows' ? 'flow' : m[1] === 'pages' ? 'page' : 'dataObject';
  return { kind, id: decodeURIComponent(m[2]) };
}

const ROLE_RANK: Record<Role, number> = { member: 0, admin: 1, guest: 2 };
/** Members first, then the administrator, then the guest, as the toolbox shows them. */
export const sortPersonas = (ps: DemoPersona[]) =>
  [...ps].sort((a, b) => ROLE_RANK[a.role] - ROLE_RANK[b.role] || a.username.localeCompare(b.username));

/** The guest-read demo reads the record shared with the guest. */
const GUEST_RECORD: PageRecord = { kind: 'application', id: 'portal' };

const ICONS: Record<NoticeIcon, (p: { size?: number }) => ReactNode> = {
  user: IconUser,
  users: IconUsers,
  review: IconReview,
  check: IconCheck,
  x: IconX,
  eye: IconEye,
  bot: IconBotFull,
  lock: IconLock,
  gauge: IconGauge,
  reset: IconReset,
};

function Notices() {
  const { t } = useI18n();
  const { notices, dismiss } = useDemo();
  const { isAdmin } = useAuth();
  return (
    <div className="notices" aria-live="polite" aria-label={t.agents.demo.notices}>
      {notices.map((n: Notice) => {
        const I = ICONS[n.icon];
        const link = n.link && (!n.link.startsWith('/admin') || isAdmin) ? n.link : null;
        return (
          <div key={n.id} className="notice" role="status">
            <span className={'ni ' + n.tone} aria-hidden="true">
              <I size={14} />
            </span>
            <b>{n.title}</b>
            <span className="nd">
              {n.detail}
              {link && (
                <>
                  {' '}
                  <Link to={link} onClick={() => dismiss(n.id)}>
                    {t.agents.demo.showMe}
                  </Link>
                </>
              )}
            </span>
            <button type="button" className="nx" aria-label={t.agents.demo.dismiss} onClick={() => dismiss(n.id)}>
              <IconX size={12} />
            </button>
          </div>
        );
      })}
    </div>
  );
}

/**
 * The demo toolbox: a floating "Demo" button opening a panel (a bottom sheet on phones) to switch persona and make
 * colleagues, validators, an AI agent and the guest act. Rendered only in demo mode.
 */
export function DemoToolbox() {
  const { t, lang } = useI18n();
  const demo = useDemo();
  const { me } = useAuth();
  const { model, reload: reloadLandscape } = useLandscape();
  const { byId: pageById } = usePages();
  const { refresh: refreshInbox } = useInbox();
  const { settings } = useSettings();
  const loc = useLocation();
  const [busy, setBusy] = useState<string | null>(null);
  const [resetting, setResetting] = useState(false);
  const [mine, setMine] = useState<Proposal | null | undefined>(undefined);
  const panel = useRef<HTMLElement>(null);
  const fab = useRef<HTMLButtonElement>(null);
  const { open, setOpen } = demo;

  useEffect(() => {
    if (!open || !me || me.role === 'guest') return;
    let alive = true;
    api
      .proposals({ mine: true, status: 'submitted' })
      .then((ps) => alive && setMine(ps[0] ?? null))
      .catch(() => alive && setMine(null));
    return () => {
      alive = false;
    };
  }, [open, me, demo.tick]);

  useEffect(() => {
    if (!open) return;
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        setOpen(false);
        fab.current?.focus();
      }
    };
    document.addEventListener('keydown', onKey);
    panel.current?.focus();
    return () => document.removeEventListener('keydown', onKey);
  }, [open, setOpen]);

  if (!demo.on || !me) return null;

  const org = settings.organisationName.trim() || settings.instanceName.trim() || t.brand.name;
  const nameOf = (r: PageRecord | null): string | null => {
    if (!r) return null;
    if (r.kind === 'application') {
      const a = model?.apps.get(r.id);
      return a && !isRestrictedApp(a) ? a.name : null;
    }
    if (r.kind === 'flow') {
      const f = model?.flow.get(r.id);
      return f && !isRestrictedFlow(f) ? f.name : null;
    }
    if (r.kind === 'dataObject') return model?.objects.get(r.id)?.name ?? null;
    const p = pageById.get(r.id);
    return p && 'title' in p && p.title ? p.title : null;
  };
  // The record on screen, or else the first application of one of my teams.
  const here = recordOfPath(loc.pathname);
  const hereName = nameOf(here);
  const myTeams = new Set(me.teams?.map((x) => x.id) ?? []);
  const fallbackId =
    model?.appIds.find((id) => {
      const a = model.apps.get(id);
      return a && !isRestrictedApp(a) && a.teamId && myTeams.has(a.teamId);
    }) ?? model?.appIds.find((id) => !isRestrictedApp(model.apps.get(id)!));
  const editTarget: PageRecord | null =
    here && hereName ? here : fallbackId ? { kind: 'application', id: fallbackId } : null;
  const editName = nameOf(editTarget) ?? '—';
  const guestTarget = nameOf(GUEST_RECORD) ? GUEST_RECORD : editTarget;
  const guestName = nameOf(guestTarget) ?? '—';
  const guest = demo.state?.personas.find((p) => p.role === 'guest');
  const isGuest = me.role === 'guest';
  const personas = sortPersonas(demo.state?.personas ?? []);

  const run = async (
    key: string,
    call: () => Promise<{ notice: DemoNotice }>,
    tone: NoticeTone,
    icon: NoticeIcon,
    after: { landscape?: boolean; remount?: boolean } = {},
  ) => {
    setBusy(key);
    try {
      const r = await call();
      demo.notify(r.notice, tone, icon);
      refreshInbox();
      if (after.landscape) reloadLandscape();
      demo.changed(after.remount);
    } catch (e) {
      demo.notify(
        {
          title: t.agents.demo.failed,
          detail:
            e instanceof ApiError
              ? e.status === 0
                ? t.common.networkError
                : e.detail || e.message
              : t.common.genericError,
          link: null,
        },
        'dg',
        'x',
      );
    } finally {
      setBusy(null);
    }
  };
  const agent = (s: DemoAgentScenario, tone: NoticeTone, icon: NoticeIcon) =>
    void run('agent:' + s, () => api.demoAgent(s), tone, icon);

  const switchTo = async (username: string) => {
    if (username === me.username) return;
    setBusy('switch:' + username);
    try {
      await demo.switchTo(username);
    } catch (e) {
      demo.notify(
        { title: t.agents.demo.failed, detail: e instanceof ApiError ? e.detail || e.message : '', link: null },
        'dg',
        'x',
      );
    } finally {
      setBusy(null);
    }
  };

  const roleLine = (p: DemoPersona) =>
    [t.agents.demo.role[p.role], p.teams.map((x) => x.name).join(', ')].filter(Boolean).join(' · ');

  return (
    <div className={'demo-layer' + (open ? ' dp-open' : '')}>
      {open && <div className="dp-back" onClick={() => setOpen(false)} aria-hidden="true" />}
      {open && (
        <section
          ref={panel}
          className="dpanel"
          role="dialog"
          aria-modal="false"
          aria-labelledby="dp-title"
          tabIndex={-1}
        >
          <div className="dp-grip" aria-hidden="true" />
          <div className="dp-h">
            <span className="fl" aria-hidden="true">
              <IconFlask size={16} />
            </span>
            <b id="dp-title">{t.agents.demo.title}</b>
            <small>{t.agents.demo.subtitle(org)}</small>
            <button
              type="button"
              className="iconbtn"
              aria-label={t.agents.demo.close}
              onClick={() => {
                setOpen(false);
                fab.current?.focus();
              }}
            >
              <IconX size={16} />
            </button>
          </div>

          <div className="dsec">
            <div className="dsec-h">
              <h3>{t.agents.demo.beSomeone}</h3>
              <small>{t.agents.demo.noPassword}</small>
            </div>
            <div className="pgrid">
              {personas.map((p) => {
                const you = p.username === me.username;
                return (
                  <button
                    key={p.username}
                    type="button"
                    className="pcard2"
                    aria-pressed={you}
                    disabled={!!busy && busy !== 'switch:' + p.username}
                    aria-busy={busy === 'switch:' + p.username}
                    onClick={() => void switchTo(p.username)}
                  >
                    <span className="av" aria-hidden="true">
                      {initials(p.displayName)}
                    </span>
                    <b>
                      {p.displayName} {you && <span className="you2">{t.agents.demo.you}</span>}
                    </b>
                    <span className="rl">{roleLine(p)}</span>
                    <span className="try">
                      <i>{t.agents.demo.try}</i> {localized(p.whatToTry, lang)}
                    </span>
                  </button>
                );
              })}
              {!demo.state && [0, 1, 2, 3].map((i) => <span key={i} className="sk block" style={{ height: 110 }} />)}
            </div>
            <label className="stay">
              <input type="checkbox" checked={demo.stay} onChange={(e) => demo.setStay(e.target.checked)} />
              {t.agents.demo.stay}
            </label>
          </div>

          <div className="dsec">
            <div className="dsec-h">
              <h3>{t.agents.demo.simulate}</h3>
              <small>{here && hereName ? t.agents.demo.onThisPage(hereName) : t.agents.demo.onRecord(editName)}</small>
            </div>
            <div className="dacts">
              <button
                type="button"
                className="dact"
                disabled={!editTarget || !!busy}
                onClick={() =>
                  editTarget &&
                  void run('edit', () => api.demoEdit(editTarget.kind, editTarget.id), 'wr', 'users', {
                    landscape: true,
                  })
                }
              >
                <span className="di" aria-hidden="true">
                  <IconUsers size={16} />
                </span>
                <b>{t.agents.demo.edit(editName)}</b>
                <small>{t.agents.demo.editText}</small>
                <span className="go" aria-hidden="true">
                  {t.agents.demo.run}
                  <IconArrow size={12} />
                </span>
              </button>
              {!isGuest && (
                <button
                  type="button"
                  className="dact"
                  disabled={!!busy}
                  onClick={() => void run('proposal', () => api.demoProposal(), '', 'review')}
                >
                  <span className="di" aria-hidden="true">
                    <IconReview size={16} />
                  </span>
                  <b>{t.agents.demo.proposal}</b>
                  <small>{t.agents.demo.proposalText}</small>
                  <span className="go" aria-hidden="true">
                    {t.agents.demo.run}
                    <IconArrow size={12} />
                  </span>
                </button>
              )}
              {!isGuest && (
                <div className="dact-x">
                  <span className="di" aria-hidden="true">
                    <IconCheck size={16} />
                  </span>
                  <b>{t.agents.demo.decision}</b>
                  <small>
                    {mine === undefined ? (
                      <span className="sk line" style={{ width: '70%' }} />
                    ) : mine ? (
                      t.agents.demo.decisionOn(mine.title)
                    ) : (
                      t.agents.demo.decisionNone
                    )}
                  </small>
                  <span className="row3">
                    <button
                      type="button"
                      className="btn sm"
                      disabled={!mine || !!busy}
                      onClick={() =>
                        mine &&
                        void run('accept', () => api.demoDecision(mine.id, 'accept'), 'ok', 'check', {
                          landscape: true,
                        })
                      }
                    >
                      {t.agents.demo.accept}
                    </button>
                    <button
                      type="button"
                      className="btn sm"
                      disabled={!mine || !!busy}
                      onClick={() => mine && void run('refuse', () => api.demoDecision(mine.id, 'refuse'), 'dg', 'x')}
                    >
                      {t.agents.demo.refuse}
                    </button>
                  </span>
                </div>
              )}
              {guestTarget && (
                <button
                  type="button"
                  className="dact"
                  disabled={!!busy}
                  onClick={() =>
                    void run('guest', () => api.demoGuestRead(guestTarget.kind, guestTarget.id), '', 'eye')
                  }
                >
                  <span className="di" aria-hidden="true">
                    <IconEye size={16} />
                  </span>
                  <b>{t.agents.demo.guest}</b>
                  <small>{t.agents.demo.guestText(guest?.displayName ?? 'Claire', guestName)}</small>
                  <span className="go" aria-hidden="true">
                    {t.agents.demo.run}
                    <IconArrow size={12} />
                  </span>
                </button>
              )}
            </div>
          </div>

          <div className="dsec">
            <div className="dsec-h">
              <h3>{t.agents.demo.agent}</h3>
              <small>{t.agents.demo.agentRules}</small>
            </div>
            {isGuest ? (
              <p className="agent-for">
                <IconInfo size={14} />
                {t.agents.demo.agentGuest}
              </p>
            ) : (
              <>
                <p className="agent-for">
                  <IconBotFull size={14} />
                  {t.agents.demo.agentFor(me.displayName)}
                </p>
                <div className="agrid">
                  <button type="button" className="abtn" disabled={!!busy} onClick={() => agent('draft', 'ag', 'bot')}>
                    <IconEdit size={16} />
                    <b>{t.agents.demo.agentDraft}</b>
                    <small>{t.agents.demo.agentDraftText}</small>
                  </button>
                  <button type="button" className="abtn" disabled={!!busy} onClick={() => agent('submit', 'ag', 'bot')}>
                    <IconSend size={16} />
                    <b>{t.agents.demo.agentSubmit}</b>
                    <small>{t.agents.demo.agentSubmitText}</small>
                  </button>
                  <button
                    type="button"
                    className="abtn r"
                    disabled={!!busy}
                    onClick={() => agent('refused', 'dg', 'lock')}
                  >
                    <IconLock size={16} />
                    <b>{t.agents.demo.agentRefused}</b>
                    <small>{t.agents.demo.agentRefusedText}</small>
                  </button>
                  <button
                    type="button"
                    className="abtn q"
                    disabled={!!busy}
                    onClick={() => agent('quota', 'wr', 'gauge')}
                  >
                    <IconGauge size={16} />
                    <b>{t.agents.demo.agentQuota}</b>
                    <small>{t.agents.demo.agentQuotaText}</small>
                  </button>
                </div>
              </>
            )}
          </div>

          <div className="dsec">
            {!resetting ? (
              <button type="button" className="btn sm reset-b" onClick={() => setResetting(true)}>
                <IconReset size={14} />
                {t.agents.demo.reset}
              </button>
            ) : (
              <div className="reset-c" role="alert">
                <b>{t.agents.demo.resetTitle}</b>
                <ul>
                  <li>{t.agents.demo.reset1}</li>
                  <li>{t.agents.demo.reset2}</li>
                  <li>{t.agents.demo.reset3(me.displayName)}</li>
                </ul>
                <span className="row3">
                  <button
                    type="button"
                    className="btn sm danger-solid"
                    disabled={!!busy}
                    onClick={() =>
                      void run('reset', () => api.demoReset(), 'ok', 'reset', { remount: true }).then(() =>
                        setResetting(false),
                      )
                    }
                  >
                    {busy === 'reset' ? t.common.saving : t.agents.demo.reset}
                  </button>
                  <button type="button" className="btn sm" onClick={() => setResetting(false)}>
                    {t.agents.demo.cancel}
                  </button>
                </span>
              </div>
            )}
          </div>
          <div className="dp-f">
            <IconInfo size={13} />
            <span>{t.agents.demo.footer}</span>
          </div>
        </section>
      )}
      <button
        ref={fab}
        type="button"
        className="dfab"
        aria-label={t.agents.demo.fabLabel}
        aria-expanded={open}
        onClick={() => setOpen(!open)}
      >
        <span className="fl" aria-hidden="true">
          {open ? <IconX size={13} /> : <IconFlask size={15} />}
        </span>
        <span>{open ? t.agents.demo.fabClose : t.agents.demo.fab}</span>
      </button>
      <Notices />
    </div>
  );
}
