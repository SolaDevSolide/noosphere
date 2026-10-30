import { useEffect, useLayoutEffect, useRef, useState } from 'react';
import { FLOW_STATUSES, FLOW_TYPES, isRestrictedFlow, type Sensitivity } from '../api/types';
import {
  anyFilter,
  appMeetsSens,
  EMPTY_FILTERS,
  filterCount,
  makeMatcher,
  matches,
  type Filters,
} from '../domain/model';
import { useAuth } from '../auth/auth';
import { useI18n } from '../i18n';
import { IconFilter, IconWarn, TypeIcon } from '../ui/icons';
import { Sdot, objName } from './bits';
import { domainColor } from './graph/palette';
import { useLandscape } from './landscape';
import { presetOn, toggleIn, togglePreset, type PresetId } from './filterOps';

const SENS_BASE: Sensitivity[] = ['internal', 'personal', 'sensitive'];

/** The one Filter button and popover (a bottom sheet on phones) shared by every explorer view. */
export function FilterControl({ appsShown }: { appsShown?: number }) {
  const { t } = useI18n();
  const { model, filters, setFilters } = useLandscape();
  const { me } = useAuth();
  const [open, setOpen] = useState(false);
  const btn = useRef<HTMLButtonElement>(null);
  const pop = useRef<HTMLDivElement>(null);
  const [pos, setPos] = useState<{ top: number; left: number } | null>(null);
  const width = 380;

  useLayoutEffect(() => {
    if (!open || !btn.current) return;
    const place = () => {
      const r = btn.current!.getBoundingClientRect();
      setPos({ top: r.bottom + 8, left: Math.max(12, Math.min(window.innerWidth - width - 12, r.right - width)) });
    };
    place();
    window.addEventListener('resize', place);
    return () => window.removeEventListener('resize', place);
  }, [open]);

  useEffect(() => {
    if (!open) return;
    pop.current?.querySelector<HTMLElement>('.preset, .chip')?.focus({ preventScroll: true });
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        e.stopPropagation();
        setOpen(false);
        btn.current?.focus({ preventScroll: true });
      }
    };
    document.addEventListener('keydown', onKey, true);
    return () => document.removeEventListener('keydown', onKey, true);
  }, [open]);

  if (!model) return null;
  const guest = me?.role === 'guest';
  const count = filterCount(filters);
  const appTotal = model.appIds.length;
  const appsMeeting = model.appIds.filter((id) => appMeetsSens(model.apps.get(id), filters.appSens)).length;
  const total = model.flows.length;
  const nMatch = model.flows.filter(makeMatcher(model, filters)).length;
  const sensVals = [...SENS_BASE];
  if (model.flows.some((f) => !isRestrictedFlow(f) && f.sensitivity === 'public')) sensVals.unshift('public');
  const presetCount = (p: PresetId) => model.flows.filter((f) => matches(f, togglePreset(EMPTY_FILTERS, p))).length;
  const set = (f: Filters) => setFilters(f);
  const close = () => {
    setOpen(false);
    btn.current?.focus({ preventScroll: true });
  };

  const presets: { id: PresetId; label: string; desc: string; icon: React.ReactNode }[] = [
    {
      id: 'sensitive',
      label: t.filters.presetSensitive,
      desc: t.filters.presetSensitiveDesc,
      icon: <Sdot s="sensitive" />,
    },
    {
      id: 'changes',
      label: t.filters.presetChanges,
      desc: t.filters.presetChangesDesc,
      icon: (
        <span style={{ color: 'var(--warn-fg)', display: 'flex' }}>
          <IconWarn />
        </span>
      ),
    },
    {
      id: 'deprecated',
      label: t.filters.presetDeprecated,
      desc: t.filters.presetDeprecatedDesc,
      icon: (
        <span className="tg danger" style={{ padding: '0 4px' }}>
          D
        </span>
      ),
    },
  ];

  return (
    <>
      <button
        ref={btn}
        type="button"
        className="fbtn"
        aria-haspopup="dialog"
        aria-expanded={open}
        aria-controls="fpop"
        onClick={() => setOpen((o) => !o)}
      >
        <IconFilter />
        <span className="flabel">{t.top.filter}</span>
        {count > 0 && <span className="cnt">{count}</span>}
      </button>
      {open && (
        <>
          <div className="fback" onClick={() => setOpen(false)} aria-hidden="true" />
          <div
            ref={pop}
            id="fpop"
            className="fpop"
            role="dialog"
            aria-label={t.filters.title}
            style={pos ? { top: pos.top, left: pos.left } : { visibility: 'hidden' }}
          >
            <span className="fp-grip" aria-hidden="true" />
            <div className="fp-h">
              <b>{t.top.filter}</b>
              <button type="button" className="iconbtn" aria-label={t.filters.closeLabel} onClick={close}>
                ✕
              </button>
            </div>
            {!guest && (
              <div className="fp-sec fp-new">
                <span className="fp-l" id="fp-lv-l">
                  {t.access.filterTitle}
                  <span className="newtag">{t.access.newTag}</span>
                </span>
                <div className="seg lvseg" role="group" aria-labelledby="fp-lv-l" aria-describedby="fp-lv-help">
                  <button
                    type="button"
                    aria-pressed={!filters.appSens}
                    onClick={() => set({ ...filters, appSens: null })}
                  >
                    {t.access.any}
                  </button>
                  {(['internal', 'personal', 'sensitive'] as const).map((v) => (
                    <button
                      key={v}
                      type="button"
                      aria-pressed={filters.appSens === v}
                      onClick={() => set({ ...filters, appSens: filters.appSens === v ? null : v })}
                    >
                      <Sdot s={v} />
                      {t.access.levelShort[v]}
                    </button>
                  ))}
                </div>
                <small className="fp-help" id="fp-lv-help">
                  {t.access.filterHelp}
                </small>
              </div>
            )}
            <div className="fp-sec">
              <span className="fp-l">{t.filters.quick}</span>
              <div className="fp-presets">
                {presets.map((p) => (
                  <button
                    key={p.id}
                    type="button"
                    className="preset"
                    aria-pressed={presetOn(filters, p.id)}
                    onClick={() => set(togglePreset(filters, p.id))}
                  >
                    <span className="pic">{p.icon}</span>
                    <span>
                      <b>{p.label}</b>
                      <small>{p.desc}</small>
                    </span>
                    <span className="pc">{presetCount(p.id)}</span>
                  </button>
                ))}
              </div>
            </div>
            <div className="fp-sec">
              <span className="fp-l">{t.filters.flowType}</span>
              <div className="fp-chips">
                {FLOW_TYPES.map((v) => (
                  <button
                    key={v}
                    type="button"
                    className="chip"
                    aria-pressed={filters.type.includes(v)}
                    onClick={() => set(toggleIn(filters, 'type', v))}
                  >
                    <span className={'t-' + v} style={{ display: 'flex', color: 'var(--tc)' }}>
                      <TypeIcon type={v} />
                    </span>
                    {t.typeChip[v]}
                  </button>
                ))}
              </div>
            </div>
            <div className="fp-sec">
              <span className="fp-l">{t.filters.data}</span>
              <div className="fp-chips">
                {model.objectIds.map((v) => (
                  <button
                    key={v}
                    type="button"
                    className="chip"
                    aria-pressed={filters.obj.includes(v)}
                    onClick={() => set(toggleIn(filters, 'obj', v))}
                  >
                    <Sdot s={model.objects.get(v)!.sensitivity} />
                    {objName(model, v)}
                  </button>
                ))}
              </div>
            </div>
            <div className="fp-sec">
              <span className="fp-l">{t.filters.sensitivity}</span>
              <div className="fp-chips">
                {sensVals.map((v) => (
                  <button
                    key={v}
                    type="button"
                    className="chip"
                    aria-pressed={filters.sens.includes(v)}
                    onClick={() => set(toggleIn(filters, 'sens', v))}
                  >
                    <Sdot s={v} />
                    {t.sens[v]}
                  </button>
                ))}
              </div>
            </div>
            <div className="fp-sec">
              <span className="fp-l">{t.filters.status}</span>
              <div className="fp-chips">
                {FLOW_STATUSES.map((v) => (
                  <button
                    key={v}
                    type="button"
                    className="chip"
                    aria-pressed={filters.status.includes(v)}
                    onClick={() => set(toggleIn(filters, 'status', v))}
                  >
                    {t.status[v]}
                  </button>
                ))}
              </div>
            </div>
            <div className="fp-sec">
              <span className="fp-l">{t.filters.domains}</span>
              <div className="fp-chips">
                {model.domains.map((d) => (
                  <button
                    key={d.id}
                    type="button"
                    className="chip dom"
                    aria-pressed={!filters.hidden.includes(d.id)}
                    onClick={() => set(toggleIn(filters, 'hidden', d.id))}
                  >
                    <span
                      className={'dot' + (d.id === 'external' ? ' ext' : '')}
                      style={{ '--dc': domainColor(model, d.id) } as React.CSSProperties}
                    />
                    {d.name}
                  </button>
                ))}
              </div>
            </div>
            <div className="fp-sec">
              <button
                type="button"
                className="switch"
                role="switch"
                aria-checked={filters.isolated}
                onClick={() => set({ ...filters, isolated: !filters.isolated })}
              >
                <span className="tr" />
                {t.filters.hideIsolated}
              </button>
            </div>
            <div className="fp-f">
              <span className="fcount">
                {filters.appSens
                  ? t.access.appsShown(appsMeeting, appTotal)
                  : (anyFilter(filters) ? t.filters.matchCount(nMatch, total) : t.filters.allShown(total)) +
                    (appsShown != null ? t.filters.appsSuffix(appsShown) : '')}
              </span>
              <button type="button" className="aclear" onClick={() => set(EMPTY_FILTERS)}>
                {t.common.clearAll}
              </button>
              <button type="button" className="btn primary" onClick={close}>
                {t.common.done}
              </button>
            </div>
          </div>
        </>
      )}
    </>
  );
}

