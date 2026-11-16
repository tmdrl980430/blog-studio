import { defineConfig } from "vitest/config";
import react from "@vitejs/plugin-react";
import { fileURLToPath } from "node:url";

const shared = fileURLToPath(new URL("../../shared", import.meta.url));

// GitHub Pages 주소: https://<user>.github.io/blog-studio/
export default defineConfig({
  base: process.env.PAGES_BASE ?? "/",
  plugins: [react()],
  resolve: { alias: { "@shared": shared } },
  server: { fs: { allow: [".", shared] } },
  test: { environment: "jsdom" },
});
