import { useEffect, useRef, useState } from 'react';
import { useGo } from '../ui/useGo';
import { Link } from 'react-router-dom';
import { useAuth } from '../auth/auth';
import { useI18n } from '../i18n';
import { IconLogo, IconReview, IconSettings, IconTeam, IconTrash, IconUpload } from '../ui/icons';

export function LanguageSwitch({ small }: { small?: boolean }) {
  const { lang, t } = useI18n();
  const { changeLanguage } = useAuth();
  return (
    <div className={'seg' + (small ? ' sm' : '')} role="group" aria-label={t.top.language}>
      <button type="button" aria-pressed={lang === 'en'} onClick={() => changeLanguage('en')} lang="en">
        EN
      </button>
      <button type="button" aria-pressed={lang === 'fr'} onClick={() => changeLanguage('fr')} lang="fr">
        FR
      </button>
    </div>
  );
}

export function UserMenu() {
  const { t } = useI18n();
  const { me, isAdmin, logout } = useAuth();
  const nav = useGo();
  const [open, setOpen] = useState(false);
  const wrap = useRef<HTMLDivElement>(null);
  const btn = useRef<HTMLButtonElement>(null);

  useEffect(() => {
    if (!open) return;
    const onDoc = (e: MouseEvent) => {
      if (!wrap.current?.contains(e.target as Node)) setOpen(false);
    };
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        e.stopPropagation();
        setOpen(false);
        btn.current?.focus();
      }
    };
    document.addEventListener('mousedown', onDoc);
    document.addEventListener('keydown', onKey, true);
    return () => {
      document.removeEventListener('mousedown', onDoc);
      document.removeEventListener('keydown', onKey, true);
    };
  }, [open]);

  if (!me) return null;
  const initials = me.displayName
    .split(/\s+/)
    .map((w) => w[0])
    .join('')
    .slice(0, 2)
    .toUpperCase();
  return (
    <div ref={wrap} style={{ position: 'relative' }}>
      <button
        ref={btn}
        type="button"
        className="iconbtn avatar"
        aria-haspopup="true"
        aria-expanded={open}
        aria-label={`${t.top.userMenu}: ${me.displayName}`}
        onClick={() => setOpen((o) => !o)}
      >
        {initials}
      </button>
      {open && (
        <div className="menu" role="menu" aria-label={t.top.userMenu}>
          <div className="menu-h">
            <b>{me.displayName}</b>
            <small>
              {me.username} · {t.top.role[me.role]}
            </small>
          </div>
          <div className="menu-sec">
            <span>{t.top.language}</span>
            <LanguageSwitch small />
          </div>
          <hr />
          <Link role="menuitem" className="mi" to="/" onClick={() => setOpen(false)}>
            <IconLogo size={16} />
            {t.top.explorer}
          </Link>
          {me.role !== 'guest' && (
            <Link role="menuitem" className="mi" to="/review" onClick={() => setOpen(false)}>
              <IconReview />
              {t.top.review}
            </Link>
          )}
          <Link role="menuitem" className="mi" to="/import-export" onClick={() => setOpen(false)}>
            <IconUpload />
            {t.top.importExport}
          </Link>
          {isAdmin && (
            <Link role="menuitem" className="mi" to="/admin/users" onClick={() => setOpen(false)}>
              <IconTeam />
              {t.top.users}
            </Link>
          )}
          {isAdmin && (
            <Link role="menuitem" className="mi" to="/admin/trash" onClick={() => setOpen(false)}>
              <IconTrash />
              {t.top.trash}
            </Link>
          )}
          {isAdmin && (
            <Link role="menuitem" className="mi" to="/admin/settings" onClick={() => setOpen(false)}>
              <IconSettings />
              {t.top.settings}
            </Link>
          )}
          <hr />
          <button
            role="menuitem"
            type="button"
            className="mi"
            onClick={() => {
              setOpen(false);
              void logout().then(() => nav('/login'));
            }}
          >
            {t.top.signOut}
          </button>
        </div>
      )}
    </div>
  );
}
