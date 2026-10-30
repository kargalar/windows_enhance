import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

export default defineConfig(({ command }) => ({
  base: './',
  plugins: [
    react(),
    {
      name: 'winforge-content-security-policy',
      transformIndexHtml(html) {
        if (command !== 'build') return html;
        return {
          html,
          tags: [{
            tag: 'meta',
            attrs: {
              'http-equiv': 'Content-Security-Policy',
              content: "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; font-src 'self' data:; connect-src 'none'; object-src 'none'; base-uri 'none'; frame-src 'none'",
            },
            injectTo: 'head',
          }],
        };
      },
    },
  ],
  build: {
    outDir: '../wwwroot',
    emptyOutDir: true,
  },
}));
