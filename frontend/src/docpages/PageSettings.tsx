import { useMemo, useState, type KeyboardEvent } from 'react';
import type { AttachedTo, Visibility } from '../api/types';
import { useAuth } from '../auth/auth';
import { teamName } from '../explorer/bits';
import { useLandscape } from '../explorer/landscape';
import { useI18n } from '../i18n';
import { describe, Field } from '../records/common';
import { IconLock, IconX } from '../ui/icons';
import { LinkOptions, useLinkSearch, type LinkOption } from './LinkSearch';
import { ATTACH_KINDS, descendants, flattenTree, recordOwner, SLUG } from './model';
import { AttachedChip } from './PageView';
import { usePages } from './store';

export interface PageDraft {
  title: string;
  slug: string;
  /** False while the address follows the title (new pages). */
  slugTouched: boolean;
  body: string;
  parentId: string;
  attachedTo: AttachedTo | null;
  /** Empty: the owner of the attached record. */
  ownerTeamId: string;
  tags: string[];
  visibility: Visibility;
}

export const TAG = /^[\p{L}\p{N}][\p{L}\p{N}_-]*$/u;

export function validateDraft(
  t: ReturnType<typeof useI18n>['t'],
  d: PageDraft,
  o: { isNew: boolean; ownerRequired: boolean; takenIds: Set<string> },
): Record<string, string> {
  const e: Record<string, string> = {};
  if (!d.title.trim()) e['ps-title'] = t.record.errors.required;
  else if (d.title.trim().length > 200) e['ps-title'] = t.record.errors.tooLong(200);
  if (o.isNew) {
    if (!d.slug) e['ps-slug'] = t.record.errors.required;
    else if (!SLUG.test(d.slug) || d.slug.length > 64) e['ps-slug'] = t.pages.errors.slug;
    else if (o.takenIds.has(d.slug)) e['ps-slug'] = t.pages.errors.slugTaken;
  }
  if (o.ownerRequired && !d.ownerTeamId && !d.attachedTo) e['ps-team'] = t.pages.errors.ownerRequired;
  if (d.body.length > 200_000) e['ps-body'] = t.record.errors.tooLong(200_000);
  return e;
}

function AttachField({
  value,
  onChange,
  error,
}: {
  value: AttachedTo | null;
  onChange: (v: AttachedTo | null) => void;
  error?: string;
}) {
  const { t } = useI18n();
  const [picking, setPicking] = useState(false);
  const [q, setQ] = useState('');
  const [active, setActive] = useState(0);
  const items = useLinkSearch(picking ? q : null, ATTACH_KINDS);
  const pick = (o: LinkOption) => {
    onChange({ kind: o.kind, id: o.id });
    setPicking(false);
    setQ('');
  };
  const key = (e: KeyboardEvent) => {
    const n = items?.length ?? 0;
    if (e.key === 'ArrowDown' && n) {
      e.preventDefault();
      setActive((a) => (a + 1) % n);
    } else if (e.key === 'ArrowUp' && n) {
      e.preventDefault();
      setActive((a) => (a - 1 + n) % n);
    } else if (e.key === 'Enter') {
      e.preventDefault();
      if (items?.[active]) pick(items[active]);
    } else if (e.key === 'Escape') {
      e.preventDefault();
      e.stopPropagation();
      setPicking(false);
    }
  };
  return (
    <div className="field">
      <span className="lbl" id="ps-att-l">
        {t.pages.attachedTo}
      </span>
      <div className="attf" aria-labelledby="ps-att-l">
        {value ? (
          <AttachedChip kind={value.kind} id={value.id} badge={false} />
        ) : (
          <span className="muted">{t.pages.notAttached}</span>
        )}
        <span className="acts2">
          <button
            type="button"
            className="btn ghost sm"
            aria-expanded={picking}
            onClick={() => {
              setPicking((p) => !p);
              setActive(0);
            }}
          >
            {value ? t.pages.change : t.pages.attach}
          </button>
          {value && (
            <button
              type="button"
              className="btn ghost sm"
              onClick={() => onChange(null)}
              aria-label={t.pages.detach}
              title={t.pages.detach}
            >
              <IconX size={14} />
            </button>
          )}
        </span>
      </div>
      {picking && (
        <div className="attpick">
          <input
            className="input"
            autoFocus
            role="combobox"
            aria-expanded="true"
            aria-controls="ps-att-list"
            aria-activedescendant={items?.length ? `ps-att-${active}` : undefined}
            aria-label={t.pages.attachSearch}
            placeholder={t.pages.attachSearch}
            value={q}
            onChange={(e) => {
              setQ(e.target.value);
              setActive(0);
            }}
            onKeyDown={key}
          />
          <div className="ac" role="listbox" id="ps-att-list" aria-label={t.pages.ac.label}>
            <LinkOptions id="ps-att" items={items} active={active} query={q} onPick={pick} onHover={setActive} />
          </div>
        </div>
      )}
      {error ? (
        <span className="err" role="alert">
          {error}
        </span>
      ) : (
        <small>{t.pages.attachedHint}</small>
      )}
    </div>
  );
}

