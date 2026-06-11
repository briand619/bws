# Code Review — Bubble Word Splash

Initial review: 2026-06-10. Scope: `ui/index.html` (game UI), `api/` (ASP.NET Core
suggestions API), and project configuration. Findings are grouped by severity, then by area.

**Re-reviewed 2026-06-11** after the latest UI commit ("Latest UI.", `5e90a00`) — see the
[Re-review section](#re-review-of-uiindexhtml--2026-06-11) at the bottom for the status of
each original finding and new findings in the rewritten file. Line numbers in the original
sections below refer to the pre-rewrite file and are stale.

---

## Bugs

### UI (`ui/index.html`)

1. **Score is never displayed.** `scoreDisplay` and `rankDisplay` are looked up by IDs
   `current-score` and `current-rank` (`index.html:1105-1106`), but no elements with those IDs
   exist in the markup — the top bar only contains the static title "Bubble Splash". The
   `if (scoreDisplay)` guard in `processValidWord()` (`index.html:1630`) silently hides the
   problem, so the player's running score is only visible inside the next-milestone rank bubble.
   Either add the element or remove the dead lookups.

2. **Possible infinite loop / page hang on puzzle generation.** `initGame()` runs
   `while (true) { if (generatePuzzle(wordList)) break; }` (`index.html:1167-1169`).
   `generatePuzzle()` returns `undefined` (falsy) when no pangrams remain (`index.html:1197-1202`),
   which makes the loop spin forever, synchronously, freezing the page. With the current
   dictionary this won't trigger, but any dictionary where every pangram has more than 85 valid
   answers (each such pangram gets pushed to `excludedPangrams` and retried) will eventually
   exhaust the list and hang the browser. The "no pangrams" branch should `return false` is not
   enough — the caller must distinguish "retry" from "give up" (e.g., relax the 85-answer cap or
   show the error and stop).

3. **Typing in the "Suggest a Word" modal also types into the game.** The game keyboard handler
   (`index.html:1759-1787`) bails out when the Found Words modal is open but never checks the
   suggest modal. Typing a word into the suggestion input simultaneously feeds letters into the
   game's input display, and pressing Enter both submits the form and calls `submitWord()`.
   Add `if (!suggestModal.classList.contains('hidden')) return;` to the handler.

4. **Hardcoded localhost API URL.** `submitWordSuggestion()` posts to
   `http://localhost:5179/api/suggestions` (`index.html:1902`). This breaks everywhere except a
   developer machine: on the published site (CNAME present, presumably HTTPS) it is blocked as
   mixed content, and inside the Capacitor iOS/Android apps `localhost` is the device itself.
   The URL needs to be environment-aware (relative path behind a reverse proxy, or a config
   constant swapped at build time).

5. **Invalid CSS declaration.** `.rank-bubbles-container` has `gap: 5 px;` (`index.html:778`) —
   the space makes the declaration invalid and it is dropped. Also `justify-content: left`
   (`index.html:775`) is a non-standard value; use `flex-start`.

### API (`api/`)

6. **Duplicate check has a race (TOCTOU).** `SubmitSuggestion` does a read-then-insert
   (`SuggestionsController.cs:36-57`) and the `word` index is not unique
   (`BubbleSplashDbContext.cs:58-59`), so two concurrent submissions of the same word both pass
   the check and both insert. Fix with a partial unique index
   (`word` WHERE `status != 'Rejected'`) and catch `DbUpdateException` to return 409.

7. **DB default for `created_at_utc` is wrong (currently dead, but a trap).**
   The column is `timestamp with time zone` but the default is `now() at time zone 'utc'`
   (`BubbleSplashDbContext.cs:48`, migration line 24), which yields a `timestamp without time
   zone` that Postgres reinterprets in the session timezone when cast back — wrong value on any
   server not running in UTC. It's currently harmless only because the C# property initializer
   (`WordSuggestion.cs:28`) always supplies a value, making the DB default dead code. Use
   plain `now()` (which is already a timestamptz) or drop one of the two defaults.

8. **Production configuration is non-functional as committed.** `appsettings.json` has an empty
   connection string and no `Cors:AllowedOrigins` section, so outside Development the API would
   fail to reach the DB and CORS would fall back to the localhost defaults in `Program.cs:25`,
   blocking the real frontend. Fine if env vars are supplied at deploy time, but worth making
   explicit (fail fast on a missing connection string).

---

## Inefficiencies & Redundancies

### UI

9. **Pangram list recomputed on every generation retry.** Each `generatePuzzle()` call re-filters
   all ~38k words for 7-unique-letter words and re-scans `excludedPangrams` with
   `Array.includes` (O(n·m)) (`index.html:1189-1195`). Compute the pangram list once after the
   dictionary loads and use a `Set` for exclusions; retries then only pay for the
   `validAnswers` filter.

10. **`validAnswers` filter does `uniqueLetters.includes(char)` per character**
    (`index.html:1222-1228`) — an array scan inside a loop over every word in the dictionary.
    A `Set` of the 7 letters (or a regex like `^[letters]+$`) is the natural fit.

11. **Two document-level `keydown` listeners** (`index.html:1759` and `1836`) — the Escape
    handling for the suggest modal could live in the main handler, which also fixes bug #3 in
    one place.

12. **Mixed event-binding styles.** Some buttons use inline `onclick="..."` attributes
    (`changePage`, `deleteLetter`, `shuffleLetters`, `submitWord`) while others use
    `addEventListener`. Pick one (listeners) for consistency and to avoid relying on globals.

13. **Font Awesome (full CDN bundle) is loaded for two icons** (`index.html:8`) — a lightbulb
    and a spin arrow. Inline SVGs would remove the network dependency, which matters in the
    Capacitor apps where the CDN may be unreachable offline.

### API

14. **Entity→response mapping is duplicated** in `SubmitSuggestion` and `GetSuggestion`
    (`SuggestionsController.cs:61-68` and `88-95`). A small `ToResponse(WordSuggestion, string
    message)` helper (or an extension method) removes the duplication.

15. **Duplicate-word lookup materializes the entity when only existence matters.**
    `FirstOrDefaultAsync` (`SuggestionsController.cs:36-37`) could be `AnyAsync` — the loaded
    entity is never used.

---

## Opportunities for Improvement

### Gameplay / UX

16. **No state persistence.** Refreshing the page loses the puzzle, score, and found words.
    Serializing `{pangram, foundWords, score}` to `localStorage` would make this feel like a
    finished daily-puzzle game, and would also enable a "same puzzle for everyone today" mode
    (seed by date) instead of a random puzzle per load.

17. **No minimum-quality check on puzzles.** Generation rejects puzzles with >85 answers but
    accepts ones with very few (even a single pangram-only answer). A lower bound (e.g., ≥15
    answers) would avoid dud puzzles.

18. **Unlimited input length.** `handleBubbleClick` appends without bound; a long mash overflows
    the input display. Cap it (e.g., at 20 chars) with the shake feedback.

19. **Accessibility.** `user-scalable=no, maximum-scale=1.0` in the viewport meta blocks zoom;
    the modals lack `role="dialog"`/`aria-modal` and focus trapping; rank/notification updates
    aren't announced (`aria-live`). All cheap to add.

20. **`shuffleLetters` hardcodes 6 outer bubbles** (`indices = [0,1,2,3,4,5]`,
    `index.html:1524`). Derive the count from `currentPuzzle.outerLetters.length` so the
    function survives any future change to puzzle shape.

21. **Suggest-modal Escape/overlay close doesn't reset the submitting state.** If the user
    closes the modal while a request is in flight, the toast still fires later and the button
    state is restored by `finally` — fine — but the form is reset while a submission may still
    succeed. Consider disabling close during submit or aborting the fetch.

### API / Backend

22. **No rate limiting or abuse protection** on a public, unauthenticated POST endpoint. The
    model even stores `SubmitterIp` for "spam/abuse tracking" but nothing enforces a limit.
    ASP.NET Core's built-in rate limiter middleware (fixed window per IP) is a few lines.

23. **`RemoteIpAddress` will be the proxy's address in any real deployment.** Add
    `UseForwardedHeaders` (configured with known proxies) if `SubmitterIp` is meant to be useful.

24. **The review workflow is unreachable.** `SuggestionStatus.Approved/Rejected` and
    `ReviewedAtUtc` exist, and there's an index on `status` "for admin filtering", but no
    endpoint lists or updates suggestions — moderation requires raw SQL. Either add a minimal
    (authenticated) admin endpoint or note this as deliberate.

25. **Word validation between client and server is looser than the dictionary's rules.** The
    API accepts any 4–45-letter word; it never checks the word against the shipped
    `dictionary.json`, so users can "suggest" words that already exist (only the client checks
    this). Cheap server-side check if the API is given the dictionary.

26. **`UseHttpsRedirection` is absent** from the pipeline; harmless behind a TLS-terminating
    proxy, but worth adding for direct exposure.

### Project / Repo

27. **`ui/index.html` is a 1,900-line single file** (CSS + HTML + JS). Splitting into
    `styles.css` / `game.js` would help diffs, caching, and future contributors; Capacitor's
    `webDir` already points at `ui/` so no build pipeline is required.

28. **`README.md` is one line.** A short section on running the UI (any static server), running
    the API (`dotnet run`, requires Postgres), and syncing Capacitor would lower the barrier
    for contributors.

29. **Dev DB credentials (`postgres/postgres`) are committed** in
    `appsettings.Development.json`. Standard for local dev, but user-secrets
    (`dotnet user-secrets`) keeps the repo clean of even placeholder credentials.

---

## What's Done Well

- Clean separation of DTOs from the EF entity, with validation attributes on the request DTO.
- Snake_case column mapping, sensible max lengths, and indexes thought through in the DbContext.
- The submitter's IP is stored but deliberately never returned in responses.
- The UI's particle/animation system is self-cleaning (elements removed after their animations)
  and uses the double-`requestAnimationFrame` idiom correctly.
- Client-side validation in the suggest form mirrors the server's rules (4+ letters,
  letters-only, duplicate check against the loaded dictionary).

