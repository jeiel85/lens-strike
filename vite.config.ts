import { defineConfig } from 'vite';

export default defineConfig({
  // GitHub Pages 하위 경로(/lens-strike/)에서도 동작하도록 상대 경로로 빌드한다
  base: './',
  build: {
    target: 'es2022',
    // 사람 인식 wasm(약 14MB)은 번들에 인라인하지 않고 별도 파일로 둔다
    assetsInlineLimit: 4096,
  },
  server: {
    host: true,
    port: 5173,
  },
});
