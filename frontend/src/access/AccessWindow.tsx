import { useEffect, useRef, useState, type ReactNode } from 'react';
import { Link } from 'react-router-dom';
import { api, ApiError } from '../api/client';
import {
  isRestrictedPage,
  type AccessList,
  type PageRef,
  type RecordAccess,
  type RecordKind,
  type Sensitivity,
  type SensitivityReason,
  type Visibility,
} from '../api/types';
import { useSettings } from '../app/settings';
import { useAuth } from '../auth/auth';
import { pagePaths } from '../docpages/model';
import { usePages } from '../docpages/store';
import { Sdot, teamName } from '../explorer/bits';
import { useLandscape } from '../explorer/landscape';
import { paths } from '../explorer/paths';
import { useI18n } from '../i18n';
import {
  IconCheck,
  IconChevron,
  IconDoc,
  IconEye,
  IconEyeOff,
  IconLock,
  IconReview,
  IconShield,
  IconUser,
  IconUsers,
  IconWarn,
  IconX,
  TypeIcon,
} from '../ui/icons';

export type AccessPop = 'access' | 'sens' | null;

/** Closes a popover on Escape and on a press outside `wrap`. */
function usePopover(open: boolean, wrap: React.RefObject<HTMLElement | null>, close: () => void) {
  const fn = useRef(close);
  fn.current = close;
  useEffect(() => {
    if (!open) return;
    const key = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        e.stopPropagation();
        fn.current();
      }
    };
    const down = (e: PointerEvent) => {
      const tg = e.target as Element;
      if (wrap.current?.contains(tg) || tg.closest?.('.dlg-back, .drawer-back')) return;
      fn.current();
    };
    document.addEventListener('keydown', key, true);
    document.addEventListener('pointerdown', down);
    return () => {
      document.removeEventListener('keydown', key, true);
      document.removeEventListener('pointerdown', down);
    };
  }, [open, wrap]);
}

/** Loads GET /api/{kind}/{id}/access while the window is open; again when the record changes. */
function useRecordAccess(kind: RecordKind, id: string, version: number, open: boolean) {
  const [data, setData] = useState<{ key: string; value: RecordAccess } | null>(null);
  const [error, setError] = useState<string | null>(null);
  const key = `${kind}:${id}:${version}`;
  useEffect(() => {
    if (!open || data?.key === key) return;
    let alive = true;
    setError(null);
    api
      .recordAccess(kind, id)
      .then((value) => alive && setData({ key, value }))
      .catch((e: unknown) => alive && setError(e instanceof ApiError ? e.detail || e.message : 'error'));
    return () => {
      alive = false;
    };
  }, [open, key, kind, id, data?.key]);
  return { access: data?.key === key ? data.value : null, error };
}

function You({ no, children }: { no?: boolean; children: ReactNode }) {
  return <span className={'you' + (no ? ' no' : '')}>{children}</span>;
}

interface WindowProps {
  kind: RecordKind;
  id: string;
  name: string;
  version: number;
  open: boolean;
  onClose: () => void;
  /** Pages attached to the record, for the warning about public pages of a restricted record. */
  pages?: PageRef[];
  canPublish: boolean;
  onRestrict?: (mode: 'restrict' | 'guest') => void;
  onMakeEveryone?: () => void;
  onTransfer?: () => void;
}

