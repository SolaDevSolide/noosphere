import { useEffect, useRef, useState, type FormEvent } from 'react';
import { api, ApiError } from '../api/client';
import { ROLES, type Language, type Role, type User, type UserCreateInput, type UserUpdateInput } from '../api/types';
import { useAuth } from '../auth/auth';
import { useI18n } from '../i18n';
import { describe, Field, PageShell, serverErrors } from '../records/common';
import { IconPlus, IconWarn } from '../ui/icons';
import { useToast } from '../ui/Toast';

interface Draft {
  username: string;
  displayName: string;
  role: Role;
  language: Language;
  password: string;
}

function UserDialog({
  user,
  onClose,
  onSaved,
}: {
  user: User | null;
  onClose: () => void;
  onSaved: (u: User, created: boolean) => void;
}) {
  const { t } = useI18n();
  const [d, setD] = useState<Draft>(() => ({
    username: user?.username ?? '',
    displayName: user?.displayName ?? '',
    role: user?.role ?? 'member',
    language: user?.language ?? 'en',
    password: '',
  }));
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [failure, setFailure] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const ref = useRef<HTMLDivElement>(null);
  const back = useRef<HTMLDivElement>(null);
  useEffect(() => {
    const el = back.current;
    const down = (e: MouseEvent) => e.target === el && onClose();
    el?.addEventListener('mousedown', down);
    return () => el?.removeEventListener('mousedown', down);
  }, [onClose]);
  const opener = useRef<Element | null>(document.activeElement);
  const set = <K extends keyof Draft>(k: K, v: Draft[K]) => setD((x) => ({ ...x, [k]: v }));

  useEffect(() => {
    ref.current?.querySelector<HTMLElement>('input')?.focus();
    const back = opener.current;
    const k = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        e.stopPropagation();
        onClose();
      }
      if (e.key === 'Tab' && ref.current) {
        // keep the focus inside the dialog
        const f = [...ref.current.querySelectorAll<HTMLElement>('input, select, button')].filter(
          (x) => !x.hasAttribute('disabled'),
        );
        if (!f.length) return;
        const first = f[0];
        const last = f[f.length - 1];
        if (e.shiftKey && document.activeElement === first) {
          e.preventDefault();
          last.focus();
        } else if (!e.shiftKey && document.activeElement === last) {
          e.preventDefault();
          first.focus();
        }
      }
    };
    document.addEventListener('keydown', k, true);
    return () => {
      document.removeEventListener('keydown', k, true);
      (back as HTMLElement | null)?.focus?.();
    };
  }, [onClose]);

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    const er: Record<string, string> = {};
    if (!user) {
      if (!d.username.trim()) er.username = t.record.errors.required;
      else if (!/^[a-z0-9][a-z0-9-]{0,39}$/.test(d.username.trim())) er.username = t.users.usernameHint;
      if (!d.password) er.password = t.record.errors.required;
    }
    if (!d.displayName.trim()) er.displayName = t.record.errors.required;
    setErrors(er);
    setFailure(null);
    if (Object.keys(er).length) return;
    setBusy(true);
    const common = {
      displayName: d.displayName.trim(),
      role: d.role,
      language: d.language,
      password: d.password || null,
    };
    try {
      const saved = user
        ? await api.updateUser(user.username, { ...common, version: user.version } satisfies UserUpdateInput)
        : await api.createUser({ ...common, username: d.username.trim() } satisfies UserCreateInput);
      onSaved(saved as User, !user);
    } catch (err) {
      if (err instanceof ApiError && err.status === 400 && Object.keys(err.fieldErrors).length)
        setErrors(serverErrors(err));
      else if (err instanceof ApiError && err.isConflict) setFailure(err.detail || t.record.conflict);
      else setFailure(err instanceof ApiError ? err.detail || err.message : t.common.genericError);
      setBusy(false);
    }
  };

  return (
    <div className="dlg-back" ref={back}>
      <div ref={ref} className="dlg" role="dialog" aria-modal="true" aria-labelledby="dlg-h">
        <h2 id="dlg-h">{user ? t.users.editTitle : t.users.newTitle}</h2>
        <form className="form" onSubmit={(e) => void submit(e)} noValidate>
          {failure && (
            <div className="banner danger" role="alert">
              <IconWarn />
              <span className="bt">{failure}</span>
            </div>
          )}
          <div className="fgrid">
            <Field
              id="u-username"
              label={t.users.username}
              required={!user}
              hint={user ? undefined : t.users.usernameHint}
              error={errors.username}
            >
              <input
                className="input"
                {...describe('u-username', user ? undefined : t.users.usernameHint, errors.username)}
                value={d.username}
                disabled={!!user}
                onChange={(e) => set('username', e.target.value)}
                autoCapitalize="none"
                autoComplete="off"
                spellCheck={false}
              />
            </Field>
            <Field id="u-display" label={t.users.displayName} required error={errors.displayName}>
              <input
                className="input"
                {...describe('u-display', undefined, errors.displayName)}
                value={d.displayName}
                onChange={(e) => set('displayName', e.target.value)}
                autoComplete="off"
              />
            </Field>
            <Field id="u-role" label={t.users.role}>
              <select
                className="select"
                id="u-role"
                value={d.role}
                onChange={(e) => set('role', e.target.value as Role)}
              >
                {ROLES.map((r) => (
                  <option key={r} value={r}>
                    {t.top.role[r]}
                  </option>
                ))}
              </select>
            </Field>
            <Field id="u-lang" label={t.users.language}>
              <select
                className="select"
                id="u-lang"
                value={d.language}
                onChange={(e) => set('language', e.target.value as Language)}
              >
                <option value="en">English</option>
                <option value="fr">Français</option>
              </select>
            </Field>
            <Field
              id="u-pass"
              label={user ? t.users.passwordReset : t.users.passwordNew}
              required={!user}
              error={errors.password}
              full
            >
              <input
                className="input"
                type="password"
                {...describe('u-pass', undefined, errors.password)}
                value={d.password}
                onChange={(e) => set('password', e.target.value)}
                autoComplete="new-password"
              />
            </Field>
          </div>
          <div className="form-actions">
            <button type="button" className="btn" onClick={onClose}>
              {t.common.cancel}
            </button>
            <button type="submit" className="btn primary" disabled={busy}>
              {busy ? t.common.saving : t.common.save}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}

