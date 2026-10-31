import { useEffect, useState, type FormEvent } from 'react';
import { Link as RouterLink, useSearchParams } from 'react-router-dom';
import { useGo } from '../ui/useGo';
import { api, ApiError } from '../api/client';
import { LINK_CAPABILITIES, type LinkCapability, type LinkCode } from '../api/types';
import { clock, copyText } from '../agents/bits';
import { useAuth } from '../auth/auth';
import { useI18n } from '../i18n';
import { PageShell } from '../records/common';
import {
  IconCheck,
  IconClock,
  IconCopy,
  IconDoc,
  IconHourglass,
  IconSettings,
  IconTerminal,
  IconWarn,
} from '../ui/icons';
import { useToast } from '../ui/Toast';
import { CapChips, capText, useCountdown } from './bits';
import { AdminOnly, LinksHeader, usePeopleNames } from './LinksPage';

/** How often the waiting step asks whether the code was used. */
export const POLL_MS = 3000;
const LIFETIME_MIN = 15;

interface Shown {
  id: string;
  /** The full code, known only right after creation. */
  code: string | null;
  codeId: string;
  expiresAt: string;
  capabilities: string[];
  pairUrl: string;
  createdBy: string;
  createdAt: string;
}

function Steps({ step }: { step: 1 | 2 }) {
  const { t } = useI18n();
  const c = t.links.code;
  return (
    <ol className="steps3 lk-steps">
      <li className={step === 1 ? 'now' : 'done'}>
        <span className="sd2">{step === 1 ? '1' : <IconCheck size={14} />}</span>
        <span>
          <b>{step === 1 ? c.s1 : c.created}</b>
          {step === 1 && <small>{c.s1Hint}</small>}
        </span>
      </li>
      <li className={step === 2 ? 'now' : ''}>
        <span className="sd2">2</span>
        <span>
          <b>{c.s2}</b>
          <small>
            {step === 2 ? (
              c.s2Now
            ) : (
              <>
                {c.s2Hint}{' '}
                <a className="lk-a" href={t.links.protocolUrl} target="_blank" rel="noopener noreferrer">
                  {c.s2HintLink}
                </a>
                .
              </>
            )}
          </small>
        </span>
      </li>
      <li>
        <span className="sd2">3</span>
        <span>
          <b>{c.s3}</b>
          {step === 1 && <small>{c.s3Hint}</small>}
        </span>
      </li>
    </ol>
  );
}

function CreateStep({ onCreated }: { onCreated: (s: Shown) => void }) {
  const { t } = useI18n();
  const { me } = useAuth();
  const toast = useToast();
  const nav = useGo();
  const c = t.links.code;
  const [caps, setCaps] = useState<LinkCapability[]>(LINK_CAPABILITIES);
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState<string | null>(null);
  const submit = async (e: FormEvent) => {
    e.preventDefault();
    if (caps.length === 0) {
      setErr(c.noneChosen);
      return;
    }
    setBusy(true);
    try {
      const r = await api.createLinkCode(caps);
      onCreated({
        id: r.id,
        code: r.code,
        codeId: r.codeId,
        expiresAt: r.expiresAt,
        capabilities: r.capabilities,
        pairUrl: r.pairUrl,
        createdBy: me?.displayName ?? '',
        createdAt: new Date().toISOString(),
      });
    } catch (x) {
      if (x instanceof ApiError && x.fieldErrors.capabilities) setErr(x.fieldErrors.capabilities[0]);
      else toast(x instanceof ApiError && x.detail ? x.detail : t.common.genericError);
    } finally {
      setBusy(false);
    }
  };
  const toggle = (cap: LinkCapability, on: boolean) => {
    setErr(null);
    setCaps((cs) => LINK_CAPABILITIES.filter((x) => (x === cap ? on : cs.includes(x))));
  };
  return (
    <div className="split2">
      <div>
        <section className="box" aria-labelledby="pc-h">
          <form onSubmit={(e) => void submit(e)} noValidate>
            <div className="set-hd">
              <h2 id="pc-h" className="big">
                {t.links.pair}
              </h2>
              <span className="muted2">{c.stepOf(1)}</span>
            </div>
            <Steps step={1} />
            <span className="lbl2" id="cap-l">
              {c.may}
            </span>
            <div className="rcards" role="group" aria-labelledby="cap-l">
              {LINK_CAPABILITIES.map((cap) => {
                const [name, text] = capText(t, cap);
                return (
                  <label key={cap} className="rcard">
                    <input
                      type="checkbox"
                      checked={caps.includes(cap)}
                      onChange={(e) => toggle(cap, e.target.checked)}
                    />
                    <b>{name}</b>
                    <small>
                      {text} <span className="mono">{cap}</span>
                    </small>
                  </label>
                );
              })}
            </div>
            {err && (
              <span className="lk-err" role="alert">
                <IconWarn size={13} />
                {err}
              </span>
            )}
            <p className="muted2" style={{ margin: '8px 0 0' }}>
              {c.mayHint}
            </p>
            <div className="callout">
              <IconClock size={15} />
              <span>{c.expiry(LIFETIME_MIN)}</span>
            </div>
            <div className="set-f">
              <button type="button" className="btn" onClick={() => nav('/admin/links')}>
                {c.cancel}
              </button>
              <button type="submit" className="btn primary" disabled={busy}>
                {busy ? c.creating : c.create}
              </button>
            </div>
          </form>
        </section>
      </div>
      <div>
        <section className="box" aria-labelledby="cl-h">
          <h2 id="cl-h">{c.cliTitle}</h2>
          <p className="cn-p">{c.cliText}</p>
          <div className="cbw">
            <pre className="codeblock wrapc">{'noosphere link code --expires 15m\nnoosphere link list'}</pre>
          </div>
          <p className="muted2" style={{ marginTop: 12 }}>
            {c.bootstrap}
          </p>
        </section>
      </div>
    </div>
  );
}

