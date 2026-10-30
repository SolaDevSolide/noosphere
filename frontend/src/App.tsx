import { BrowserRouter, Route, Routes } from 'react-router-dom';
import { SettingsPage } from './admin/SettingsPage';
import { TrashPage } from './admin/TrashPage';
import { SettingsProvider } from './app/settings';
import { AuthProvider, RequireAuth } from './auth/auth';
import { Explorer } from './explorer/Explorer';
import { LandscapeProvider } from './explorer/landscape';
import { I18nProvider } from './i18n';
import { ImportExportPage } from './pages/ImportExportPage';
import { LoginPage } from './pages/LoginPage';
import { NotFound } from './pages/NotFound';
import { UsersPage } from './pages/UsersPage';
import { ApplicationRecord } from './records/ApplicationRecord';
import { FlowRecord } from './records/FlowRecord';
import { TeamRecord } from './records/TeamRecord';
import { InboxProvider } from './review/inbox';
import { ProposalPage } from './review/ProposalPage';
import { ReviewPage } from './review/ReviewPage';
import { ToastProvider } from './ui/Toast';

function Booting() {
  return <div className="login" aria-busy="true" />;
}

function SignedIn() {
  return (
    <RequireAuth fallback={<Booting />}>
      <LandscapeProvider>
        <InboxProvider>
          <Routes>
            <Route path="/" element={<Explorer />} />
            <Route path="/applications/:id/lens" element={<Explorer />} />
            <Route path="/flows/:id/lens" element={<Explorer />} />
            <Route path="/data-objects/:id" element={<Explorer />} />
            <Route path="/applications/:id" element={<ApplicationRecord />} />
            <Route path="/flows/:id" element={<FlowRecord />} />
            <Route path="/teams/:id" element={<TeamRecord />} />
            <Route path="/review" element={<ReviewPage />} />
            <Route path="/review/:id" element={<ProposalPage />} />
            <Route path="/admin/users" element={<UsersPage />} />
            <Route path="/admin/settings" element={<SettingsPage />} />
            <Route path="/admin/trash" element={<TrashPage />} />
            <Route path="/import-export" element={<ImportExportPage />} />
            <Route path="*" element={<NotFound />} />
          </Routes>
        </InboxProvider>
      </LandscapeProvider>
    </RequireAuth>
  );
}

export function App() {
  return (
    <I18nProvider>
      <SettingsProvider>
        <BrowserRouter>
          <AuthProvider>
            <ToastProvider>
              <Routes>
                <Route path="/login" element={<LoginPage />} />
                <Route path="*" element={<SignedIn />} />
              </Routes>
            </ToastProvider>
          </AuthProvider>
        </BrowserRouter>
      </SettingsProvider>
    </I18nProvider>
  );
}
