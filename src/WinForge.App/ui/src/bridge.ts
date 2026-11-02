let requestSequence = 0;
const pageRequestPrefix = Math.random().toString(36).slice(2);
export function send(command: string, payload: Record<string, unknown> = {}) {
  window.chrome?.webview?.postMessage({ command, payload, requestId: `${pageRequestPrefix}-${++requestSequence}` });
}

