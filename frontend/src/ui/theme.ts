const KEY = 'noos-theme';

export function isDark(): boolean {
  const t = document.documentElement.dataset.theme;
  return t ? t === 'dark' : window.matchMedia?.('(prefers-color-scheme: dark)').matches === true;
}

/** Flip between light and dark, remember the choice, and tell canvas views to repaint. */
export function toggleTheme() {
  const next = isDark() ? 'light' : 'dark';
  document.documentElement.dataset.theme = next;
  try {
    localStorage.setItem(KEY, next);
  } catch {
    /* private mode */
  }
  window.dispatchEvent(new Event('noos-theme'));
}
