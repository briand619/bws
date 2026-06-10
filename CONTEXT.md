# Code Review — Bubble Word Splash

Review date: 2026-06-10. Scope: `ui/index.html` (game UI, ~1,900 lines), `api/` (ASP.NET Core
suggestions API), and project configuration. Findings are grouped by severity, then by area.

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
