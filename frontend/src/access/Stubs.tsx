import type { ReactNode } from 'react';
import { useI18n } from '../i18n';
import { IconEyeOff, IconInfo, IconLock } from '../ui/icons';
import { useToast } from '../ui/Toast';

type StubKind = 'application' | 'flow' | 'page';

/** What a member without access gets for a restricted record: a lock, without its name. */
export function RestrictedStub({ kind, badge, sub }: { kind: StubKind; badge: ReactNode; sub?: string }) {
  const { t } = useI18n();
  return (
    <>
      <div className="page-h stub-h">
        <div className="ph-t">
          {badge}
          <h1>
            <IconLock size={20} />
            {t.access.restrictedName[kind]}
          </h1>
          {sub && <p>{sub}</p>}
        </div>
      </div>
      <section className="box stub-box">
        <div className="pn-lock">
          <span className="lk">
            <IconLock size={18} />
          </span>
          <div>
            <b>{t.access.noAccessTitle[kind]}</b>
            <p>{t.access.noAccessText[kind]}</p>
          </div>
        </div>
      </section>
    </>
  );
}

/** What a guest gets for a record not shared with them: its name and place, and who to ask. */
export function GuestStub({
  name,
  badge,
  sub,
  fields,
}: {
  name: string;
  badge: ReactNode;
  sub?: string;
  fields: [string, ReactNode][];
}) {
  const { t } = useI18n();
  const toast = useToast();
  return (
    <>
      <div className="page-h">
        <div className="ph-t">
          {badge}
          <h1>{name}</h1>
          {sub && <p>{sub}</p>}
        </div>
      </div>
      <section className="box stub-box">
        <div className="pn-lock">
          <span className="lk">
            <IconEyeOff size={18} />
          </span>
          <div>
            <b>{t.access.notSharedTitle}</b>
            <p>{t.access.notSharedText}</p>
            <button type="button" className="btn sm" onClick={() => toast(t.access.askOwner)}>
              {t.access.whoCanShare}
            </button>
          </div>
        </div>
        {fields.length > 0 && (
          <dl className="stub-fields">
            {fields.map(([k, v]) => (
              <div key={k} style={{ display: 'contents' }}>
                <dt>{k}</dt>
                <dd>{v}</dd>
              </div>
            ))}
          </dl>
        )}
      </section>
    </>
  );
}

/** Banner on a record shared with the guest reading it. */
export function SharedBanner({ owner }: { owner: string }) {
  const { t } = useI18n();
  return (
    <div className="banner info" role="note">
      <IconInfo />
      <span className="bt">
        <b>{t.access.sharedBanner(owner)}</b> {t.access.sharedBannerText}
      </span>
    </div>
  );
}
