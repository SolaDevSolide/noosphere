import { render } from '@testing-library/react';
import type { ReactNode } from 'react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { vi } from 'vitest';
import { api } from '../api/client';
import type { Me } from '../api/types';
import { AuthProvider } from '../auth/auth';
import { LandscapeProvider } from '../explorer/landscape';
import { I18nProvider } from '../i18n';
import { ToastProvider } from '../ui/Toast';
import { memberLandscape } from './fixture';

export const ME: Record<string, Me> = {
  alice: { username: 'alice', displayName: 'Alice Martin', role: 'admin', language: 'en', teams: [] },
  bob: {
    username: 'bob',
    displayName: 'Bob Durand',
    role: 'member',
    language: 'en',
    teams: [{ id: 'team-finance', name: 'Team Finance' }],
  },
  david: {
    username: 'david',
    displayName: 'David Leroy',
    role: 'member',
    language: 'en',
    teams: [{ id: 'team-crm', name: 'Team CRM' }],
  },
};

/** Renders a signed-in page at `path`, with the API calls the shell makes answered from the demo landscape. */
export function renderPage(ui: ReactNode, { path, route, me }: { path: string; route: string; me: Me }) {
  vi.spyOn(api, 'me').mockResolvedValue(me);
  vi.spyOn(api, 'landscape').mockResolvedValue(memberLandscape());
  vi.spyOn(api, 'inbox').mockResolvedValue({ toValidate: 0, mineOpen: 0 });
  vi.spyOn(api, 'settings').mockResolvedValue({
    instanceName: 'Noosphere',
    organisationName: '',
    defaultLanguage: 'en',
    version: 0,
  });
  return render(
    <I18nProvider initial="en">
      <MemoryRouter initialEntries={[path]}>
        <AuthProvider>
          <ToastProvider>
            <LandscapeProvider>
              <Routes>
                <Route path={route} element={ui} />
              </Routes>
            </LandscapeProvider>
          </ToastProvider>
        </AuthProvider>
      </MemoryRouter>
    </I18nProvider>,
  );
}
