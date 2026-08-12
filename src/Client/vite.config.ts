import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

const clientPort = Number(process.env.VITE_DEV_PORT ?? 5173)
const apiTarget = process.env.VITE_API_TARGET ?? 'http://localhost:5114'

export default defineConfig({
  plugins: [react()],
  server: {
    port: clientPort,
    proxy: {
      '/api': {
        target: apiTarget,
        changeOrigin: true,
      },
    },
  },
})