function WaitStep({ shown, onAgain }: { shown: Shown; onAgain: () => void }) {
  const { t, lang } = useI18n();
  const toast = useToast();
  const nav = useGo();
  const c = t.links.code;
  const { left, over } = useCountdown(shown.expiresAt);
  const [state, setState] = useState<LinkCode['state']>('active');

  useEffect(() => {
    let alive = true;
    const check = async () => {
      try {
        const code = (await api.linkCodes()).find((x) => x.id === shown.id);
        if (!alive || !code) return;
        setState(code.state);
        if (code.state === 'used' && code.linkId) nav(`/admin/links/${encodeURIComponent(code.linkId)}`);
      } catch {
        /* try again at the next tick */
      }
    };
    void check();
    const id = window.setInterval(() => void check(), POLL_MS);
    return () => {
      alive = false;
      window.clearInterval(id);
    };
  }, [shown.id, nav]);

  const copy = async () => {
    if (!shown.code) return;
    toast((await copyText(shown.code)) ? c.copied : c.copyFailed);
  };
  const ended = state === 'expired' || state === 'burnt' || (over && state === 'active');
  const example = `<app> link pair ${shown.pairUrl} ${shown.code ?? shown.codeId + '…'}`;
  return (
    <div className="split2">
      <div>
        <section className="box" aria-labelledby="pw-h">
          <div className="set-hd">
            <h2 id="pw-h" className="big">
              {t.links.pair}
            </h2>
            <span className="muted2">{c.stepOf(2)}</span>
          </div>
          <ol className="steps3 lk-steps">
            <li className="done">
              <span className="sd2">
                <IconCheck size={14} />
              </span>
              <span>
                <b>{c.created}</b>
                <small>{c.createdBy(shown.createdBy, clock(shown.createdAt, lang))}</small>
              </span>
            </li>
            <li className="now">
              <span className="sd2">2</span>
              <span>
                <b>{c.s2}</b>
                <small>{c.s2Now}</small>
              </span>
            </li>
            <li>
              <span className="sd2">3</span>
              <span>
                <b>{c.s3}</b>
              </span>
            </li>
          </ol>
          <div className="pcode-w">
            <span className="lbl2">{c.label}</span>
            <div className="pcode">
              <span className="mono">{shown.code ?? `${shown.codeId}-····-····`}</span>
              {shown.code && (
                <button type="button" className="btn sm ghost" aria-label={c.copyLabel} onClick={() => void copy()}>
                  <IconCopy />
                  {c.copy}
                </button>
              )}
            </div>
            {!shown.code && <p className="muted2">{c.hidden}</p>}
            <p className="muted2">
              {!ended && (
                <span className="pchip warn">
                  <IconHourglass size={11} />
                  {c.expiresIn(left)}
                </span>
              )}{' '}
              {c.singleUse} <span className="mono">{shown.pairUrl}</span>
            </p>
          </div>
          {ended ? (
            <div className="banner warn lk-ended" role="alert">
              <IconWarn />
              <div className="bt">
                <b>{state === 'burnt' ? c.burnt : c.expired}</b>
                <div className="page-actions" style={{ marginTop: 10 }}>
                  <button type="button" className="btn primary" onClick={onAgain}>
                    {c.newCode}
                  </button>
                </div>
              </div>
            </div>
          ) : (
            <div className="lk-wait" role="status">
              <span className="spin" aria-hidden="true" />
              <span>
                <b>{c.waiting}</b>
                <small>{c.waitingHint}</small>
              </span>
            </div>
          )}
          <div className="lk-f">
            <span className="lk-al">
              {c.allowed} <CapChips caps={shown.capabilities} />
            </span>
            <RouterLink className="btn sm" to="/admin/links" title={c.closeHint}>
              {c.close}
            </RouterLink>
          </div>
        </section>
      </div>
      <div>
        <section className="box" aria-labelledby="gw-h">
          <h2 id="gw-h">{c.otherTitle}</h2>
          <ul className="can">
            <li>
              <IconSettings />
              <span>
                <b>{c.otherSettings}</b>
                <small>{c.otherSettingsHint}</small>
              </span>
            </li>
            <li>
              <IconTerminal />
              <span>
                <b>{c.otherServer}</b>
                <small>
                  {c.otherServerHint} <span className="mono">{example}</span>
                </small>
              </span>
            </li>
          </ul>
          <div className="lk-dev lk-dev-side">
            <IconDoc />
            <span>
              {c.builder}{' '}
              <a className="lk-a" href={t.links.protocolUrl} target="_blank" rel="noopener noreferrer">
                {t.links.code.s2HintLink} ({t.links.protocolPath})
              </a>{' '}
              {c.builderEnd}
            </span>
          </div>
        </section>
      </div>
    </div>
  );
}