/** Active filters as removable chips (in the toolbar). */
export function ActiveChips({ appsShown }: { appsShown?: number }) {
  const { t } = useI18n();
  const { model, filters, setFilters } = useLandscape();
  if (!model) return null;
  const chips: { key: string; label: string; aria: string; next: Filters }[] = [];
  const add = (key: string, label: string, next: Filters, aria = t.filters.remove(label)) =>
    chips.push({ key, label, aria, next });
  if (filters.changeOnly) add('change', t.filters.presetChanges, { ...filters, changeOnly: false });
  for (const v of filters.type) add('type' + v, t.typeChip[v], toggleIn(filters, 'type', v));
  for (const v of filters.obj) add('obj' + v, objName(model, v), toggleIn(filters, 'obj', v));
  for (const v of filters.sens) add('sens' + v, t.sens[v], toggleIn(filters, 'sens', v));
  for (const v of filters.status) add('status' + v, t.status[v], toggleIn(filters, 'status', v));
  for (const v of filters.hidden) {
    const name = model.domain.get(v)?.name ?? v;
    add('hid' + v, t.filters.without(name), toggleIn(filters, 'hidden', v), t.filters.showDomain(name));
  }
  if (filters.appSens) {
    const label = t.access.filterChip(t.access.levelShort[filters.appSens]);
    add('appsens', label, { ...filters, appSens: null });
  }
  if (filters.isolated) add('iso', t.filters.isolatedChip, { ...filters, isolated: false }, t.filters.showUnlinked);
  if (!chips.length) return <div className="active" aria-label={t.filters.active} />;
  const n = model.flows.filter(makeMatcher(model, filters)).length;
  return (
    <div className="active" aria-label={t.filters.active}>
      <span className="alabel">{appsShown != null ? t.filters.appsDot(appsShown) : t.filters.flowsDot(n)}</span>
      {chips.map((c) => (
        <button key={c.key} type="button" className="achip" aria-label={c.aria} onClick={() => setFilters(c.next)}>
          {c.label}
          <span className="x" aria-hidden="true">
            ×
          </span>
        </button>
      ))}
      <button type="button" className="aclear" onClick={() => setFilters(EMPTY_FILTERS)}>
        {t.common.clear}
      </button>
    </div>
  );
}
