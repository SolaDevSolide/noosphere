import { BrowserRouter, Route, Routes } from 'react-router-dom';
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
import { ToastProvider } from './ui/Toast';

function Booting() {
  return <div className="login" aria-busy="true" />;
}

function SignedIn() {
  return (
    <RequireAuth fallback={<Booting />}>
      <LandscapeProvider>
        <Routes>
          <Route path="/" element={<Explorer />} />
          <Route path="/applications/:id/lens" element={<Explorer />} />
          <Route path="/flows/:id/lens" element={<Explorer />} />
          <Route path="/data-objects/:id" element={<Explorer />} />
          <Route path="/applications/:id" element={<ApplicationRecord />} />
          <Route path="/flows/:id" element={<FlowRecord />} />
          <Route path="/admin/users" element={<UsersPage />} />
          <Route path="/import-export" element={<ImportExportPage />} />
          <Route path="*" element={<NotFound />} />
        </Routes>
      </LandscapeProvider>
    </RequireAuth>
  );
}

export function App() {
  return (
    <I18nProvider>
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
    </I18nProvider>
  );
}
