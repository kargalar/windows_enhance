import React from 'react';
import { createRoot } from 'react-dom/client';
import { App } from './App';
import { ErrorBoundary } from './ErrorBoundary';
import { t } from './localization';
import './styles.css';
import './island-design.css';
import './management.css';
import './dashboard.css';
import './module-design.css';

const FeatureApp = React.lazy(() => import('./features/FeatureApp').then(module => ({ default: module.FeatureApp })));
const view = new URLSearchParams(window.location.search).get('view');
createRoot(document.getElementById('root')!).render(
  <React.StrictMode><ErrorBoundary>{view
    ? <React.Suspense fallback={<div role="status">{t('loading.generic')}</div>}><FeatureApp view={view} /></React.Suspense>
    : <App />}</ErrorBoundary></React.StrictMode>,
);
