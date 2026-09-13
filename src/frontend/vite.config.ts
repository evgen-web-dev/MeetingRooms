import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'

// Tailwind 4 is a Vite plugin rather than a PostCSS step, so there is no
// tailwind.config.js and no postcss.config.js: source detection is automatic and the
// only other half of the wiring is `@import "tailwindcss";` in src/index.css.
export default defineConfig({
  plugins: [react(), tailwindcss()],

  build: {
    // The API project is the only thing deployed. Building straight into its wwwroot is
    // what lets one App Service serve both the SPA and the API - no CORS, and no
    // cross-origin SignalR negotiate.
    outDir: '../backend/MeetingRooms.Api/wwwroot',

    // Required, not optional: outDir sits outside the Vite root, and Vite refuses to
    // clear such a directory unless told to explicitly. Safe because nothing in wwwroot
    // is tracked - it is build output in its entirety, and static assets that need to
    // survive go in public/ instead.
    emptyOutDir: true,
  },

  server: {
    // Bind 0.0.0.0 rather than localhost, so the dev container's forwarded port reaches
    // a browser on the host.
    host: true,
    port: 5173,
    strictPort: true,

    // Phase 2's single page works from a plain `npm run build`; this is here for phase 7,
    // which cannot work without HMR. Same-origin in production, proxied in development,
    // so application code never needs a base URL.
    proxy: {
      '/health': 'http://localhost:5000',
      '/api': 'http://localhost:5000',
      '/hubs': { target: 'http://localhost:5000', ws: true },
    },
  },
})
