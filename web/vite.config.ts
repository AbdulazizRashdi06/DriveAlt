/// <reference types="vitest/config" />
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// The built app is served by the ASP.NET API from its wwwroot.
export default defineConfig({
  plugins: [react()],
  build: {
    outDir: '../src/DriveAlt.Api/wwwroot',
    emptyOutDir: true,
    chunkSizeWarningLimit: 1500,
  },
  server: {
    port: 5173,
    proxy: { '/api': { target: 'http://localhost:5257', changeOrigin: false } },
  },
  test: {
    environment: 'jsdom',
  },
})
