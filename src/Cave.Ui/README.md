# CAVE UI

This package is the single React/TypeScript visualization surface for CAVE. It consumes the normalized snapshot API from `Cave.Host`; it does not query CodeGraph or Git directly.

```powershell
npm ci
npm run dev
npm run build
npm run test
npm run lint
```

The production build is emitted into `../Cave.Host/wwwroot` so the ASP.NET Core host can serve the same surface used during local development.