function TagsField({ value, onChange }: { value: string[]; onChange: (v: string[]) => void }) {
  const { t } = useI18n();
  const [text, setText] = useState('');
  const [err, setErr] = useState<string | null>(null);
  const add = () => {
    const tag = text.trim().replace(/^#/, '').toLowerCase();
    if (!tag) return;
    if (!TAG.test(tag) || tag.length > 50) {
      setErr(t.pages.errors.tag);
      return;
    }
    if (!value.includes(tag) && value.length < 20) onChange([...value, tag]);
    setText('');
    setErr(null);
  };
  return (
    <div className="field">
      <label htmlFor="ps-tags">{t.pages.tagsF}</label>
      <div className="tagsf">
        {value.map((tag) => (
          <span key={tag} className="tagc">
            {tag}
            <button
              type="button"
              aria-label={t.pages.removeTag(tag)}
              onClick={() => onChange(value.filter((x) => x !== tag))}
            >
              <IconX size={12} />
            </button>
          </span>
        ))}
        <input
          id="ps-tags"
          value={text}
          placeholder={t.pages.addTag}
          aria-invalid={err ? true : undefined}
          aria-describedby={err ? 'ps-tags-err' : undefined}
          onChange={(e) => {
            setText(e.target.value);
            setErr(null);
          }}
          onKeyDown={(e) => {
            if (e.key === 'Enter' || e.key === ',') {
              e.preventDefault();
              add();
            } else if (e.key === 'Backspace' && !text && value.length) onChange(value.slice(0, -1));
          }}
          onBlur={add}
        />
      </div>
      {err && (
        <span className="err" id="ps-tags-err" role="alert">
          {err}
        </span>
      )}
    </div>
  );
}

/** Title, address, parent, attachment, owner, tags and visibility of the page being edited. */
export function PageSettings({
  draft,
  set,
  isNew,
  pageId,
  errors,
  open,
  onToggle,
  ownerHint,
}: {
  draft: PageDraft;
  set: <K extends keyof PageDraft>(k: K, v: PageDraft[K]) => void;
  isNew: boolean;
  pageId: string | null;
  errors: Record<string, string>;
  open: boolean;
  onToggle: (open: boolean) => void;
  ownerHint: string;
}) {
  const { t } = useI18n();
  const { isAdmin } = useAuth();
  const { model } = useLandscape();
  const { tree, list } = usePages();
  const parents = useMemo(() => {
    const blocked = pageId && list ? descendants(list, pageId) : new Set<string>();
    const rows = tree ? flattenTree(tree) : [];
    return rows
      .filter((r) => !r.node.restricted && !blocked.has(r.node.id))
      .map((r) => ({ id: r.node.id, label: ' '.repeat(r.depth) + (r.node.title ?? r.node.id) }));
  }, [tree, list, pageId]);
  const teams = useMemo(
    () => (model ? [...model.teams.values()].sort((a, b) => a.name.localeCompare(b.name)) : []),
    [model],
  );
  const attachedOwner = draft.attachedTo ? recordOwner(model, draft.attachedTo.kind, draft.attachedTo.id) : null;
  return (
    <aside className="settings-wrap">
      <details className="box settings" open={open} onToggle={(e) => onToggle((e.target as HTMLDetailsElement).open)}>
        <summary>
          <h2>{t.pages.settings}</h2>
          <span className="mob-only muted" style={{ fontSize: 12.5 }}>
            {t.pages.settingsSub}
          </span>
        </summary>
        <div className="form">
          <Field id="ps-title" label={t.pages.titleF} required error={errors['ps-title']}>
            <input
              className="input"
              {...describe('ps-title', undefined, errors['ps-title'])}
              value={draft.title}
              maxLength={210}
              autoComplete="off"
              onChange={(e) => set('title', e.target.value)}
            />
          </Field>
          {isNew ? (
            <Field
              id="ps-slug"
              label={t.pages.address}
              required
              hint={t.pages.addressNewHint}
              error={errors['ps-slug']}
            >
              <span className="slug-in">
                /pages/
                <input
                  className="input"
                  {...describe('ps-slug', t.pages.addressNewHint, errors['ps-slug'])}
                  value={draft.slug}
                  maxLength={64}
                  autoComplete="off"
                  spellCheck={false}
                  onChange={(e) => {
                    set('slug', e.target.value.toLowerCase());
                    set('slugTouched', true);
                  }}
                />
              </span>
            </Field>
          ) : (
            <div className="field">
              <span className="lbl">{t.pages.address}</span>
              <div className="slugf" aria-describedby="ps-slug-hint">
                <IconLock />
                /pages/{pageId}
              </div>
              <small id="ps-slug-hint">{t.pages.addressHint}</small>
            </div>
          )}
          <Field id="ps-parent" label={t.pages.parent} error={errors['ps-parent']}>
            <select
              className="select"
              {...describe('ps-parent', undefined, errors['ps-parent'])}
              value={draft.parentId}
              onChange={(e) => set('parentId', e.target.value)}
            >
              <option value="">{t.pages.topLevel}</option>
              {parents.map((p) => (
                <option key={p.id} value={p.id}>
                  {p.label}
                </option>
              ))}
            </select>
          </Field>
          <AttachField value={draft.attachedTo} onChange={(v) => set('attachedTo', v)} error={errors['ps-att']} />
          <Field id="ps-team" label={t.pages.ownerTeam} hint={ownerHint} error={errors['ps-team']}>
            <select
              className="select"
              {...describe('ps-team', ownerHint, errors['ps-team'])}
              value={draft.ownerTeamId}
              onChange={(e) => set('ownerTeamId', e.target.value)}
            >
              <option value="">
                {draft.attachedTo
                  ? t.pages.ownerOfAttached(attachedOwner && model ? teamName(model, attachedOwner) : t.pages.admins)
                  : isAdmin
                    ? t.pages.admins
                    : t.pages.noOwner}
              </option>
              {teams.map((x) => (
                <option key={x.id} value={x.id}>
                  {x.name}
                </option>
              ))}
            </select>
          </Field>
          <TagsField value={draft.tags} onChange={(v) => set('tags', v)} />
          <div className="field">
            <span className="lbl" id="ps-vis-l">
              {t.pages.visibilityF}
            </span>
            <div className="seg full" role="group" aria-labelledby="ps-vis-l">
              <button
                type="button"
                aria-pressed={draft.visibility === 'everyone'}
                onClick={() => set('visibility', 'everyone')}
              >
                {t.visibility.everyone}
              </button>
              <button
                type="button"
                aria-pressed={draft.visibility === 'restricted'}
                onClick={() => set('visibility', 'restricted')}
              >
                <IconLock />
                {t.visibility.restricted}
              </button>
            </div>
            <small>{t.access.visibilityHint}</small>
          </div>
        </div>
      </details>
    </aside>
  );
}
