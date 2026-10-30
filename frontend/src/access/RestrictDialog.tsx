import { useEffect, useMemo, useRef, useState } from 'react';
import { api, ApiError } from '../api/client';
import { isRestrictedPage, type AccessList, type PageRef, type Person, type Visibility } from '../api/types';
import { usePages } from '../docpages/store';
import { fold } from '../domain/model';
import { useLandscape } from '../explorer/landscape';
import { useI18n } from '../i18n';
import { IconEye, IconLock, IconSearch, IconShield, IconUser, IconUsers, IconWarn, IconX } from '../ui/icons';
import { useModal } from '../ui/modal';

export interface RestrictTarget {
  kind: 'application' | 'page';
  name: string;
  visibility: Visibility;
  access: AccessList;
  /** Owning team id (always sees the record). */
  owner: string | null;
}

interface Option {
  key: string;
  kind: 'team' | 'user';
  id: string;
  label: string;
  sub: string;
  disabled: boolean;
}

const initials = (s: string) =>
  s
    .split(/\s+/)
    .filter(Boolean)
    .map((w) => w[0])
    .join('')
    .slice(0, 2)
    .toUpperCase();

/** Joins names the way the language does: "A, B and C". */
export function joinNames(lang: string, names: string[]): string {
  try {
    return new Intl.ListFormat(lang, { style: 'long', type: 'conjunction' }).format(names);
  } catch {
    return names.join(', ');
  }
}

/** The access list as saved for a visibility: a public record keeps only the guests it is shared with. */
export function listFor(vis: Visibility, list: AccessList, people: Person[] | null): AccessList {
  if (vis === 'restricted') return { teamIds: [...list.teamIds], usernames: [...list.usernames] };
  const guests = new Set((people ?? []).filter((p) => p.role === 'guest').map((p) => p.username));
  return { teamIds: [], usernames: people ? list.usernames.filter((u) => guests.has(u)) : [...list.usernames] };
}

/**
 * "Who can see …?": everyone or restricted, the teams and people of the access list (with a search), a preview of the
 * result and a warning about attached pages that stay public. Also used to share a record with a guest.
 */