/** The "Who can see and edit?" window: who reads, who publishes, who proposes, and who reads the protected notes. */
export function AccessWindow({
  kind,
  id,
  name,
  version,
  open,
  onClose,
  pages = [],
  canPublish,
  onRestrict,
  onMakeEveryone,
  onTransfer,
}: WindowProps) {
  const { t } = useI18n();
  const { me, isAdmin, teamIds } = useAuth();
  const { settings } = useSettings();
  const { byId } = usePages();
  const { access: a, error } = useRecordAccess(kind, id, version, open);
  const box = useRef<HTMLDivElement>(null);
  useEffect(() => {
    if (open) box.current?.querySelector<HTMLElement>('[data-autofocus]')?.focus({ preventScroll: true });
  }, [open]);
  if (!open) return null;
  const guest = me?.role === 'guest';
  const hId = `acc-h-${kind}`;
  const owner = a?.owningTeam ?? null;
  const restricted = a?.visibility === 'restricted';
  const inOwner = !!owner && teamIds.includes(owner.id);
  const listedTeam = a?.readers.teams.find((x) => x.id !== owner?.id && teamIds.includes(x.id));
  const recordKinds = kind === 'application' || kind === 'flow' || kind === 'page';
  const publicPages =
    restricted && kind === 'application'
      ? pages.filter((p) => {
          const s = byId.get(p.id);
          return s && !isRestrictedPage(s) && 'visibility' in s && s.visibility === 'everyone';
        })
      : [];
  const notesWho = owner ? owner.name : t.access.administrators;

  let body: ReactNode;
  if (error)
    body = (
      <div className="banner danger" role="alert">
        <IconWarn />
        <span className="bt">{t.access.loadError}</span>
      </div>
    );
  else if (!a)
    body = (
      <div aria-busy="true" aria-label={t.access.windowLoading}>
        {[0, 1, 2, 3].map((i) => (
          <span key={i} className="sk line" style={{ width: `${88 - i * 12}%`, marginTop: i ? 14 : 2 }} />
        ))}
      </div>
    );
  else
    body = (
      <>
        <ul className="can">
          {restricted ? (
            <>
              <li className="lock">
                <IconLock size={16} />
                <span>
                  <b>{t.access.restrictedLead}</b>
                  {t.access.onlyThese}
                  <span className="who-list">
                    {a.readers.teams.map((x) => (
                      <span key={x.id} className="wchip">
                        <IconUsers size={13} />
                        {x.name}{' '}
                        <small>{x.id === owner?.id ? `${t.access.ownerTag} · ${x.memberCount}` : x.memberCount}</small>
                      </span>
                    ))}
                    {a.readers.users.map((u) => (
                      <span key={u.username} className="wchip">
                        <IconUser size={13} />
                        {u.displayName}
                      </span>
                    ))}
                    <span className="wchip">
                      <IconShield size={13} />
                      {t.access.admins}
                    </span>
                  </span>
                  <small>
                    {isAdmin
                      ? t.access.youSeeAsAdmin
                      : inOwner
                        ? t.access.youSeeAsOwner(owner!.name)
                        : listedTeam
                          ? t.access.youSeeAsListedTeam(listedTeam.name)
                          : t.access.youSeeAsListed}
                  </small>
                </span>
              </li>
              <li>
                <IconEyeOff />
                <span>
                  {kind === 'flow'
                    ? `${t.access.othersSee.flow} ${t.access.flowRule}`
                    : t.access.othersSee[kind === 'page' ? 'page' : 'application']}
                </span>
              </li>
            </>
          ) : (
            <li>
              <IconEye />
              <span>
                {recordKinds ? (
                  <>
                    <b>{t.access.everyoneIn(settings.organisationName)}</b>
                    {t.access.canSeeExceptGuests}
                    {guest ? (
                      <small>
                        {t.access.guestsSeeName}
                        {t.access.sharedWithYouNote}
                      </small>
                    ) : (
                      a.readers.users.length > 0 && (
                        <small>
                          {t.access.sharedGuests(
                            a.readers.users.length,
                            a.readers.users.map((u) => u.displayName).join(', '),
                          )}
                        </small>
                      )
                    )}
                  </>
                ) : (
                  <>
                    <b>{t.access.everyone}</b>
                    {t.access.canSeeWithGuests}
                  </>
                )}
              </span>
            </li>
          )}
          {a.proposers === 'none' ? (
            <li className="ok">
              <IconShield size={16} />
              <span>
                {t.access.adminsChange}
                {isAdmin && <You>{t.access.youAreAdmin}</You>}
              </span>
            </li>
          ) : (
            <>
              <li className="ok">
                <IconCheck />
                <span>
                  {owner ? (
                    <>
                      <b>{owner.name}</b>
                      {t.access.publishesText(owner.memberCount)}
                    </>
                  ) : (
                    t.access.adminsOnlyPublish
                  )}
                  {inOwner ? <You>{t.access.youAreIn(owner!.name)}</You> : isAdmin && <You>{t.access.youAreAdmin}</You>}
                </span>
              </li>
              {guest ? (
                <li>
                  <IconReview />
                  <span>{t.access.guestsReadOnly}</span>
                </li>
              ) : (
                <li>
                  <IconReview />
                  <span>
                    {restricted ? (
                      t.access.othersAbove
                    ) : (
                      <>
                        <b>{t.access.anyoneElse}</b>
                        {t.access.canProposeText}
                      </>
                    )}
                    {!inOwner && !isAdmin && <You>{t.access.youCanPropose}</You>}
                  </span>
                </li>
              )}
            </>
          )}
          {a.protectedNotes.exists && (
            <li className="lock">
              <IconLock size={16} />
              <span>
                <b>{t.access.notesLead}</b>
                {restricted ? t.access.notesSamePeople : t.access.notesVisibleTo(notesWho)}
                {a.protectedNotes.readable ? <You>{t.access.youCanRead}</You> : <You no>{t.access.youCannotRead}</You>}
              </span>
            </li>
          )}
          {publicPages.length > 0 && (
            <li className="warn">
              <IconWarn size={16} />
              <span>
                {t.access.publicPages(publicPages.length, pages.length)}{' '}
                {publicPages.map((p, i) => (
                  <span key={p.id}>
                    {i > 0 && ', '}
                    <Link to={pagePaths.page(p.id)} onClick={onClose}>
                      {p.title}
                    </Link>
                  </span>
                ))}
                .{t.access.pagesOwn}
              </span>
            </li>
          )}
        </ul>
        {a.canManage ? (
          <div className="ap-f">
            {restricted ? (
              <>
                <button type="button" className="btn sm" onClick={() => onRestrict?.('restrict')}>
                  <IconLock />
                  {t.access.changeWho}
                </button>
                <button type="button" className="btn sm" onClick={onMakeEveryone}>
                  <IconEye size={14} />
                  {t.access.makeEveryone}
                </button>
              </>
            ) : (
              <button type="button" className="btn sm" onClick={() => onRestrict?.('restrict')}>
                <IconLock />
                {t.access.restrict}
              </button>
            )}
            {onTransfer && (
              <button type="button" className="btn sm" onClick={onTransfer}>
                {t.access.transfer}
              </button>
            )}
            {!restricted && (
              <button type="button" className="btn sm" onClick={() => onRestrict?.('guest')}>
                {t.access.shareGuest}
              </button>
            )}
          </div>
        ) : (
          canPublish &&
          onTransfer && (
            <div className="ap-f">
              <button type="button" className="btn sm" onClick={onTransfer}>
                {t.access.transfer}
              </button>
            </div>
          )
        )}
        {!a.canManage && recordKinds && !guest && (
          <p className="ap-note">
            {kind === 'flow'
              ? t.access.flowFollows
              : owner
                ? t.access.onlyOwnersChange(owner.name)
                : t.access.onlyAdminsChange}
          </p>
        )}
        {isAdmin && a.protectedNotes.exists && <p className="ap-note">{t.access.adminLogged}</p>}
      </>
    );

  return (
    <>
      <div className="pop-back on" aria-hidden="true" onClick={onClose} />
      <div ref={box} className="acc-pop" role="dialog" aria-labelledby={hId}>
        <div className="ap-h">
          <h2 id={hId}>{t.access.windowTitle(name)}</h2>
          <button type="button" className="iconbtn" aria-label={t.common.close} onClick={onClose} data-autofocus>
            <IconX />
          </button>
        </div>
        {body}
      </div>
    </>
  );
}

