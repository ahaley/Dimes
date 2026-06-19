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
      '/api': { target: API, changeOrigin: true },
      '/openapi': { target: API, changeOrigin: true },
    },
  },
})
