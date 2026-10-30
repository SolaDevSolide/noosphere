import type { ReactNode } from 'react';
import { Link } from 'react-router-dom';
import { useI18n } from '../i18n';
import { toggleTheme } from '../ui/theme';
import { IconLogo, IconMoon, IconSun } from '../ui/icons';
import { Search } from './Search';
import { UserMenu } from './UserMenu';

export function Brand({ to = '/' }: { to?: string }) {
  const { t } = useI18n();
  return (
    <Link className="brand" to={to} aria-label={t.brand.name}>
      <span className="logo" aria-hidden="true">
        <IconLogo />
      </span>
      <span>
        <b>{t.brand.name}</b>
        <small>{t.brand.sub}</small>
      </span>
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
        <ThemeButton />
        <UserMenu />
      </div>
    </header>
  );
}
