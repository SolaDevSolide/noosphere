# Security policy

## Reporting a vulnerability

Please do not open a public issue. Report it privately through
[GitHub security advisories](https://github.com/SolaDevSolide/noosphere/security/advisories/new), with the steps to
reproduce it and the version affected. You will get an answer within a week.

## Supported versions

Only the latest release receives security fixes.

## What Noosphere does

- Passwords are hashed with ASP.NET Core Identity's password hasher (PBKDF2).
- Sessions are HttpOnly, SameSite=Lax cookies, marked Secure on HTTPS (also behind a trusted reverse proxy).
- Every state-changing API request must carry an `X-Requested-With` header (cross-site request forgery).
- Sign-in attempts are rate limited per client address.
- Strict browser security headers (Content-Security-Policy, X-Frame-Options, nosniff).
- Restricted applications are never sent in full to readers who are not administrators.

Run Noosphere behind an HTTPS reverse proxy and never enable demo mode on a shared server.
