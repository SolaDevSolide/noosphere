// Demo documentation pages for the development mock: the pages of the server demo (backend/.../Demo), then a few more
// so the tree is deeper. A few links are broken on purpose so the warnings show. The replay runbook gets a longer body
// that shows every Markdown feature (callouts, tables, code).

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
  // The server demo pages.
  {
    id: 'operations',
    title: 'Operations handbook',
    order: 1,
    ownerTeamId: 'team-platform',
    tags: ['runbook'],
    hoursAgo: 300,
    body: 'Runbooks for the recurring operations of the Alizé landscape. Each runbook says when to use it, the steps, and how to check the result.\n\n## Runbooks\n\n- [[page:replay-nightly-import]]: when Billing did not receive the customers from the CRM.\n- [[page:month-end-closing]]: the checks before Accounting closes the month.\n\nKeep a runbook next to the record it is about: attach it to the flow or the application, so that it shows on that record.',
  },
  {
    id: 'replay-nightly-import',
    title: 'Replay the nightly customer import',
    parentId: 'operations',
    order: 1,
    attachedTo: { kind: 'flow', id: 'crm-to-billing-customers' },
    tags: ['runbook', 'billing', 'crm'],
    hoursAgo: 2,
    body: REPLAY,
  },
  {
    id: 'month-end-closing',
    title: 'Month-end closing checklist',
    parentId: 'operations',
    order: 2,
    attachedTo: { kind: 'application', id: 'acct' },
    tags: ['runbook', 'finance'],
    hoursAgo: 170,
    body: 'Checks to run with Team Finance before [[application:acct]] closes the month.\n\n1. [[flow:billing-acct]] delivered the ledger entries of the month (1st of the month, after the last invoice run).\n2. [[flow:payments-acct]] delivered the cash entries of the last day.\n3. The totals of invoices and ledger entries match. A gap usually means a credit note issued after the export.\n4. Team Payments confirmed the bank reconciliation.\n\nBackground on the domain: [[page:finance-overview]].',
  },
  {
    id: 'architecture',
    title: 'Architecture decisions',
    order: 2,
    ownerTeamId: 'team-platform',
    tags: ['adr'],
    hoursAgo: 900,
    body: 'Architecture decision records (ADR) of the landscape: one page per decision, with its context, the options and the consequences. A decision is never deleted; a later ADR replaces it.\n\n- [[page:adr-nightly-file]]',
  },
  {
    id: 'adr-nightly-file',
    title: 'ADR 0007: Why a nightly file and not an API',
    parentId: 'architecture',
    order: 1,
    attachedTo: { kind: 'application', id: 'billing' },
    tags: ['adr', 'integration'],
    hoursAgo: 75,
    body: "**Status:** accepted, to be revisited with Billing 4.3.\n\n## Context\n\n[[application:billing]] needs every new or changed customer of [[application:crm]] before its invoice run at 05:00. The CRM is a SaaS: its API is rate-limited and its webhooks are not guaranteed.\n\n## Options\n\n1. **Nightly file** through [[application:sftp|the file exchange]]: one extract of the day's changes.\n2. **Events** on [[application:bus|the message bus]]: one message per change.\n3. **Calls** from Billing to the CRM API during the invoice run.\n\n## Decision\n\nOption 1: [[flow:crm-to-billing-customers]]. Billing only needs the customers once a day, the file can be replayed (see [[page:replay-nightly-import]]) and the CRM extract is a standard feature.\n\n## Consequences\n\n- A customer changed during the day is invoiced with the old details until the next night.\n- Billing 4.3 offers a customer API (v4). When it is live, option 2 becomes cheaper: this ADR will be replaced.\n\nTerms used here: [[page:glossary]].",
  },
  {
    id: 'finance-overview',
    title: 'Finance domain overview',
    order: 3,
    attachedTo: { kind: 'domain', id: 'finance' },
    ownerTeamId: 'team-finance',
    tags: ['domain', 'finance'],
    hoursAgo: 118,
    body: 'The Finance domain invoices premiums, collects payments and keeps the books.\n\n| Application | Team | Role |\n| --- | --- | --- |\n| [[application:billing]] | [[team:team-finance]] | Invoices premiums, sends them by email and post |\n| [[application:payments]] | [[team:team-payments]] | Direct debits, card payments, bank reconciliation |\n| [[application:acct]] | [[team:team-finance]] | General ledger |\n\n## Main flows\n\n- Customers come from the CRM every night: [[flow:crm-to-billing-customers]].\n- Invoices go to the ledger once a month: [[flow:billing-acct]].\n- Payments go to the ledger every evening: [[flow:payments-acct]].\n\n## Recurring work\n\n- [[page:month-end-closing]]\n- Words and data used in the domain: [[page:glossary]]',
  },
  {
    id: 'glossary',
    title: 'Glossary',
    order: 4,
    ownerTeamId: 'team-data',
    tags: ['reference'],
    hoursAgo: 650,
    body: 'Business words used across the landscape, with the data object that carries each of them.\n\n**Customer**: a policyholder, with identity, contact details and preferences. Source: the CRM. See [[dataObject:customer]].\n\n**Policy**: an insurance contract and its cover. See [[dataObject:policy]].\n\n**Claim**: a declared loss, its assessment and settlement. Sensitive data. See [[dataObject:claim]].\n\n**Invoice**: a premium invoice issued by Billing. See [[dataObject:invoice]].\n\n**Payment**: money received or paid out. See [[dataObject:payment]].\n\n**Delta file**: an extract holding only the records created or changed since the previous one.',
  },
  // More pages, for a deeper tree.
  {
    id: 'billing-handbook',
    title: 'Billing handbook',
    parentId: 'finance-overview',
    order: 0,
    attachedTo: { kind: 'application', id: 'billing' },
    tags: ['overview'],
    hoursAgo: 72,
    body: `[[application:billing]] invoices premiums and sends them by email and post. This handbook gathers what the Finance on-call needs.\n\n## On call\n\nFinance on-call: +33 1 23 45 67 89, nights and weekends.\n\n## Runbooks\n\n- [[page:replay-nightly-import]]\n- [[page:print-file-rejections]]\n`,
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
    parentId: 'architecture',
    order: 10,
    attachedTo: { kind: 'flow', id: 'billing-acct' },
    tags: ['adr'],
    hoursAgo: 75,
    body: `**Status:** accepted · **Date:** 2026-10-27\n\n## Context\n\n[[application:billing]] sent ledger entries to [[application:acct]] every night, and month-end reconciliation took three days.\n\n## Decision\n\nSend one export on the first day of the month, after the last invoice run.\n\n## Consequences\n\n- Reconciliation takes one morning.\n- Daily figures come from the [[application:dwh]] instead.\n`,
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
    id: 'document-a-flow',
    title: 'How to document a flow',
    parentId: 'getting-started',
    order: 10,
    ownerTeamId: 'team-data',
    hoursAgo: 1400,
    body: `Give every flow a schedule, the data it carries and an owner team. Link runbooks with \`[[flow:id]]\`.\n`,
  },
];
