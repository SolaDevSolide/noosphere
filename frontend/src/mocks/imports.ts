// Development mock of the import from other formats: whatever the files, the dry run answers a small Backstage-like
// plan on the demo landscape, and applying it only marks it applied (the mock landscape does not change).
import type { ImportEntry, ImportPlan, UserM2 } from '../api/types';

const plans = new Map<string, ImportPlan>();

const json = (status: number, body: unknown) =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });
const problem = (status: number, detail: string) =>
  new Response(JSON.stringify({ title: detail, status, detail }), {
    status,
    headers: { 'Content-Type': 'application/problem+json' },
  });

function demoPlan(user: UserM2, files: File[], format: string | null): ImportPlan {
  const file = files[0]?.name ?? 'catalog-info.yaml';
  const at = (index: number, line: number) => ({ file, line, place: `documents[${index}]` });
  const e = (x: Partial<ImportEntry> & Pick<ImportEntry, 'index' | 'id' | 'name' | 'kind'>): ImportEntry => ({
    action: 'create',
    matchedBy: null,
    changes: [],
    possibleMatches: [],
    source: at(x.index, 2 + x.index * 12),
    hasErrors: false,
    requires: [],
    ...x,
  });
  const entries: ImportEntry[] = [
    e({
      index: 0,
      kind: 'team',
      id: 'team-claims',
      name: 'Team Claims',
      changes: [{ field: 'contact', label: 'Contact', kind: 'text', before: null, after: 'claims@alize.example' }],
    }),
    e({
      index: 1,
      kind: 'application',
      id: 'claims-portal',
      name: 'Claims portal',
      requires: [0],
      changes: [
        { field: 'domainId', label: 'Domain', kind: 'ref', before: null, after: 'members' },
        { field: 'teamId', label: 'Team', kind: 'ref', before: null, after: 'team-claims' },
        { field: 'lifecycle', label: 'Lifecycle', kind: 'text', before: null, after: 'live' },
      ],
    }),
    e({
      index: 2,
      kind: 'flow',
      id: 'claims-portal-crm',
      name: 'Member lookup',
      requires: [1],
      changes: [
        { field: 'fromId', label: 'From', kind: 'ref', before: null, after: 'claims-portal' },
        { field: 'toId', label: 'To', kind: 'ref', before: null, after: 'crm' },
      ],
    }),
    e({
      index: 3,
      kind: 'application',
      action: 'update',
      id: 'crm',
      name: 'CRM',
      matchedBy: 'identifier',
      changes: [{ field: 'lifecycle', label: 'Lifecycle', kind: 'text', before: 'retiring', after: 'live' }],
    }),
    e({ index: 4, kind: 'application', action: 'unchanged', id: 'billing', name: 'Billing', matchedBy: 'name' }),
  ];
  const now = Date.now();
  return {
    id: crypto.randomUUID(),
    status: 'planned',
    username: user.username,
    createdAt: new Date(now).toISOString(),
    expiresAt: new Date(now + 30 * 60_000).toISOString(),
    closedAt: null,
    outcome: null,
    formats: [format ?? 'backstage'],
    files: files.map((f) => ({ name: f.name, format: format ?? 'backstage', size: f.size, sha256: '', entries: 5 })),
    counts: { create: 3, update: 1, unchanged: 1, warnings: 1, errors: 0 },
    entries,
    warnings: [
      {
        message: "User 'jdupont' skipped: accounts are managed in Noosphere, and teams own the records.",
        source: at(5, 64),
        entry: null,
        field: null,
      },
    ],
    errors: [],
    canApply: true,
    skipped: [],
  };
}

export function handleImports(
  user: UserM2,
  method: string,
  url: URL,
  path: string,
  body: () => { skip?: number[] },
  files: File[],
): Response | null {
  if (!path.startsWith('/imports')) return null;
  if (user.role === 'guest') return problem(403, 'Guests cannot import.');
  if (path === '/imports' && method === 'POST') {
    if (!files.length) return problem(400, 'Send the files as multipart/form-data.');
    const plan = demoPlan(user, files, url.searchParams.get('format'));
    plans.set(plan.id, plan);
    return json(201, plan);
  }
  if (path === '/imports' && method === 'GET') return json(200, [...plans.values()]);
  const m = /^\/imports\/([^/]+)(\/apply)?$/.exec(path);
  const plan = m ? plans.get(m[1]) : undefined;
  if (!m || !plan) return problem(404, 'No import plan with this id of yours.');
  if (!m[2]) return json(200, plan);
  if (plan.status !== 'planned') return problem(409, 'This import was applied already.');
  if (Date.parse(plan.expiresAt) < Date.now()) return problem(410, 'This import plan expired. Run the dry run again.');
  const applied: ImportPlan = { ...plan, status: 'applied', canApply: false, skipped: body().skip ?? [] };
  plans.set(plan.id, applied);
  return json(200, applied);
}
