import '@fontsource-variable/inter';
import '@fontsource-variable/jetbrains-mono';
import './index.css';
import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { App } from './App';

async function start() {
  // The mock API exists only in development, behind VITE_MOCK=1; production builds never include it.
  if (import.meta.env.DEV && import.meta.env.VITE_MOCK === '1') {
    const { installMockApi } = await import('./mocks/server');
    installMockApi();
  }
  createRoot(document.getElementById('root')!).render(
    <StrictMode>
      <App />
    </StrictMode>,
  );
}

void start();
