import { defineConfig } from "vite";

// The dev server proxies the hub to the C# server (dotnet run, port 5199 by default).
const server = process.env.MULE_SERVER ?? "http://127.0.0.1:5199";

export default defineConfig({
  server: {
    port: 5173,
    proxy: { "/hub": { target: server, ws: true } },
  },
  // The server serves the built client (client/dist) with the invite links.
  build: { outDir: "dist", emptyOutDir: true, assetsDir: "build", chunkSizeWarningLimit: 2000 },
});
