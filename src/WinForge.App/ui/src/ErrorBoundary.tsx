import { Component, type ErrorInfo, type ReactNode } from 'react';
import { t } from './localization';

export class ErrorBoundary extends Component<{ children: ReactNode; onBack?: () => void }, { failed: boolean }> {
  state = { failed: false };
  static getDerivedStateFromError() { return { failed: true }; }
  componentDidCatch(error: Error, info: ErrorInfo) {
    console.error('WinForge UI render failed', error, info.componentStack);
  }
  render() {
    if (!this.state.failed) return this.props.children;
    return <div className="ui-recovery" role="alert"><h2>{t('recovery.title')}</h2>
      <p>{t('recovery.description')}</p>
      <button className="primary-button" onClick={() => { this.setState({ failed: false }); this.props.onBack?.(); }}>{t('recovery.retry')}</button>
      <button className="secondary-button" onClick={() => window.location.reload()}>{t('recovery.reload')}</button>
    </div>;
  }
}
