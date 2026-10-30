// Demo documentation pages for the development mock (and the screenshots of the docs). Ids of records match the
// Alizé demo landscape; a few links are broken on purpose so the warnings show.

export interface SeedPage {
  id: string;
  title: string;
  parentId?: string;
  order: number;
  attachedTo?: { kind: 'application' | 'flow' | 'dataObject' | 'domain' | 'team'; id: string };
  ownerTeamId?: string;
  tags?: string[];
  visibility?: 'everyone' | 'restricted';
  /** Hours since the last change, to spread the "recently updated" list. */
  hoursAgo: number;
  body: string;
}

const REPLAY = `The [[flow:crm-to-billing-customers]] file is produced by [[application:crm]] at 02:00 and loaded by [[application:billing]] before the invoice run. When the load fails, invoices for new customers are missing the next morning.

> [!NOTE] When to use this page
> The 06:00 check in the Billing console shows **Customers import: failed** or **0 lines**.

## Check the file

The file lands on [[application:sftp]] as \`customers_YYYYMMDD.csv\`. A normal night holds 12,000 to 18,000 lines.

\`\`\`bash
# size and line count of last night's file
ls -lh /exchange/crm/out/customers_$(date +%Y%m%d).csv
wc -l /exchange/crm/out/customers_$(date +%Y%m%d).csv
\`\`\`

| Symptom | Likely cause | Who acts |
|---|---|---|
| No file at 02:30 | CRM export job did not start | [[team:team-crm]] |
| File with 0 lines | Empty delta after a CRM release | [[team:team-crm]] |
| Load error in Billing | New column in the file | [[team:team-finance]] |

## Replay the load

1. Put the import on hold in the Billing console.
2. Run the loader with the file date, first as a dry run:

\`\`\`bash
billing-cli import customers --date 2026-10-29 --dry-run
billing-cli import customers --date 2026-10-29
\`\`\`

3. Check the [[dataObject:customer]] count in the console, then release the hold.

> [!WARNING] Do not replay after 05:00
> [[flow:billing-dwh]] starts at 05:00 and would export half-loaded invoices. After 05:00, wait for the next night and warn the owners of the [[page:invoice-run-calendar]].

## Old export

Until March, the file came from [[application:crm-legacy|the old CRM export]]. Its scripts are archived; do not use them.

## Escalation

- Nights and weekends: Finance on-call, phone in the [[page:billing-handbook]].
- Office hours: [[team:team-crm]] for the file, [[team:team-finance]] for the load.
`;

