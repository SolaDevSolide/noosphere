import { Link } from 'react-router-dom';
import { useI18n } from '../i18n';
import { PageShell } from '../records/common';

export function NotFound() {
  const { t } = useI18n();
  return (
    <PageShell narrow>
      <div className="page-h">
        <div className="ph-t">
          <h1>{t.notFound.title}</h1>
          <p>{t.notFound.text}</p>
        </div>
      </div>
      <Link className="btn primary" to="/">
        {t.common.backToExplorer}
      </Link>
    </PageShell>
  );
}