/** The "Why sensitive?" popover: the data objects and flows that make the level. */
function SensPop({
  name,
  level,
  reasons,
  onClose,
}: {
  name: string;
  level: Sensitivity;
  reasons: SensitivityReason[];
  onClose: () => void;
}) {
  const { t } = useI18n();
  const { model: m } = useLandscape();
  const ref = useRef<HTMLDivElement>(null);
  useEffect(() => ref.current?.focus({ preventScroll: true }), []);
  return (
    <>
      <div className="pop-back on" aria-hidden="true" onClick={onClose} />
      <div ref={ref} className="sens-pop" role="dialog" aria-labelledby="sens-h" tabIndex={-1}>
        <h3 id="sens-h">{t.access.whyTitle(t.sens[level])}</h3>
        <p>{t.access.sensExplain(name)}</p>
        {reasons.length ? (
          <ul className="reasons">
            {reasons.map((r) => {
              const f = r.kind === 'flow' ? m?.flow.get(r.id) : undefined;
              return (
                <li key={r.kind + r.id}>
                  <Link to={r.kind === 'flow' ? paths.flow(r.id) : paths.lineage(r.id)} onClick={onClose}>
                    {f && !f.restricted ? (
                      <span className={'t-' + f.type} style={{ color: 'var(--tc)', display: 'flex' }}>
                        <TypeIcon type={f.type} size={14} />
                      </span>
                    ) : (
                      <IconDoc size={14} />
                    )}
                    <span>
                      {r.name} <span className="rk">· {t.access.reasonKind[r.kind]}</span>
                    </span>
                    <span className="rl">
                      <Sdot s={r.sensitivity} />
                      {t.sens[r.sensitivity]}
                    </span>
                  </Link>
                </li>
              );
            })}
          </ul>
        ) : (
          <p style={{ margin: 0 }}>{t.access.noReasons}</p>
        )}
      </div>
    </>
  );
}

