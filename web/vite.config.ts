import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'

// The API dev host. Run it with: dotnet run --project src/Dimes.Api --urls http://localhost:5080
const API = process.env.DIMES_API ?? 'http://localhost:5080'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: {
    proxy: {
      // changeOrigin:false preserves the original Host (localhost:5173) so the API builds the OIDC
      // redirect_uri on this origin — keeping the whole Keycloak flow (and the BFF session cookie)
      // same-origin on the dev server instead of bouncing to the API port.
      '/api': { target: API, changeOrigin: false },
      '/openapi': { target: API, changeOrigin: false },
      // SignalR realtime hub — needs WebSocket proxying.
      '/hubs': { target: API, changeOrigin: false, ws: true },
      // OIDC callback (Auth:Oidc:CallbackPath). Keycloak redirects the browser here after login;
      // proxying it to the API lets the cookie be set on the dev-server origin.
      '/signin-oidc': { target: API, changeOrigin: false },
    },
  },
})
