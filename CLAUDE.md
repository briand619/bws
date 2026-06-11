# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What This Is

**Bubble Splash** — a browser-based Spelling Bee-style word puzzle game. Players form words from a hive of 7 letter bubbles (one required center letter). The project has two parts:

- `ui/` — a single self-contained HTML file (`index.html`) with all CSS and JS inline. No build step; served statically (VS Code Live Server or any static file server on port 5500).
- `api/` — ASP.NET Core 10 Web API (`BubbleSplash.Api`) backed by PostgreSQL via EF Core.

## Commands

### API

```powershell
# Run (from api/)
dotnet run

# Build
dotnet build

# EF Core migrations (from api/)
dotnet ef migrations add <MigrationName>
dotnet ef database update
```

API listens on `http://0.0.0.0:5179` (Development profile). The database is auto-migrated on startup in Development.

### UI

No build step. Open `ui/index.html` directly in a browser or serve with Live Server. The admin interface is at `ui/admin/index.html`.

## Architecture

### UI (`ui/index.html`)

Everything is inline in one HTML file — styles, game logic, and the ocean floor canvas animation. Key sections in order:

- **CSS** — theming via CSS custom properties (`--bg-color`, `--bubble-outer-glow`, etc.) on `:root`. The `body` has `isolation: isolate`, which creates a stacking context; all z-index values are relative to this.
- **Background layer** (`#bg-layer`, `z-index: -1`) — contains the gradient, animated rays, rising bubbles, and the ocean floor canvas. Themed via the `THEMES` array (5 aquatic themes); active theme persisted in `localStorage` as `bws_theme`.
- **Ocean floor IIFE** — canvas positioned at the bottom of `#bg-layer`. `generate()` pre-computes rocks, seaweeds (sway via CSS `rotate` transform), corals (pre-computed branch trees to avoid flicker), pebbles, and starfish. `draw(ts)` runs via `requestAnimationFrame`. `window.updateFloorTheme(idx)` swaps the palette.
- **Hive** — 7 `.bubble-wrapper` divs (1 center + 6 outer) positioned absolutely within `.hive-container`. The top outer bubbles extend ~50px above the container's top edge. `.notification-zone` uses `position: relative; z-index: 10; pointer-events: none` so success/error messages render above the bubbles without blocking clicks.
- **Found words** — accordion below the rank bubbles. `#found-words-body` uses `max-height` transition for expand/collapse. 8 words per page, swipe-paginated with dot indicators.
- **Suggest modal** — the game's `keydown` listener guards `if (!suggestModal.classList.contains("hidden")) return` to prevent keystrokes from leaking into the game while the modal is open.
- **Puzzle data** — embedded as a JS object in the script block. `dictionary.json` is fetched at load time for word validation.

### API (`api/`)

Standard ASP.NET Core controller pattern. No service layer beyond `DictionaryService`.

- **`SuggestionsController`** — three admin endpoints (`GET /api/suggestions`, `POST /api/suggestions/{id}/approve`, `POST /api/suggestions/{id}/reject`) are protected by `[TypeFilter(typeof(AdminKeyFilter))]`.
- **`AdminKeyFilter`** (`Filters/`) — `IActionFilter` that reads `Admin:ApiKey` from config and validates the `X-Admin-Key` request header. Returns 401 if wrong/missing. If no key is configured, it allows all requests through.
- **`DictionaryService`** (`Services/`, registered as singleton) — thread-safe file writer with `SemaphoreSlim(1,1)`. `AddWordAsync` binary-searches for the alphabetical insertion point; `RemoveWordAsync` uses `RemoveAll`. The file path is resolved via `Path.GetFullPath(configured, env.ContentRootPath)`.
- **`BubbleSplashDbContext`** — single `WordSuggestions` table (`word_suggestions`). `Status` is stored as a string enum. Indexes on `word` and `status`.

### Key Configuration (`appsettings.Development.json`)

```json
"Admin": { "ApiKey": "bubble-admin-2026" }
"Dictionary": { "FilePath": "../ui/dictionary.json" }
"Cors": { "AllowedOrigins": ["http://localhost:5500", ...] }
```

`dictionary.json` is the source of truth for valid words — shared between the live UI (fetched at game load) and the API (written by `DictionaryService` on approve/reject).

### Admin UI (`ui/admin/index.html`)

Standalone HTML page. Stores the admin key in `sessionStorage` (`bws_admin_key`). On load it silently re-validates the stored key with a `GET /api/suggestions?status=pending` call (using `X-Admin-Key` header); any 401 triggers sign-out. Defaults to the "Pending" filter tab.


Keep track of tasks prompted and completed in TASKS.md. Be sure to write to this file before compaction is required.
