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
- AI agents (MCP) use OAuth 2.1 with PKCE (S256 only) and opaque tokens stored as SHA-256 hashes; access tokens last
  one hour and refresh tokens are rotated, a reused one revoking the connection. An agent reads at most what its
  person reads, capped by the administrators' ceiling (by default: no restricted records, no protected notes), and
  never publishes. Client metadata documents are fetched over https only, with size and time limits, without
  redirects and never from private or local addresses. Administrators can revoke every agent at once.
- The consent page API needs the session cookie and the `X-Requested-With` header; OAuth client registration is
  rate limited and can be turned off.

Run Noosphere behind an HTTPS reverse proxy and never enable demo mode on a shared server.