export function RestrictDialog({
  target,
  mode,
  pages = [],
  flowCount = 0,
  onClose,
  onSave,
}: {
  target: RestrictTarget;
  mode: 'restrict' | 'guest';
  pages?: PageRef[];
  flowCount?: number;
  onClose: () => void;
  onSave: (visibility: Visibility, access: AccessList) => Promise<void>;
}) {
  const { t, lang } = useI18n();
  const { model: m } = useLandscape();
  const { byId } = usePages();
  const box = useRef<HTMLDivElement>(null);
  useModal(box, onClose);
  const [vis, setVis] = useState<Visibility>(mode === 'guest' ? target.visibility : 'restricted');
  const [list, setList] = useState<AccessList>(() => ({
    teamIds: target.access.teamIds.filter((x) => x !== target.owner),
    usernames: [...target.access.usernames],
  }));
  const [people, setPeople] = useState<Person[] | null>(null);
  const [peopleError, setPeopleError] = useState(false);
  const [q, setQ] = useState('');
  const [active, setActive] = useState(0);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let alive = true;
    api
      .people()
      .then((p) => alive && setPeople(p))
      .catch(() => alive && setPeopleError(true));
    return () => {
      alive = false;
    };
  }, []);

  const teamName = (id: string) => m?.teams.get(id)?.name ?? id;
  const person = (u: string) => people?.find((p) => p.username === u);
  const memberCount = (team: string) => people?.filter((p) => p.teamIds.includes(team)).length;
  const restricted = vis === 'restricted';
  const seeing = new Set([...(target.owner ? [target.owner] : []), ...list.teamIds]);
  const subOf = (p: Person) =>
    p.role === 'guest'
      ? t.access.guestRole
      : p.teamIds.length
        ? p.teamIds.map(teamName).join(', ')
        : p.role === 'admin'
          ? t.top.role.admin
          : t.access.noTeam;

  const options = useMemo<Option[]>(() => {
    const f = fold(q.trim());
    const teams: Option[] = restricted
      ? [...(m?.teams.values() ?? [])]
          .filter((x) => x.id !== target.owner && !list.teamIds.includes(x.id))
          .filter((x) => !f || fold(x.name).includes(f) || x.id.includes(f))
          .map((x) => {
            const n = people?.filter((p) => p.teamIds.includes(x.id)).length;
            return {
              key: 'team:' + x.id,
              kind: 'team',
              id: x.id,
              label: x.name,
              sub: n == null ? '' : t.access.peopleCount(n),
              disabled: false,
            };
          })
      : [];
    const users: Option[] = (people ?? [])
      .filter((p) => !list.usernames.includes(p.username) && (restricted || p.role === 'guest'))
      .filter((p) => !f || fold(p.displayName).includes(f) || p.username.includes(f))
      .map((p) => {
        const sees = restricted && (p.role === 'admin' || p.teamIds.some((x) => seeing.has(x)));
        return {
          key: 'user:' + p.username,
          kind: 'user',
          id: p.username,
          label: p.displayName,
          sub: subOf(p) + (sees ? ' · ' + t.access.alreadySees : ''),
          disabled: sees,
        };
      });
    return [...teams.slice(0, 8), ...users.slice(0, 10)];
    // seeing and subOf derive from list, people and the language
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [q, restricted, m, people, list, target.owner, t]);
  const pickable = options.filter((o) => !o.disabled);

  const add = (o: Option) => {
    if (o.disabled) return;
    setList((l) =>
      o.kind === 'team' ? { ...l, teamIds: [...l.teamIds, o.id] } : { ...l, usernames: [...l.usernames, o.id] },
    );
    setQ('');
    setActive(0);
  };
  const removeTeam = (id: string) => setList((l) => ({ ...l, teamIds: l.teamIds.filter((x) => x !== id) }));
  const removeUser = (u: string) => setList((l) => ({ ...l, usernames: l.usernames.filter((x) => x !== u) }));

  const shownUsers = restricted ? list.usernames : list.usernames.filter((u) => !people || person(u)?.role === 'guest');
  const readerNames = [
    ...(target.owner ? [teamName(target.owner)] : []),
    ...list.teamIds.map(teamName),
    ...list.usernames.map((u) => person(u)?.displayName ?? u),
  ];
  const publicPages =
    target.kind === 'application'
      ? pages.filter((p) => {
          const s = byId.get(p.id);
          return s && !isRestrictedPage(s) && 'visibility' in s && s.visibility === 'everyone';
        })
      : [];

  const save = async () => {
    setBusy(true);
    setError(null);
    try {
      const next = listFor(vis, list, people);
      if (restricted && target.owner) next.teamIds = next.teamIds.filter((x) => x !== target.owner);
      await onSave(vis, next);
    } catch (e) {
      setError(
        e instanceof ApiError
          ? e.status === 0
            ? t.common.networkError
            : Object.values(e.fieldErrors).flat().join(' ') || e.detail || e.message
          : t.common.genericError,
      );
      setBusy(false);
    }
  };

  const listId = 'pk-res';
  return (
    <div className="dlg-back" role="presentation" onMouseDown={(e) => e.target === e.currentTarget && onClose()}>
      <div ref={box} className="dlg wide" role="dialog" aria-modal="true" aria-labelledby="dr-h" tabIndex={-1}>
        <h2 id="dr-h">{t.access.dlgTitle(target.name)}</h2>
        <p className="dsub">{t.access.dlgSub}</p>
        <div className="rcards two" role="radiogroup" aria-label={t.access.dlgVisibility}>
          <label className="rcard">
            <input
              type="radio"
              name="vis"
              value="everyone"
              checked={!restricted}
              onChange={() => setVis('everyone')}
              data-autofocus={!restricted || undefined}
            />
            <b>
              <IconEye size={15} />
              {t.access.optEveryone}
            </b>
            <small>{t.access.optEveryoneText}</small>
          </label>
          <label className="rcard">
            <input
              type="radio"
              name="vis"
              value="restricted"
              checked={restricted}
              onChange={() => setVis('restricted')}
              data-autofocus={restricted || undefined}
            />
            <b>
              <IconLock size={15} />
              {t.access.optRestricted}
            </b>
            <small>{t.access.optRestrictedText}</small>
          </label>
        </div>

        <span className="sec-l" id="dr-who">
          {restricted ? t.access.canSeeIt : t.access.sharedGuestsH}
        </span>
        <div className="selchips" aria-labelledby="dr-who">
          {restricted && target.owner && (
            <span className="selchip locked">
              <IconUsers size={13} />
              {teamName(target.owner)}{' '}
              <small>
                {t.access.ownerTag}
                {memberCount(target.owner) != null && ` · ${memberCount(target.owner)}`}
              </small>
            </span>
          )}
          {restricted && (
            <span className="selchip locked">
              <IconShield size={13} />
              {t.access.admins}
            </span>
          )}
          {restricted &&
            list.teamIds.map((id) => (
              <span key={id} className="selchip">
                <IconUsers size={13} />
                {teamName(id)} {memberCount(id) != null && <small>{memberCount(id)}</small>}
                <button type="button" aria-label={t.access.remove(teamName(id))} onClick={() => removeTeam(id)}>
                  <IconX size={12} />
                </button>
              </span>
            ))}
          {shownUsers.map((u) => {
            const p = person(u);
            return (
              <span key={u} className="selchip">
                <IconUser size={13} />
                {p?.displayName ?? u} {p && <small>{subOf(p)}</small>}
                <button type="button" aria-label={t.access.remove(p?.displayName ?? u)} onClick={() => removeUser(u)}>
                  <IconX size={12} />
                </button>
              </span>
            );
          })}
        </div>
        <div className="picker">
          <IconSearch size={15} />
          <input
            className="input"
            value={q}
            onChange={(e) => {
              setQ(e.target.value);
              setActive(0);
            }}
            onKeyDown={(e) => {
              if (e.key === 'ArrowDown') {
                e.preventDefault();
                setActive((a) => Math.min(a + 1, pickable.length - 1));
              } else if (e.key === 'ArrowUp') {
                e.preventDefault();
                setActive((a) => Math.max(a - 1, 0));
              } else if (e.key === 'Enter') {
                e.preventDefault();
                if (pickable[active]) add(pickable[active]);
              }
            }}
            placeholder={restricted ? t.access.pickerPh : t.access.pickerGuestPh}
            aria-label={t.access.pickerLabel}
            role="combobox"
            aria-expanded={q.trim().length > 0}
            aria-controls={listId}
            aria-autocomplete="list"
            aria-activedescendant={pickable[active] ? 'pk-' + pickable[active].key : undefined}
            autoComplete="off"
          />
          {q.trim().length > 0 && (
            <div className="pk-res" role="listbox" id={listId}>
              {options.some((o) => o.kind === 'team') && <div className="rg">{t.access.pickerTeams}</div>}
              {options
                .filter((o) => o.kind === 'team')
                .map((o) => (
                  <OptionRow key={o.key} o={o} act={pickable[active]?.key === o.key} onPick={() => add(o)} />
                ))}
              {options.some((o) => o.kind === 'user') && <div className="rg">{t.access.pickerPeople}</div>}
              {options
                .filter((o) => o.kind === 'user')
                .map((o) => (
                  <OptionRow key={o.key} o={o} act={pickable[active]?.key === o.key} onPick={() => add(o)} />
                ))}
              {!options.length && <div className="pk-empty">{t.access.noMatch}</div>}
            </div>
          )}
        </div>
        {peopleError && (
          <p className="muted" style={{ fontSize: 12.5, margin: '6px 0 0' }}>
            {t.access.peopleError}
          </p>
        )}

        <div className="preview-s" aria-live="polite">
          <span className="pl">{t.access.preview}</span>
          {restricted ? (
            <>
              <p>
                <b>{joinNames(lang, t.access.previewAndAdmins(readerNames))}</b>
                {t.access.previewWill(target.name)}
              </p>
              <p>{target.kind === 'application' ? t.access.previewOthersApp(flowCount) : t.access.previewOthersPage}</p>
            </>
          ) : (
            <>
              <p>{t.access.previewEveryone(target.name)}</p>
              {shownUsers.length > 0 && (
                <p>
                  {t.access.previewGuests(
                    joinNames(
                      lang,
                      shownUsers.map((u) => person(u)?.displayName ?? u),
                    ),
                  )}
                </p>
              )}
            </>
          )}
        </div>
        {restricted && publicPages.length > 0 && (
          <div className="banner warn" role="note">
            <IconWarn size={16} />
            <span className="bt">
              <b>{t.access.publicPagesWarn(publicPages.length, target.name)}</b>
              {t.access.publicPagesText}
              <span className="pglist-in">{publicPages.map((p) => p.title).join(' · ')}</span>
            </span>
          </div>
        )}
        {error && (
          <div className="banner danger" role="alert">
            <IconWarn size={16} />
            <span className="bt">{error}</span>
          </div>
        )}
        <div className="form-actions">
          <button type="button" className="btn" onClick={onClose} disabled={busy}>
            {t.common.cancel}
          </button>
          <button type="button" className="btn primary" onClick={() => void save()} disabled={busy}>
            {busy
              ? t.common.saving
              : restricted && target.visibility !== 'restricted'
                ? t.access.doRestrict(target.name)
                : t.access.doSave}
          </button>
        </div>
      </div>
    </div>
  );
}

function OptionRow({ o, act, onPick }: { o: Option; act: boolean; onPick: () => void }) {
  const { t } = useI18n();
  return (
    <div
      id={'pk-' + o.key}
      role="option"
      tabIndex={-1}
      aria-selected={act}
      aria-disabled={o.disabled || undefined}
      className={'pk-opt' + (act ? ' act' : '') + (o.disabled ? ' dis' : '')}
      onMouseDown={(e) => {
        e.preventDefault();
        onPick();
      }}
    >
      <span className={'av' + (o.kind === 'team' ? ' team' : '')}>{initials(o.label)}</span>
      <span>
        {o.label}
        {o.sub && <small>{o.sub}</small>}
      </span>
      <span className="add">{o.disabled ? '' : t.access.add}</span>
    </div>
  );
}
