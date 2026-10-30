import { useState, type FormEvent } from 'react';
import { Navigate, useSearchParams } from 'react-router-dom';
import { ApiError } from '../api/client';
import { useAuth } from '../auth/auth';
import { useI18n } from '../i18n';
import { Brand, ThemeButton } from '../layout/TopBar';
import { LanguageSwitch } from '../layout/UserMenu';
import { IconEye, IconWarn } from '../ui/icons';

/** Only same-site paths are followed after sign-in. */
export function safeNext(next: string | null) {
  return next && next.startsWith('/') && !next.startsWith('//') && !next.startsWith('/\\') ? next : '/';
}

export function LoginPage() {
  const { t } = useI18n();
  const { status, login } = useAuth();
  const [q] = useSearchParams();
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [show, setShow] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  if (status === 'signed-in') return <Navigate to={safeNext(q.get('next'))} replace />;

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    if (!username.trim() || !password) {
      setError(t.login.invalid);
      return;
    }
    setBusy(true);
    setError(null);
    try {
      await login(username.trim(), password);
    } catch (err) {
      setError(
        err instanceof ApiError && err.status === 0
          ? t.common.networkError
          : err instanceof ApiError && err.status >= 500
            ? t.common.genericError
            : t.login.invalid,
      );
      setBusy(false);
    }
  };

  return (
    <div className="login">
      <div className="login-top">
        <LanguageSwitch small />
        <ThemeButton />
      </div>
      <main className="login-card">
        <Brand to="/login" />
        <h1>{t.login.title}</h1>
        <p>{t.login.intro}</p>
        <form className="form" onSubmit={(e) => void submit(e)} noValidate>
          {error && (
            <div className="banner danger" role="alert" id="login-err">
              <IconWarn />
              <span className="bt">{error}</span>
            </div>
          )}
          <div className="field full">
            <label htmlFor="username">{t.login.username}</label>
            <input
              id="username"
              className="input"
              autoComplete="username"
              autoCapitalize="none"
              spellCheck={false}
              value={username}
              onChange={(e) => setUsername(e.target.value)}
              aria-invalid={error ? true : undefined}
              aria-describedby={error ? 'login-err' : undefined}
              autoFocus
            />
          </div>
          <div className="field full">
            <label htmlFor="password">{t.login.password}</label>
            <div className="pw">
              <input
                id="password"
                className="input"
                type={show ? 'text' : 'password'}
                autoComplete="current-password"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                aria-invalid={error ? true : undefined}
                aria-describedby={error ? 'login-err' : undefined}
              />
              <button
                type="button"
                aria-label={show ? t.login.hidePassword : t.login.showPassword}
                aria-pressed={show}
                title={show ? t.login.hidePassword : t.login.showPassword}
                onClick={() => setShow((s) => !s)}
              >
                <IconEye />
              </button>
            </div>
          </div>
          <button
            type="submit"
            className="btn primary"
            style={{ width: '100%', height: 40, marginTop: 6 }}
            disabled={busy}
          >
            {busy ? t.login.submitting : t.login.submit}
          </button>
        </form>
        {import.meta.env.VITE_MOCK === '1' && <p className="demo-note">{t.login.demo}</p>}
      </main>
    </div>
  );
}
