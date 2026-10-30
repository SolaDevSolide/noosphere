import { useRef, useState } from 'react';
import { api, ApiError } from '../api/client';
import type { ImportResult } from '../api/types';
import { useAuth } from '../auth/auth';
import { useLandscape } from '../explorer/landscape';
import { useI18n } from '../i18n';
import { PageShell } from '../records/common';
import { IconDownload, IconUpload, IconWarn } from '../ui/icons';
import { useToast } from '../ui/Toast';

interface Picked {
  name: string;
  size: number;
  text: string;
  yaml: boolean;
}

export function ImportExportPage() {
  const { t } = useI18n();
  const { isAdmin } = useAuth();
  const { reload } = useLandscape();
  const toast = useToast();
  const input = useRef<HTMLInputElement>(null);
  const [file, setFile] = useState<Picked | null>(null);
  const [over, setOver] = useState(false);
  const [busy, setBusy] = useState<'dry' | 'apply' | null>(null);
  const [result, setResult] = useState<{ r: ImportResult; dry: boolean } | null>(null);
  const [error, setError] = useState<string | null>(null);

  const pick = async (f: File | undefined) => {
    if (!f) return;
    setResult(null);
    setError(null);
    const text = await f.text();
    const yaml = /\.ya?ml$/i.test(f.name);
    if (!yaml) {
      try {
        JSON.parse(text);
      } catch {
        setFile(null);
        setError(t.io.invalidJson);
        return;
      }
    }
    setFile({ name: f.name, size: f.size, text, yaml });
  };

  const run = async (dry: boolean) => {
    if (!file) return;
    if (!dry && !window.confirm(t.io.applyConfirm)) return;
    setBusy(dry ? 'dry' : 'apply');
    setError(null);
    try {
      const r = await api.importEnvelope(file.text, dry, file.yaml ? 'application/yaml' : 'application/json');
      setResult({ r, dry });
      if (!dry) {
        reload();
        toast(t.io.resultDone);
      }
    } catch (e) {
      if (e instanceof ApiError && e.status === 400) {
        const errs = Object.entries(e.fieldErrors).flatMap(([path, msgs]) =>
          (Array.isArray(msgs) ? msgs : [String(msgs)]).map((message) => ({ path, message })),
        );
        setResult({
          r: {
            created: 0,
            updated: 0,
            unchanged: 0,
            errors: errs.length ? errs : [{ message: e.detail || e.message }],
          },
          dry,
        });
      } else
        setError(
          e instanceof ApiError && e.status === 0
            ? t.common.networkError
            : e instanceof ApiError
              ? e.detail || e.message
              : t.common.genericError,
        );
    } finally {
      setBusy(null);
    }
  };

  return (
    <PageShell narrow>
      <div className="page-h">
        <div className="ph-t">
          <h1>{t.io.title}</h1>
          <p>{t.io.intro}</p>
        </div>
      </div>
      <section className="box" aria-labelledby="exp-h">
        <h2 className="big" id="exp-h">
          {t.io.exportH}
        </h2>
        <p className="muted" style={{ marginTop: 0 }}>
          {t.io.exportText}
        </p>
        <div className="page-actions">
          <a className="btn primary" href={api.exportUrl('json')} download="noosphere-landscape.json">
            <IconDownload />
            {t.io.exportJson}
          </a>
          <a className="btn" href={api.exportUrl('yaml')} download="noosphere-landscape.yaml">
            <IconDownload />
            {t.io.exportYaml}
          </a>
        </div>
      </section>
      <section className="box" aria-labelledby="imp-h">
        <h2 className="big" id="imp-h">
          {t.io.importH}
        </h2>
        <p className="muted" style={{ marginTop: 0 }}>
          {t.io.importText}
        </p>
        {!isAdmin ? (
          <div className="banner" role="note">
            <IconWarn />
            <span className="bt">{t.io.adminOnly}</span>
          </div>
        ) : (
          <>
            <div
              className={'drop' + (over ? ' over' : '')}
              onDragOver={(e) => {
                e.preventDefault();
                setOver(true);
              }}
              onDragLeave={() => setOver(false)}
              onDrop={(e) => {
                e.preventDefault();
                setOver(false);
                void pick(e.dataTransfer.files[0]);
              }}
            >
              <IconUpload size={22} />
              <button type="button" className="btn" onClick={() => input.current?.click()}>
                {t.io.choose}
              </button>
              <span className="muted">{t.io.drop}</span>
              <input
                ref={input}
                type="file"
                className="sr"
                accept=".json,.yaml,.yml,application/json"
                aria-label={t.io.fileLabel}
                onChange={(e) => {
                  void pick(e.target.files?.[0]);
                  e.target.value = '';
                }}
              />
              {file && <b>{t.io.selected(file.name, (file.size / 1024).toFixed(1))}</b>}
            </div>
            {error && (
              <div className="banner danger" role="alert" style={{ marginTop: 12 }}>
                <IconWarn />
                <span className="bt">{error}</span>
              </div>
            )}
            <div className="page-actions" style={{ marginTop: 14 }}>
              <button type="button" className="btn" disabled={!file || !!busy} onClick={() => void run(true)}>
                {busy === 'dry' ? t.io.checking : t.io.dryRun}
              </button>
              <button type="button" className="btn primary" disabled={!file || !!busy} onClick={() => void run(false)}>
                {busy === 'apply' ? t.io.applying : t.io.apply}
              </button>
            </div>
            {result && (
              <div aria-live="polite" style={{ marginTop: 16 }}>
                <div className={'banner ' + (result.r.errors.length ? 'danger' : 'ok')}>
                  <span className="bt">
                    <b>{result.dry ? t.io.resultDry : t.io.resultDone}</b>
                  </span>
                </div>
                <div className="counts">
                  {(
                    [
                      ['created', result.r.created],
                      ['updated', result.r.updated],
                      ['unchanged', result.r.unchanged],
                    ] as const
                  ).map(([k, v]) => (
                    <div key={k} className="stat">
                      <b>{v}</b>
                      <span>{t.io[k]}</span>
                    </div>
                  ))}
                  <div className="stat">
                    <b>{result.r.errors.length}</b>
                    <span>{t.io.errors(result.r.errors.length).replace(/^\d+ /, '')}</span>
                  </div>
                </div>
                {result.r.errors.length > 0 && (
                  <>
                    <h3>{t.io.errorsH}</h3>
                    <ul className="errlist">
                      {result.r.errors.map((e, i) => (
                        <li key={i}>
                          {(e.path || e.id) && <code>{e.path ?? `${e.kind ?? ''} ${e.id ?? ''}`.trim()}</code>}{' '}
                          {e.message}
                        </li>
                      ))}
                    </ul>
                  </>
                )}
              </div>
            )}
          </>
        )}
      </section>
      <section className="box" aria-labelledby="fmt-h">
        <h2 id="fmt-h">{t.io.format}</h2>
        <p className="muted" style={{ margin: 0 }}>
          {t.io.formatText}
        </p>
        <pre className="codeblock">{`{
  "formatVersion": "noosphere/v1",
  "items": [
    { "kind": "Application", "id": "billing",
      "metadata": { "name": "Billing" },
      "spec": { "domain": "finance", "kind": "app" } }
  ]
}`}</pre>
      </section>
    </PageShell>
  );
}
