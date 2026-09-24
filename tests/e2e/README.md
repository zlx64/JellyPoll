# JellyPoll E2E

Self-contained end-to-end tests for the JellyPoll plugin. One command builds the
plugin, starts a throwaway Jellyfin server in Docker, performs the first-time
setup (admin account, movies library, scan), installs the plugin, and runs the
Playwright test suite against it.

## Prerequisites

- **Docker** (with the Compose plugin) and a running Docker daemon.
- **Node.js 18+** (the scripts use the built-in `fetch`).
- **.NET 10 SDK** (to build the plugin).
- A local folder containing **at least 3 movies** (any movies work — the tests
  discover them from the library automatically).

## Setup (one time)

```bash
cd tests/e2e
npm install
npx playwright install chromium

cp .env.example .env    # then edit .env
```

Fill in `.env`:

| Variable                | Meaning                                                        |
| ----------------------- | -------------------------------------------------------------- |
| `MOVIES_FOLDER`         | Absolute path to your movies folder (mounted read-only).       |
| `JELLYFIN_ADMIN_PASSWORD` | Password for the admin account the setup creates.             |
| `JELLYFIN_ADMIN_USER`   | Admin username (default `admin`).                              |
| `JELLYFIN_HOST_PORT`    | Host port for the server (default `8096`).                     |
| `JELLYFIN_IMAGE`        | Jellyfin image (default `jellyfin/jellyfin:12.1`).             |
| `SERVER_NAME`           | Server name shown in the Jellyfin UI.                          |
| `PLUGIN_VERSION`        | Must match `src/Jellyfin.Plugin.JellyPoll/meta.json`.          |

`.env` is gitignored; `.env.example` is the committed template.

## Run

```bash
npm run all      # build plugin -> start Jellyfin -> configure -> install plugin -> run tests
```

Other commands:

```bash
npm run fresh    # same, but wipes the stack + volume first (guaranteed clean state)
npm run setup    # everything except the test run
npm run e2e      # just re-run the tests against the already-running stack
npm run down     # stop the stack (volume is kept)
```

`run-e2e.mjs` is idempotent: on re-runs it reuses the existing volume and skips
the wizard/library steps that are already done. Use `fresh` when you want a
guaranteed clean slate.

## What the runner does

1. `dotnet publish` the plugin (version from `meta.json`).
2. `docker compose up -d` (image, port and movies mount come from `.env`).
3. Wait for `/health`.
4. If the first-time wizard is incomplete, complete it via the API
   (`/Startup/User`, `/Startup/Configuration`, `/Startup/Complete`) using the
   credentials from `.env`.
5. Create a "Movies" library on `/media/movies` if missing and trigger a scan.
6. Copy the built plugin into `/config/plugins/Jelly Poll_<version>/`
   (stripping the `runtimes/` native libs, which would break plugin loading)
   and restart the server.
7. Run `e2e.mjs`.

## The tests

`e2e.mjs` discovers 3 distinct movies from the library and drives the real
Jellyfin web client + the plugin SPA:

- **A** — login, injected nav entries, SPA loads.
- **B** — create a collection via the JF UI and add a second movie to it.
- **C** — create a poll, suggest a movie via search, suggest the collection,
  verify board / auto-ranking / standings.
- **G** — "Share my ranking" toggle and the standings vote-breakdown tooltip.
- **E** — i18n (Ukrainian, English fallback, nav under a Ukrainian JF UI).
- **F** — admin display-language override via the dashboard config page.
- **D** — cleanup of the poll and collection it created.

Screenshots are written to `tests/e2e/shot-*.png` (gitignored) for debugging.

## Troubleshooting

- **`MOVIES_FOLDER does not exist`** — set an absolute path in `.env`. On
  Windows (Docker Desktop) use the drive path, e.g. `F:\Movies`.
- **Login failed against the existing volume** — the saved credentials differ
  from `.env`; run `npm run fresh` to wipe and re-create them.
- **Fewer than 3 movies indexed** — point `MOVIES_FOLDER` at a folder with at
  least 3 video files.
- **Plugin not loading** — check `docker logs jellypoll-e2e` for
  `PluginManager` errors.
