import { createServer } from 'vite';

// Test workers must not rewrite the dependency cache of a running dev server.
export function createTestServer() {
  return createServer({
    cacheDir: `node_modules/.vite-tests/${process.pid}`,
    optimizeDeps: { noDiscovery: true, include: [] },
    server: { middlewareMode: true, hmr: false, ws: false },
    appType: 'custom',
  });
}
