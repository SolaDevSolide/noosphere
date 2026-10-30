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
import { AccessPage } from './access/AccessPage';
import { DomainRecord } from './docpages/DomainRecord';
import { NewPageEditor, PageEditor } from './docpages/PageEditor';
import { PagesHome } from './docpages/PagesHome';
import { PageView } from './docpages/PageView';
import { PagesProvider } from './docpages/store';
import { ApplicationRecord } from './records/ApplicationRecord';
import { FlowRecord } from './records/FlowRecord';
import { TeamRecord } from './records/TeamRecord';
import { InboxProvider } from './review/inbox';
import { ProposalPage } from './review/ProposalPage';
import { ReviewPage } from './review/ReviewPage';
import { ToastProvider } from './ui/Toast';
import { ConsentPage } from './agents/ConsentPage';
import { ConsolePage } from './agents/ConsolePage';
import { MyAgentsPage } from './agents/MyAgentsPage';
import { DemoProvider, useDemo } from './demo/context';
import { DemoToolbox } from './demo/DemoToolbox';

function Booting() {
  return <div className="login" aria-busy="true" />;
}

/** The signed-in pages; the demo toolbox loads them again from scratch after a persona switch or a reset. */
function SignedInPages() {
  const { epoch } = useDemo();
  return (
    <LandscapeProvider key={epoch}>
      <InboxProvider>
        <PagesProvider>
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
            <Route path="/admin/access" element={<AccessPage />} />
            <Route path="/admin/settings" element={<SettingsPage />} />
            <Route path="/admin/trash" element={<TrashPage />} />
            <Route path="/import-export" element={<ImportExportPage />} />
            <Route path="/domains/:id" element={<DomainRecord />} />
            <Route path="/pages" element={<PagesHome />} />
            <Route path="/pages/new" element={<NewPageEditor />} />
            <Route path="/pages/:id" element={<PageView />} />
            <Route path="/pages/:id/edit" element={<PageEditor />} />
            <Route path="/my-agents" element={<MyAgentsPage />} />
            <Route path="/admin/agents" element={<ConsolePage />} />
            <Route path="*" element={<NotFound />} />
          </Routes>
          <DemoToolbox />
        </PagesProvider>
      </InboxProvider>
    </LandscapeProvider>
  );
}

function SignedIn() {
  return (
    <RequireAuth fallback={<Booting />}>
      <DemoProvider>
        <SignedInPages />
      </DemoProvider>
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
                <Route
                  path="/oauth/consent"
                  element={
                    <RequireAuth fallback={<Booting />}>
                      <ConsentPage />
                    </RequireAuth>
                  }
                />
                <Route path="*" element={<SignedIn />} />
              </Routes>
            </ToastProvider>
          </AuthProvider>
        </BrowserRouter>
      </SettingsProvider>
    </I18nProvider>
  );
}