export const SEED_PAGES: SeedPage[] = [
  {
    id: 'alize-overview',
    title: 'Alizé landscape overview',
    order: 0,
    ownerTeamId: 'team-platform',
    tags: ['overview'],
    hoursAgo: 400,
    body: `Alizé insures cars and homes. Six domains share about twenty applications: start with [[domain:customer]] and [[domain:finance]], where most flows meet.\n\n## Where to start\n\n- The [[page:glossary]] explains the words people use.\n- Runbooks live under each domain; the [[page:billing-handbook]] is the most used.\n`,
  },
  {
    id: 'finance',
    title: 'Finance',
    order: 10,
    attachedTo: { kind: 'domain', id: 'finance' },
    ownerTeamId: 'team-finance',
    hoursAgo: 500,
    body: 'Billing, payments and accounting: how money moves through Alizé.\n',
  },
  {
    id: 'billing-handbook',
    title: 'Billing handbook',
    parentId: 'finance',
    order: 0,
    attachedTo: { kind: 'application', id: 'billing' },
    tags: ['overview'],
    hoursAgo: 72,
    body: `[[application:billing]] invoices premiums and sends them by email and post. This handbook gathers what the Finance on-call needs.\n\n## On call\n\nFinance on-call: +33 1 23 45 67 89, nights and weekends.\n\n## Runbooks\n\n- [[page:replay-nightly-import]]\n- [[page:print-file-rejections]]\n`,
  },
  {
    id: 'replay-nightly-import',
    title: 'Replay the nightly customer import',
    parentId: 'billing-handbook',
    order: 0,
    attachedTo: { kind: 'flow', id: 'crm-to-billing-customers' },
    ownerTeamId: 'team-finance',
    tags: ['runbook', 'batch', 'on-call'],
    hoursAgo: 2,
    body: REPLAY,
  },
  {
    id: 'invoice-run-calendar',
    title: 'Invoice run calendar',
    parentId: 'billing-handbook',
    order: 10,
    attachedTo: { kind: 'application', id: 'billing' },
    tags: ['reference'],
    hoursAgo: 330,
    body: `The invoice run starts every night at 05:00 in [[application:billing]].\n\n| Day | Run | Notes |\n|---|---|---|\n| Monday to Friday | 05:00 | Normal run |\n| Saturday | 05:00 | No paper invoices |\n| Last day of the month | 03:00 | Earlier, for the ledger export |\n`,
  },
  {
    id: 'print-file-rejections',
    title: 'Print file rejections',
    parentId: 'billing-handbook',
    order: 20,
    attachedTo: { kind: 'application', id: 'billing' },
    tags: ['runbook'],
    hoursAgo: 120,
    body: `When [[application:printer]] rejects the daily print file, the paper invoices of the day are not sent.\n\n## Check\n\n1. Look at the rejection report on [[application:sftp]].\n2. The old [[flow:billing-printer]] link is gone: the file now goes through [[flow:billing-sftp]].\n`,
  },
  {
    id: 'oncall-handover-finance',
    title: 'On-call handover, Finance',
    parentId: 'billing-handbook',
    order: 30,
    ownerTeamId: 'team-finance',
    tags: ['on-call'],
    hoursAgo: 260,
    body: `Every Monday at 10:00, the outgoing on-call hands over to the next one.\n\n- Open incidents and their owners.\n- Night jobs that failed: start with [[page:replay-nightly-import]].\n`,
  },
  {
    id: 'adr-012-ledger-export',
    title: 'ADR-012 Monthly ledger export',
    parentId: 'finance',
    order: 10,
    attachedTo: { kind: 'flow', id: 'billing-acct' },
    tags: ['adr'],
    hoursAgo: 75,
    body: `**Status:** accepted · **Date:** 2026-10-27\n\n## Context\n\n[[application:billing]] sent ledger entries to [[application:acct]] every night, and month-end reconciliation took three days.\n\n## Decision\n\nSend one export on the first day of the month, after the last invoice run.\n\n## Consequences\n\n- Reconciliation takes one morning.\n- Daily figures come from the [[application:dwh]] instead.\n`,
  },
  {
    id: 'month-end-close',
    title: 'Month-end close checklist',
    parentId: 'finance',
    order: 20,
    ownerTeamId: 'team-finance',
    tags: ['runbook'],
    hoursAgo: 600,
    body: `1. Check the [[page:invoice-run-calendar]] for the earlier run.\n2. Confirm the ledger export ([[page:adr-012-ledger-export]]).\n3. Lock the period in [[application:acct]].\n`,
  },
  {
    id: 'customer-area',
    title: 'Customer',
    order: 20,
    attachedTo: { kind: 'domain', id: 'customer' },
    ownerTeamId: 'team-crm',
    hoursAgo: 700,
    body: 'Portal, mobile app, CRM and sign-in: everything the customer touches.\n',
  },
  {
    id: 'crm-data-quality',
    title: 'CRM data quality rules',
    parentId: 'customer-area',
    order: 0,
    attachedTo: { kind: 'application', id: 'crm' },
    ownerTeamId: 'team-crm',
    hoursAgo: 26,
    body: `Rules the [[application:crm]] applies before [[flow:crm-to-billing-customers]] leaves at 02:00.\n\n- Every [[dataObject:customer]] has a postal address.\n- Duplicates are merged before the nightly export; see [[page:replay-nightly-import]] when the file is empty.\n`,
  },
  {
    id: 'sign-in-oidc',
    title: 'Sign-in with OIDC',
    parentId: 'customer-area',
    order: 10,
    attachedTo: { kind: 'application', id: 'idp' },
    hoursAgo: 900,
    body: `[[application:portal]] and [[application:mobile]] sign customers in through [[application:idp]] with OpenID Connect.\n`,
  },
  {
    id: 'customer-portal-overview',
    title: 'Customer portal overview',
    parentId: 'customer-area',
    order: 20,
    attachedTo: { kind: 'application', id: 'portal' },
    tags: ['overview'],
    hoursAgo: 800,
    body: `[[application:portal]] lets customers see their contracts, claims and invoices from [[application:billing]].\n`,
  },
  {
    id: 'data-platform',
    title: 'Data and platform',
    order: 30,
    attachedTo: { kind: 'domain', id: 'platform' },
    ownerTeamId: 'team-platform',
    hoursAgo: 1000,
    body: 'Shared services: the event bus, file exchange and the data warehouse.\n',
  },
  {
    id: 'event-bus-conventions',
    title: 'Event bus conventions',
    parentId: 'data-platform',
    order: 0,
    attachedTo: { kind: 'application', id: 'bus' },
    hoursAgo: 25,
    body: `Topics on [[application:bus]] are named \`domain.entity.event\`, for example \`billing.invoice.issued\`.\n`,
  },
  {
    id: 'partner-onboarding-sftp',
    title: 'Onboarding a partner on File exchange',
    parentId: 'data-platform',
    order: 10,
    attachedTo: { kind: 'application', id: 'sftp' },
    tags: ['runbook'],
    hoursAgo: 400,
    body: `1. Create the partner folder on [[application:sftp]].\n2. Exchange keys as described in [[page:sftp-keys]].\n3. Tell [[team:team-ops]] the partner's IP range.\n`,
  },
  {
    id: 'dwh-loading-windows',
    title: 'Data warehouse loading windows',
    parentId: 'data-platform',
    order: 20,
    attachedTo: { kind: 'application', id: 'dwh' },
    hoursAgo: 450,
    body: `[[application:dwh]] loads [[flow:crm-dwh]] at 04:00 and [[flow:billing-dwh]] at 05:00.\n`,
  },
  {
    id: 'claims-area',
    title: 'Claims',
    order: 40,
    attachedTo: { kind: 'domain', id: 'claims' },
    ownerTeamId: 'team-claims',
    hoursAgo: 1100,
    body: 'How claims are declared, assessed and paid.\n',
  },
  {
    id: 'claim-photo-limits',
    title: 'Claim photo size limits',
    parentId: 'claims-area',
    order: 0,
    attachedTo: { kind: 'flow', id: 'mobile-claims' },
    hoursAgo: 140,
    body: `[[flow:mobile-claims]] accepts up to 10 photos of 8 MB each.\n`,
  },
  {
    id: 'expert-network-sla',
    title: 'Expert network SLA',
    parentId: 'claims-area',
    order: 10,
    attachedTo: { kind: 'application', id: 'expert' },
    hoursAgo: 1200,
    body: 'Experts answer within two working days.\n',
  },
  {
    id: 'getting-started',
    title: 'Getting started',
    order: 50,
    ownerTeamId: 'team-data',
    hoursAgo: 1300,
    body: 'New here? Read these first.\n',
  },
  {
    id: 'glossary',
    title: 'Glossary',
    parentId: 'getting-started',
    order: 0,
    ownerTeamId: 'team-data',
    hoursAgo: 118,
    body: `- **Invoice run**: the nightly job of [[application:billing]] that issues invoices.\n- **Source of truth**: the application that owns a data object, such as [[application:crm]] for [[dataObject:customer]].\n`,
  },
  {
    id: 'document-a-flow',
    title: 'How to document a flow',
    parentId: 'getting-started',
    order: 10,
    ownerTeamId: 'team-data',
    hoursAgo: 1400,
    body: `Give every flow a schedule, the data it carries and an owner team. Link runbooks with \`[[flow:id]]\`.\n`,
  },
];