/** The chips under a record's title: who can see it (opens the window) and, for applications, the sensitivity. */
export function AccessChips({
  kind,
  id,
  name,
  version,
  visibility,
  access,
  owner,
  sensitivity,
  pop,
  setPop,
  pages,
  canPublish,
  onRestrict,
  onMakeEveryone,
  onTransfer,
}: {
  kind: RecordKind;
  id: string;
  name: string;
  version: number;
  visibility: Visibility;
  access?: AccessList | null;
  owner: string | null;
  sensitivity?: { level: Sensitivity; reasons: SensitivityReason[] };
  pop: AccessPop;
  setPop: (p: AccessPop) => void;
  pages?: PageRef[];
  canPublish: boolean;
  onRestrict?: (mode: 'restrict' | 'guest') => void;
  onMakeEveryone?: () => void;
  onTransfer?: () => void;
}) {
  const { t } = useI18n();
  const { me } = useAuth();
  const accWrap = useRef<HTMLSpanElement>(null);
  const sensWrap = useRef<HTMLSpanElement>(null);
  usePopover(pop === 'access', accWrap, () => setPop(null));
  usePopover(pop === 'sens', sensWrap, () => setPop(null));
  const guest = me?.role === 'guest';
  const restricted = visibility === 'restricted';
  const teams = (access?.teamIds.filter((x) => x !== owner).length ?? 0) + (owner ? 1 : 0);
  const people = access?.usernames.length ?? 0;
  const close = () => setPop(null);
  const act = (fn?: () => void) => (fn ? () => (close(), fn()) : undefined);
  return (
    <div className="acc-chips">
      <span className="chip-wrap" ref={accWrap}>
        <button
          type="button"
          className={'vchip' + (restricted ? ' restricted' : guest ? ' shared' : '')}
          aria-expanded={pop === 'access'}
          aria-haspopup="dialog"
          onClick={() => setPop(pop === 'access' ? null : 'access')}
        >
          {restricted ? <IconLock size={15} /> : guest ? <IconUser size={15} /> : <IconEye size={15} />}
          {restricted
            ? t.access.restrictedChip(teams, people)
            : guest
              ? t.access.sharedWithYou
              : t.access.visibleEveryone}
          <IconChevron className="cv" />
        </button>
        <AccessWindow
          kind={kind}
          id={id}
          name={name}
          version={version}
          open={pop === 'access'}
          onClose={close}
          pages={pages}
          canPublish={canPublish}
          onRestrict={onRestrict && ((mode) => (close(), onRestrict(mode)))}
          onMakeEveryone={act(onMakeEveryone)}
          onTransfer={act(onTransfer)}
        />
      </span>
      {sensitivity && !guest && (
        <span className="chip-wrap" ref={sensWrap}>
          <button
            type="button"
            className={'schip ' + sensitivity.level}
            aria-expanded={pop === 'sens'}
            aria-haspopup="dialog"
            onClick={() => setPop(pop === 'sens' ? null : 'sens')}
          >
            <Sdot s={sensitivity.level} />
            {t.sens[sensitivity.level]}
            <IconChevron className="cv" />
          </button>
          {pop === 'sens' && (
            <SensPop name={name} level={sensitivity.level} reasons={sensitivity.reasons} onClose={close} />
          )}
        </span>
      )}
    </div>
  );
}

/** "Highest of its 9 flows and 1 data object", for the details of an application. */
export function useSensitivityWhy(appId: string, sourceOf: string[]) {
  const { t } = useI18n();
  const { model: m } = useLandscape();
  const flows = (m?.flowsIn.get(appId)?.length ?? 0) + (m?.flowsOut.get(appId)?.length ?? 0);
  return t.access.highestOf(flows, sourceOf.length);
}

/** Owner name for sentences: the team, or "administrators". */
export function useOwnerName(owner: string | null) {
  const { t } = useI18n();
  const { model: m } = useLandscape();
  return owner && m ? teamName(m, owner) || owner : t.access.administrators;
}
