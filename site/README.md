# PowerX homepage

Static, self-contained marketing page for GitHub Pages. No build step, no dependencies —
`index.html` carries its own CSS; everything else is in `assets/`.

## Assets

| File | Purpose |
|---|---|
| `assets/mark.png` / `mark-180.png` | Logo mark (512 / 180 px) |
| `assets/wordmark.png` | Horizontal logo + wordmark |
| `assets/favicon.ico` / `favicon.png` | Favicons |
| `assets/shot-*.png` | App screenshots — regenerate from a real run when the UI changes |

## Deploy (GitHub Pages)

The workflow at [`.github/workflows/pages.yml`](../.github/workflows/pages.yml) publishes this
folder on every push to `main` that touches `site/`. To turn it on:

1. Repo **Settings → Pages → Build and deployment → Source: GitHub Actions**.
2. Push to `main`. The site goes live at `https://nowalski.github.io/Power-X/`.

To preview locally, just open `index.html` in a browser, or:

```
cd site && python -m http.server 8000
```

## Honesty rules

This page must never claim a release that hasn't happened, link to a download that doesn't
exist, or overstate what works. Keep the "Milestone 1 — not yet released" badge until there
is a real tagged release.
