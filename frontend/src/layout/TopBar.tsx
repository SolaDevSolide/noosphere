import type { ReactNode } from 'react';
import { Link, useLocation } from 'react-router-dom';
import { useSettings } from '../app/settings';
import { useAuth } from '../auth/auth';
import { useI18n } from '../i18n';
import { useInbox } from '../review/inbox';
import { toggleTheme } from '../ui/theme';
import { IconLogo, IconMoon, IconReview, IconSun } from '../ui/icons';
import { Search } from './Search';
import { UserMenu } from './UserMenu';

/** The instance name, with the organisation name (or "Flow map") under it. */
export function Brand({ to = '/' }: { to?: string }) {
  const { t } = useI18n();
  const { settings } = useSettings();
  const name = settings.instanceName.trim() || t.brand.name;
  const sub = settings.organisationName.trim() || t.brand.sub;
  return (
    <Link className="brand" to={to} aria-label={t.brand.home(name)}>
      <span className="logo" aria-hidden="true">
        <IconLogo />
      </span>
      <span className="brand-t">
        <b>{name}</b>
        <small>{sub}</small>
      </span>
    </Link>
  );
}

/** The header entry to the review area, with the number of proposals waiting for me. */
export function ReviewLink() {
  const { t } = useI18n();
  const { me } = useAuth();
  const { inbox } = useInbox();
  const loc = useLocation();
  if (!me || me.role === 'guest') return null;
  const n = inbox?.toValidate ?? 0;
  const here = loc.pathname.startsWith('/review');
  return (
    <Link
      to="/review"
      className={'iconbtn revbtn' + (here ? ' on' : '')}
      aria-current={here ? 'page' : undefined}
      aria-label={n ? `${t.top.review}: ${t.top.reviewWaiting(n)}` : t.top.review}
      title={n ? t.top.reviewWaiting(n) : t.top.review}
    >
      <IconReview />
      <span className="revl">{t.top.review}</span>
      {n > 0 && (
        <span className="badge" aria-hidden="true">
          {n > 99 ? '99+' : n}
        </span>
      )}
    </Link>
  );
}

export function ThemeButton() {
  const { t } = useI18n();
  return (
    <button
      type="button"
      className="iconbtn"
      title={t.top.themeToggle}
      aria-label={t.top.themeToggle}
      onClick={toggleTheme}
    >
      <IconMoon className="theme-light-ic" />
      <IconSun className="theme-dark-ic" />
    </button>
  );
}

export function TopBar({ filter }: { filter?: ReactNode }) {
  return (
    <header className="top">
      <Brand />
      <div className="sgroup">
        <Search />
        {filter}
      </div>
      <div className="top-r">
        <ReviewLink />
        <ThemeButton />
        <UserMenu />
      </div>
    </header>
  );
}