---

# Re-review of ui/index.html — 2026-06-11

The UI was substantially rewritten (1,941 → 3,177 lines). New features: a 5-theme background
system (animated light rays, rising bubbles, persisted to `localStorage`), a canvas-drawn
ocean floor (rocks, seaweed, coral, starfish), a "Words Found" bottom-sheet popover with
per-word dictionary definitions (via `dictionaryapi.dev`), page-dot + swipe pagination, and a
Blue Whale congratulations modal.

## Status of original findings

| # | Finding | Status |
|---|---------|--------|
| 1 | Score never displayed (`#current-score` / `#current-rank` don't exist) | **Still open** — dead lookups remain at `index.html:1692-1693`; score is still only visible in the next-milestone rank bubble |
| 2 | Infinite loop / hang in puzzle generation (`while(true)` + falsy return) | **Still open** (`index.html:1747-1749`, `1777-1782`) |
| 3 | Typing in suggest modal leaks into the game | **Fixed** — the game keydown handler now bails when the suggest modal is open (`index.html:2504`). But see new finding N1: the same leak now exists for the words popover and whale modal |
| 4 | Hardcoded localhost API URL | **Still open, arguably worse** — now `http://10.0.0.90:5179` (`index.html:2669`), a private LAN IP that fails for anyone outside the developer's network, plus mixed-content blocking on HTTPS |
| 5 | Invalid CSS (`gap: 5 px`, `justify-content: left`) | **Still open** (`index.html:1147,1150`) |
| 9 | Pangram list recomputed per retry | Still open |
| 10 | `uniqueLetters.includes(char)` array scan in answers filter | Still open |
| 11 | Multiple document-level keydown listeners | Still open — now **three** (game, popover Escape, suggest Escape) |
| 12 | Mixed inline `onclick` / `addEventListener` styles | Still open |
| 13 | Font Awesome CDN for a few icons | Still open |
| 16 | No game-state persistence | Partially addressed — the **theme** is persisted, but puzzle/score/found words still reset on refresh |
| 17 | No minimum answer count for puzzles | Still open |
| 18 | Unlimited input length | Still open |
| 19 | Accessibility (no zoom, no dialog roles/focus trap) | Still open; `#bg-layer` does get `aria-hidden` |
| 20 | `shuffleLetters` hardcodes 6 outer bubbles | Still open |

API findings (#6-8, #14-15, #22-26) are unaffected — no API changes in this commit.

## New findings

### Bugs

- **N1 — Keyboard still leaks into the game behind the words popover and whale modal.**
  The keydown guard only checks the suggest modal (`index.html:2504`). With the words popover
  open, typed letters land in the game input and Backspace/Enter still delete/submit invisibly
  behind the sheet; same for the whale modal. The old code blocked input while the (since
  removed) words modal was open, so this is a partial regression. Extend the guard:
  popover `.open`, whale modal not `.hidden`.

- **N2 — XSS sink in the definition renderer.** `buildDefinitionHTML()`
  (`index.html:2482-2499`) interpolates `entry.word`, phonetics, part of speech, definitions,
  and examples from the third-party `dictionaryapi.dev` response directly into `innerHTML`.
  Any HTML in the API payload executes in the page. Low likelihood (HTTPS, reputable-ish API)
  but it is untrusted external data; build the DOM with `textContent` or escape the strings.

- **N3 — Corrupt `localStorage` theme value bricks the whole game.**
  `parseInt(localStorage.getItem("bws_theme") || "0", 10)` (`index.html:2794`) yields `NaN`
  for any non-numeric stored value; the `>= THEMES.length` guard doesn't catch `NaN`, so
  `applyTheme(THEMES[NaN])` throws on `theme.vars`. Because that call is top-level script
  code that runs *before* `initGame()` (`index.html:3174`), the exception stops the script
  and the page is stuck on "Loading Dictionary..." forever. Guard with `Number.isNaN` (or
  `THEMES[idx] ?? THEMES[0]`).

- **N4 — Ocean floor isn't regenerated on resize.** The `resize` handler
  (`index.html:3163`) only resets canvas width; `generate()` is never re-run, so after a
  rotation or window-widening the rocks/seaweed/coral only cover the old width and the new
  area is bare floor.

- **N5 — Leftover debug `console.log(previousWord)`** in the suggest-open handler
  (`index.html:2566`).

### Inefficiencies / redundancies

- **N6 — `changePage` rebuilds an array just to count it**:
  `Array.from(foundWords).length` (`index.html:2284`) instead of `foundWords.size`. Also
  hardcodes `8` items-per-page separately from `renderFoundWords`'s `itemsPerPage = 8` —
  extract a shared constant.

- **N7 — Duplicate `#suggest-word-btn` CSS rule blocks** (`index.html:67-79` and `85-92`)
  — the second overrides the first's `background`; merge them.

- **N8 — Definitions are re-fetched on every click.** A simple `Map` cache of
  word → definition HTML would avoid repeat network round-trips when reopening a word.

- **N9 — Background animation runs unconditionally.** The canvas `requestAnimationFrame`
  loop and the 2.8 s `setInterval` bubble spawner never pause (rAF self-pauses in hidden
  tabs, but the interval keeps queuing work and the loop runs even when the popover/modals
  fully cover the scene). Consider pausing on `visibilitychange`, and honoring
  `prefers-reduced-motion` for the rays/bubbles/floor sway.

### Improvements

- **N10 — Suggest prefill UX.** Opening the suggest modal pre-fills the last submitted word
  (`previousWord`, `index.html:2567`) — a nice touch for "my word was rejected" flows, but it
  also pre-fills after *successful* submissions (where it then trips the "already in the
  dictionary" error) and after too-short accidental submits. Only prefill when the previous
  submission failed with "Not in word list". (Also `.toLowerCase()` there is redundant —
  `previousWord` is already lowercased.)

- **N11 — Word count no longer visible.** The old modal header showed "Words Found (N)";
  the new popover header and trigger button show no count. Re-adding the count to the
  "Words Found" trigger is cheap and useful.

- **N12 — Network-vs-missing distinction in definitions.** The `catch` in `showDefinition`
  (`index.html:2477`) shows "No definition found" for both 404s and network failures
  (offline Capacitor use). Distinguishing the two ("couldn't reach the dictionary service")
  avoids telling users a real word has no definition.

- **N13 — `currentScore` is never reset in `generatePuzzle`.** Harmless today because only
  one puzzle is ever generated per page load, but it's a landmine for any future
  "New Game" button: score, rank, and the whale-modal state would carry over. Reset
  `currentScore = 0` alongside `foundWords`.

## What improved since the last review

- The suggest-modal keyboard leak (original #3) was fixed via the keydown guard.
- The theme choice is persisted to `localStorage` — first step toward state persistence.
- The clunky Prev/Next paginated modal was replaced with a slicker popover with page dots
  and swipe gestures, and word definitions are a genuinely nice addition.
- The new background/ocean-floor code is well-contained (IIFE, pre-computed coral branch
  geometry so the draw loop stays cheap, self-removing DOM bubbles).
