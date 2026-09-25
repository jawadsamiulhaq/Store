import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react(), tailwindcss()],

  // Proxying /api means the browser sees one origin, so the refresh cookie (SameSite=Strict)
  // behaves exactly as it will in production behind a single nginx.
  server: {
    port: 5173,
    proxy: {
      '/api': { target: 'http://localhost:5080', changeOrigin: true },
      '/uploads': { target: 'http://localhost:5080', changeOrigin: true },
    },
  },

  // `vite preview` serves the real production build and does NOT inherit `server.proxy`, so it
  // needs its own. Without this, performance testing against the built output would hit a dead
  // API and measure a broken page.
  preview: {
    port: 4173,
    proxy: {
      '/api': { target: 'http://localhost:5080', changeOrigin: true },
      '/uploads': { target: 'http://localhost:5080', changeOrigin: true },
    },
  },

  build: {
    target: 'es2022',

    // Source maps ship for production debugging but are not referenced by the bundle, so they
    // cost nothing on the wire unless a developer opens them.
    sourcemap: true,

    // Warn earlier than Vite's 500 kB default. The legacy store shipped a single 1 MB chunk;
    // a low ceiling here makes a regression toward that loud rather than silent.
    chunkSizeWarningLimit: 250,

    rollupOptions: {
      output: {
        // Manual vendor chunks.
        //
        // Without this, every dependency lands in one vendor bundle and changing any single
        // library invalidates the whole cached file. Splitting along libraries that version
        // independently means a React patch does not force shoppers to re-download the router
        // and the query client as well.
        //
        // Written as a function rather than the object form because Rollup in Vite 8 types
        // `manualChunks` as a function here. Order matters: `react-router` and
        // `@tanstack/react-query` both contain the substring "react", so they are matched first.
        manualChunks(id) {
          if (!id.includes('node_modules')) {
            return undefined
          }

          if (id.includes('react-router')) return 'vendor-router'
          if (id.includes('@tanstack')) return 'vendor-query'
          if (id.includes('react-dom') || id.includes('/react/') || id.includes('scheduler')) {
            return 'vendor-react'
          }

          return undefined
        },

        // Hashed filenames so /assets/* can be served immutable with a one-year max-age.
        entryFileNames: 'assets/[name]-[hash].js',
        chunkFileNames: 'assets/[name]-[hash].js',
        assetFileNames: 'assets/[name]-[hash][extname]',
      },
    },
  },
})