function Pairing() {
  const { t } = useI18n();
  const [params, setParams] = useSearchParams();
  const nameOf = usePeopleNames();
  const codeParam = params.get('code');
  const [shown, setShown] = useState<Shown | null>(null);
  const [missing, setMissing] = useState(false);

  // A code created earlier: its value is not known any more, only its id.
  useEffect(() => {
    if (!codeParam || shown?.id === codeParam) return;
    let alive = true;
    setMissing(false);
    api
      .linkCodes()
      .then((cs) => {
        if (!alive) return;
        const c = cs.find((x) => x.id === codeParam);
        if (!c) return setMissing(true);
        setShown({
          id: c.id,
          code: null,
          codeId: c.codeId,
          expiresAt: c.expiresAt,
          capabilities: c.capabilities,
          pairUrl: location.origin,
          createdBy: c.bootstrap ? t.links.detail.bootstrapCode : nameOf(c.createdBy),
          createdAt: c.createdAt,
        });
      })
      .catch(() => alive && setMissing(true));
    return () => {
      alive = false;
    };
    // nameOf changes once the people are loaded; the name is refreshed on the next visit.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [codeParam]);

  return (
    <PageShell wide title={t.links.pair}>
      <LinksHeader />
      {missing && codeParam && (
        <div className="banner warn" role="alert">
          <IconWarn />
          <span className="bt">{t.links.code.notFound}</span>
        </div>
      )}
      {shown ? (
        <WaitStep
          shown={shown}
          onAgain={() => {
            setShown(null);
            setParams({});
          }}
        />
      ) : codeParam && !missing ? (
        <div aria-busy="true">
          <span className="sk block" style={{ height: 320 }} />
        </div>
      ) : (
        <CreateStep
          onCreated={(s) => {
            setShown(s);
            setParams({ code: s.id }, { replace: true });
          }}
        />
      )}
    </PageShell>
  );
}

/** Pairing an application: create a code with its capabilities, then wait until the other application uses it. */
export function PairPage() {
  return (
    <AdminOnly>
      <Pairing />
    </AdminOnly>
  );
}