export function UsersPage() {
  const { t, lang } = useI18n();
  const { me, isAdmin } = useAuth();
  const toast = useToast();
  const [users, setUsers] = useState<User[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [dialog, setDialog] = useState<{ user: User | null } | null>(null);

  useEffect(() => {
    if (!isAdmin) return;
    api
      .users()
      .then(setUsers)
      .catch((e: unknown) =>
        setError(e instanceof ApiError && e.status === 0 ? t.common.networkError : t.common.genericError),
      );
    // load once per visit
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [isAdmin]);

  if (!isAdmin)
    return (
      <PageShell narrow>
        <div className="page-h">
          <div className="ph-t">
            <h1>{t.users.title}</h1>
          </div>
        </div>
        <div className="banner" role="note">
          <IconWarn />
          <span className="bt">{t.users.forbidden}</span>
        </div>
      </PageShell>
    );

  const remove = async (u: User) => {
    if (!window.confirm(t.users.deleteConfirm(u.username))) return;
    try {
      await api.deleteUser(u.username);
      setUsers((us) => us?.filter((x) => x.username !== u.username) ?? null);
      toast(t.users.deleted(u.username));
    } catch (e) {
      toast(e instanceof ApiError ? e.detail || e.message : t.common.genericError);
    }
  };
  const sorted = users ? [...users].sort((a, b) => a.username.localeCompare(b.username, lang)) : null;
  const actions = (u: User) => (
    <>
      <button
        type="button"
        className="btn"
        onClick={() => setDialog({ user: u })}
        aria-label={t.users.editUser(u.username)}
      >
        {t.common.edit}
      </button>
      <button
        type="button"
        className="btn danger"
        disabled={u.username === me?.username}
        onClick={() => void remove(u)}
        aria-label={t.users.deleteUser(u.username)}
      >
        {t.common.delete}
      </button>
    </>
  );
  const role = (r: Role) => <span className={'rolebadge ' + r}>{t.top.role[r]}</span>;

  return (
    <PageShell>
      <div className="page-h">
        <div className="ph-t">
          <h1>{t.users.title}</h1>
          <p>{t.users.intro}</p>
        </div>
        <div className="page-actions">
          <button type="button" className="btn primary" onClick={() => setDialog({ user: null })}>
            <IconPlus />
            {t.users.add}
          </button>
        </div>
      </div>
      {error && (
        <div className="banner danger" role="alert">
          <span className="bt">{error}</span>
        </div>
      )}
      {!sorted && !error && (
        <div aria-busy="true">
          {Array.from({ length: 4 }, (_, i) => (
            <span key={i} className="sk block" style={{ height: 52, marginBottom: 8 }} />
          ))}
        </div>
      )}
      {sorted && (
        <>
          <div className="table-wrap users">
            <table className="table">
              <caption className="sr">{t.users.count(sorted.length)}</caption>
              <thead>
                <tr>
                  <th scope="col">{t.users.username}</th>
                  <th scope="col">{t.users.displayName}</th>
                  <th scope="col">{t.users.role}</th>
                  <th scope="col">{t.users.language}</th>
                  <th scope="col">
                    <span className="sr">{t.users.actions}</span>
                  </th>
                </tr>
              </thead>
              <tbody>
                {sorted.map((u) => (
                  <tr key={u.username}>
                    <td>
                      <code>{u.username}</code>
                      {u.username === me?.username && <span className="muted"> · {t.users.you}</span>}
                    </td>
                    <td>{u.displayName}</td>
                    <td>{role(u.role)}</td>
                    <td>{u.language.toUpperCase()}</td>
                    <td className="acts">{actions(u)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <ul className="ucards" aria-label={t.users.count(sorted.length)}>
            {sorted.map((u) => (
              <li key={u.username} className="box">
                <div style={{ display: 'flex', justifyContent: 'space-between', gap: 8, alignItems: 'center' }}>
                  <b>{u.displayName}</b>
                  {role(u.role)}
                </div>
                <div className="muted" style={{ fontSize: 13, marginTop: 2 }}>
                  <code>{u.username}</code> · {u.language.toUpperCase()}
                  {u.username === me?.username && <> · {t.users.you}</>}
                </div>
                <div className="page-actions" style={{ marginTop: 10 }}>
                  {actions(u)}
                </div>
              </li>
            ))}
          </ul>
        </>
      )}
      {dialog && (
        <UserDialog
          user={dialog.user}
          onClose={() => setDialog(null)}
          onSaved={(u, created) => {
            setUsers((us) =>
              created ? [...(us ?? []), u] : (us ?? []).map((x) => (x.username === u.username ? u : x)),
            );
            setDialog(null);
            toast(created ? t.users.created(u.username) : t.users.updated(u.username));
          }}
        />
      )}
    </PageShell>
  );
}
