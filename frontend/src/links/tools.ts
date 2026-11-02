import type { ApiKey, Webhook } from '../api/types';

/**
 * A third-party tool, as shown on the Links page: an API key and a webhook under one name, each optional. The server
 * keeps keys and webhooks apart; the name brings them together (a rotated key keeps its name).
 */
export interface Tool {
  name: string;
  /** Every key with this name, newest first (revoked and expired ones included). */
  keys: ApiKey[];
  /** The key in use: active and not replaced. */
  key: ApiKey | null;
  /** A rotated key still working for a few hours. */
  previous: ApiKey | null;
  webhook: Webhook | null;
  /** Every webhook with this name (normally one). */
  webhooks: Webhook[];
  createdAt: string;
  createdBy: string;
  /** Last call with one of its keys, or last delivery to its webhook. */
  lastUse: string | null;
  /** No working key and no webhook left: kept in the list for its history. */
  removed: boolean;
}

export const toolPath = (name: string) => `/admin/links/tools/${encodeURIComponent(name)}`;

const latest = (xs: (string | null | undefined)[]) =>
  xs.filter((x): x is string => !!x).sort((a, b) => b.localeCompare(a))[0] ?? null;

export function groupTools(keys: ApiKey[], webhooks: Webhook[]): Tool[] {
  const names = new Map<string, { keys: ApiKey[]; webhooks: Webhook[] }>();
  const of = (name: string) => {
    let g = names.get(name);
    if (!g) names.set(name, (g = { keys: [], webhooks: [] }));
    return g;
  };
  keys.forEach((k) => of(k.name).keys.push(k));
  webhooks.forEach((w) => of(w.name).webhooks.push(w));
  const tools = [...names.entries()].map(([name, g]): Tool => {
    const ks = [...g.keys].sort((a, b) => b.createdAt.localeCompare(a.createdAt));
    const active = ks.filter((k) => k.state === 'active');
    const key = active.find((k) => !k.replacedById) ?? null;
    const previous = active.find((k) => !!k.replacedById) ?? null;
    const first = [
      ...ks.map((k) => ({ at: k.createdAt, by: k.createdBy })),
      ...g.webhooks.map((w) => ({ at: w.createdAt, by: w.createdBy })),
    ].sort((a, b) => a.at.localeCompare(b.at))[0];
    return {
      name,
      keys: ks,
      key,
      previous,
      webhook: g.webhooks[0] ?? null,
      webhooks: g.webhooks,
      createdAt: first.at,
      createdBy: first.by,
      lastUse: latest([...ks.map((k) => k.lastUsedAt), ...g.webhooks.map((w) => w.lastDeliveryAt)]),
      removed: active.length === 0 && g.webhooks.length === 0,
    };
  });
  return tools.sort((a, b) => Number(a.removed) - Number(b.removed) || a.name.localeCompare(b.name));
}

/** Whole days until `iso` (0 on the last day, negative once past). */
export const daysUntil = (iso: string, now = new Date()) =>
  Math.floor((new Date(iso).getTime() - now.getTime()) / 86_400_000);

/** A key that will stop working within 30 days and was not rotated yet: the Links page reminds of it. */
export const expiresSoon = (k: ApiKey, now = new Date(), days = 30) =>
  k.state === 'active' && !k.replacedById && !!k.expiresAt && daysUntil(k.expiresAt, now) < days;

export type HookStatus = 'paused' | 'failing' | 'off' | 'on';

export function hookStatus(w: Pick<Webhook, 'enabled' | 'pausedAt' | 'failingSince'>): HookStatus {
  if (w.pausedAt) return 'paused';
  if (!w.enabled) return 'off';
  if (w.failingSince) return 'failing';
  return 'on';
}

/** The ready-to-paste examples shown with a new key, for its scopes. */
export function keySnippets(
  origin: string,
  scopes: string[],
  words: { announce: string; readCatalog: string; tellNoosphere: string },
): { curl: string; github: string; gitlab: string } {
  const deploy = scopes.includes('deployments:write');
  const event = (id: string, version: string) =>
    `{\\"events\\":[{\\"deploymentId\\":\\"${id}\\",\\"title\\":\\"claims-portal ${version}\\",\\"phase\\":\\"closed\\",\\"outcome\\":\\"success\\",\\"url\\":\\"${'$'}RUN_URL\\",\\"applicationIds\\":[\\"claims-portal\\"],\\"updatedAt\\":\\"${'$'}(date -u +%Y-%m-%dT%H:%M:%SZ)\\"}]}`;
  const curl = deploy
    ? [
        `# ${words.announce}`,
        `curl -X POST ${origin}/api/integrations/deployments \\`,
        '  -H "Authorization: Bearer $NOOSPHERE_KEY" \\',
        '  -H "Content-Type: application/json" \\',
        `  -d '{"events":[{"deploymentId":"claims-portal-5.2-prod","title":"claims-portal 5.2","phase":"closed","outcome":"success","url":"https://ci.example/runs/1","applicationIds":["claims-portal"],"updatedAt":"2026-11-02T10:00:00Z"}]}'`,
      ].join('\n')
    : [
        `# ${words.readCatalog}`,
        'curl -H "Authorization: Bearer $NOOSPHERE_KEY" \\',
        `  "${origin}/api/integrations/catalog?limit=100"`,
      ].join('\n');
  const github = deploy
    ? [
        '# .github/workflows/deploy.yml',
        `- name: ${words.tellNoosphere}`,
        '  if: success()',
        '  env:',
        '    RUN_URL: ${{ github.server_url }}/${{ github.repository }}/actions/runs/${{ github.run_id }}',
        '  run: |',
        `    curl -fsS -X POST "${origin}/api/integrations/deployments" \\`,
        '      -H "Authorization: Bearer ${{ secrets.NOOSPHERE_KEY }}" \\',
        '      -H "Content-Type: application/json" \\',
        `      -d "${event('gh-${{ github.run_id }}', '${{ github.ref_name }}')}"`,
      ].join('\n')
    : [
        '# .github/workflows/catalog.yml',
        `- name: ${words.readCatalog}`,
        '  run: |',
        '    curl -fsS -H "Authorization: Bearer ${{ secrets.NOOSPHERE_KEY }}" \\',
        `      "${origin}/api/integrations/catalog?limit=100" -o catalog.json`,
      ].join('\n');
  const gitlab = deploy
    ? [
        '# .gitlab-ci.yml',
        'noosphere:',
        '  stage: .post',
        '  variables:',
        '    RUN_URL: $CI_PIPELINE_URL',
        '  script:',
        '    - >',
        `      curl -fsS -X POST "${origin}/api/integrations/deployments"`,
        '      -H "Authorization: Bearer $NOOSPHERE_KEY"',
        '      -H "Content-Type: application/json"',
        `      -d "${event('gl-$CI_PIPELINE_ID', '$CI_COMMIT_REF_NAME')}"`,
      ].join('\n')
    : [
        '# .gitlab-ci.yml',
        'noosphere-catalog:',
        '  script:',
        `    - curl -fsS -H "Authorization: Bearer $NOOSPHERE_KEY" "${origin}/api/integrations/catalog?limit=100" -o catalog.json`,
      ].join('\n');
  return { curl, github, gitlab };
}
