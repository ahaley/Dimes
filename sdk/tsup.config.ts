import { defineConfig } from 'tsup'

export default defineConfig({
  entry: { index: 'src/index.ts' },
  // ESM + CJS for npm consumers; IIFE (global `Dimes`) for direct <script> embedding in a host app.
  format: ['esm', 'cjs', 'iife'],
  globalName: 'Dimes',
  dts: true,
  clean: true,
  minify: true,
  target: 'es2019',
})
