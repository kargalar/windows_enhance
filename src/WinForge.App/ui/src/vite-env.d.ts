/// <reference types="vite/client" />

declare global {
  interface Window {
    chrome?: {
      webview?: {
        postMessage(message: unknown): void;
        addEventListener(type: 'message', callback: (event: MessageEvent) => void): void;
        removeEventListener(type: 'message', callback: (event: MessageEvent) => void): void;
      };
    };
  }
}

export {};
