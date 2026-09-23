import { defineConfig } from 'vite';
import { svelte } from '@sveltejs/vite-plugin-svelte';

export default defineConfig({
  base: '/JellyPoll/Web/',
  plugins: [svelte()],
  build: {
    target: 'es2022',
    outDir: '../src/Jellyfin.Plugin.JellyPoll/Web',
    emptyOutDir: true
  },
  server: {
    fs: {
      allow: ['..']
    },
    proxy: {
      '/JellyPoll': 'http://localhost:8096',
      '/Items': 'http://localhost:8096',
      '/Users': 'http://localhost:8096',
      '/System': 'http://localhost:8096'
    }
  }
});
