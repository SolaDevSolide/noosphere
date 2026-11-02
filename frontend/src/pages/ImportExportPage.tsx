import { useState } from 'react';
import { ExportDialog } from '../access/ExportDialog';
import { useI18n } from '../i18n';
import { ImportPanel } from '../imports/ImportPanel';
import { PageShell } from '../records/common';
import { IconDownload } from '../ui/icons';

/** Import and export: the import from other formats (with its dry run), and the export of the landscape. */
export function ImportExportPage() {
  const { t } = useI18n();
  const [exporting, setExporting] = useState(false);
  return (
    <PageShell title={t.imports.title}>
      <div className="page-h">
        <div className="ph-t">
          <h1>{t.imports.title}</h1>
          <p>{t.imports.intro}</p>
        </div>
      </div>
      <ImportPanel />
      <section className="box" aria-labelledby="exp-h">
        <h2 className="big" id="exp-h">
          {t.io.exportH}
        </h2>
        <p className="muted" style={{ marginTop: 0 }}>
          {t.access.exportVisibleText}
        </p>
        <div className="page-actions">
          <button type="button" className="btn primary" onClick={() => setExporting(true)}>
            <IconDownload />
            {t.access.exportTitle}
          </button>
        </div>
        {exporting && <ExportDialog onClose={() => setExporting(false)} />}
      </section>
    </PageShell>
  );
}
