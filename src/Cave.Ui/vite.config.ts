import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'
import { viteSingleFile } from 'vite-plugin-singlefile'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react(), viteSingleFile()],
  build: {
    outDir: '../Cave.Host/wwwroot',
    emptyOutDir: true,
  },
  server: {
    proxy: {
      '/api': 'http://127.0.0.1:5098',
      '/events': 'http://127.0.0.1:5098',
      '/health': 'http://127.0.0.1:5098',
    },
  },
  test: {
    environment: 'jsdom',
  },
})
