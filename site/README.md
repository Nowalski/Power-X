# PowerX homepage

The static page published to GitHub Pages at https://nowalski.github.io/Power-X/. No build
step and no dependencies: `index.html` carries its own CSS, everything else is in `assets/`.

## Assets

| File | Purpose |
|---|---|
| `assets/mark.png`, `mark-180.png` | Logo mark (512 and 180 px) |
| `assets/wordmark.png` | Logo plus wordmark |
| `assets/favicon.ico`, `favicon.png` | Favicons |
| `assets/shot-*.png` | App screenshots. Regenerate from a real run when the UI changes. |

## Deploy

[`.github/workflows/pages.yml`](../.github/workflows/pages.yml) publishes this folder on every
push to `main` that touches `site/`. It needs **Settings, Pages, Build and deployment, Source:
GitHub Actions** set once on the repo.

To preview locally, open `index.html` in a browser, or run `python -m http.server 8000` from
this folder.

## Honesty rules

The page must not claim a release that has not happened, link to a download that does not
exist, or overstate what works. When there is no current release, say so.
