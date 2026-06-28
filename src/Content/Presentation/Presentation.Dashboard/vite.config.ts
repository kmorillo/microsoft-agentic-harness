import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'
import path from 'path'

export default defineConfig({
  plugins: [react(), tailwindcss()],
  resolve: {
    alias: { '@': path.resolve(__dirname, './src') },
  },
  server: {
    port: 5174,
    proxy: {
      '/api': { target: 'http://localhost:52000', changeOrigin: true },
      '/hubs': { target: 'http://localhost:52000', ws: true, changeOrigin: true },
      // AG-UI streaming endpoint. The agent panel POSTs to /ag-ui/run and reads an SSE
      // response; the proxy must not buffer it (the backend sets X-Accel-Buffering: no
      // and flushes each frame), so streamed tool-call and text events arrive live.
      '/ag-ui': { target: 'http://localhost:52000', changeOrigin: true },
    },
  },
  test: {
    globals: true,
    environment: 'jsdom',
    setupFiles: './src/test/setup.ts',
    css: true,
    include: ['src/**/*.{test,spec}.{ts,tsx}'],
  },
})
