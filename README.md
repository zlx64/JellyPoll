# JellyPoll

A [Jellyfin](https://jellyfin.org) plugin for group polls: everyone suggests titles from your library, ranks them in the order they want to watch them, and a **🥇gold / 🥈silver / 🥉bronze** podium is crowned live — then you watch the winner together.

[![Jellyfin: 12.1+](https://img.shields.io/badge/Jellyfin-12.1%2B-000B25?style=flat-square)](https://jellyfin.org)
[![Release](https://img.shields.io/github/v/release/zlx64/JellyPoll?style=flat-square&label=Release)](https://github.com/zlx64/JellyPoll/releases/latest)
[![CI](https://img.shields.io/github/actions/workflow/status/zlx64/JellyPoll/build.yml?style=flat-square&label=CI)](https://github.com/zlx64/JellyPoll/actions/workflows/build.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue?style=flat-square)](LICENSE)

**JellyPoll** turns "what should we watch tonight?" into a quick, fair vote. Any user creates a poll, everyone suggests movies, episodes or whole series straight from the Jellyfin library and drags them into their personal watch order. Standings are computed with [Borda count](https://en.wikipedia.org/wiki/Borda_count) and update live for everyone — no more arguing in the chat.

The plugin ships its own self-contained, theme-matched web app served by the plugin itself, adds a **Jelly Polls** entry to the web UI for every user, and stores all poll data in its own SQLite database — your Jellyfin database is never touched.

## Features

- **Polls for everyone** — any user can create a poll; the poll's creator or an admin can close, reopen, or delete it; admins can bulk-delete all closed polls.
- **Suggestions from the library** — search and suggest movies, TV episodes, or entire series (per-poll toggles). Suggesting a collection (BoxSet) expands it into one suggestion per movie. Several users can suggest at the same time.
- **Server-enforced permissions** — users can only suggest items their Jellyfin access allows; parental controls and library restrictions apply as usual.
- **Ranked ballots** — drag suggestions into your watch order; position 1 is your top pick. Partial ballots are fine, and your ranking saves automatically.
- **Live standings** — Borda-count standings update for everyone within seconds, with a gold / silver / bronze podium and per-title links into Jellyfin's detail pages.
- **"Jelly Polls" in the web UI** — a menu entry for *all* users, injected into the served web client without touching any files on disk (details below).
- **Self-contained UI** — a Svelte web app embedded in the plugin, themed after Jellyfin (dark & light), with bundled icons and no CDN or third-party requests. Works in any modern browser, mobile included.
- **Zero footprint** — poll data lives in a separate SQLite database under the plugin's config folder. A daily maintenance task removes suggestions whose media was deleted from the library.

## Requirements

- Jellyfin server **12.1 or newer** (the plugin targets Jellyfin's `12.1` ABI and .NET 10)
- Any modern browser for the poll UI (desktop or mobile)

## Installation

1. In Jellyfin, open **Dashboard → Plugins → Repositories → +** and add:

   ```
   https://raw.githubusercontent.com/zlx64/JellyPoll/main/manifest.json
   ```

2. Open the **Catalog**, find **JellyPoll**, install it, and **restart Jellyfin**.
3. Hard-refresh the web client (Ctrl+Shift+R). **Jelly Polls** now appears in the user menu (top-right avatar) — for every user of the server.

The plugin can always be reached directly at `http://<your-server>/JellyPoll/Web/`, even without the menu entry.

<details>
<summary>Manual installation (without a repository)</summary>

Download `jellypoll_<version>.zip` from the [releases page](https://github.com/zlx64/JellyPoll/releases/latest), extract it into a subfolder of your server's `plugins` directory (e.g. `<data>/plugins/Jellyfin.Plugin.JellyPoll/`), and restart Jellyfin.

</details>

## How the menu entry works

JellyPoll has two ways to put **Jelly Polls** in the web UI:

1. **Automatic (default).** While the plugin is enabled, it injects a small script into the web client's `index.html` *as it is served* — a response transform, no files on disk are modified, and it survives web client updates. The script adds **Jelly Polls** to the user menu on Jellyfin 12 (right below *Profile*) and to the sidebar drawer on legacy layouts, for all users. This can be disabled by setting `ShowMainNavEntry` to `false` in the plugin's configuration (`JellyPoll.xml`).

2. **Optional `menuLinks` entry.** From **Dashboard → Plugins → Jelly Polls** an admin can click *"Add 'Jelly Polls' link to main menu (all users)"*, which writes a classic `menuLinks` entry into the web client's `config.json`. If the web client folder is read-only (typical in Docker bind mounts) the plugin degrades gracefully and tells you to add the entry by hand:

   ```json
   "menuLinks": [
     { "name": "Jelly Polls", "url": "/JellyPoll/Web/", "icon": "how_to_vote" }
   ]
   ```

Reverse proxies and non-root base URLs are supported: the injected script resolves the correct base path from the request.

## Usage

1. **Create a poll** from the polls list (any user).
2. Everyone opens the poll and **suggests titles** — search the library, or browse collections; multiple users can suggest at once.
3. Each user **ranks the suggestions** into their watch order — the ranking saves as you go.
4. Watch the **live podium**; when everyone has voted, the creator (or an admin) **closes the poll** and the final standings are locked in. Gold is "watch next" — polls can also be reopened or deleted.

Users stay signed in automatically if they came from the Jellyfin web client; otherwise a small login form (their normal Jellyfin credentials) signs them into the poll app.

## Configuration

**Dashboard → Plugins → Jelly Polls**:

| Setting | Default | Meaning |
|---|---|---|
| Allow TV episodes in new polls by default | on | default for newly created polls |
| Allow TV series in new polls by default | on | default for newly created polls |
| Max suggestions per user per poll | 0 | 0 = unlimited |
| Show live standings while polls are open | on | if off, results are revealed only after the poll closes (enforced server-side) |

The main-menu entry toggle (`ShowMainNavEntry`, on by default) is available by editing the plugin's `JellyPoll.xml` configuration file.

## How voting works

Standard **Borda count**: with *N* suggestions, your #1 pick earns *N*−1 points, #2 earns *N*−2, and so on; unranked suggestions earn nothing. Standings are recomputed from the ballots and are fully deterministic:

1. more points,
2. more first-place ranks,
3. head-to-head preference among the tied titles,
4. the earlier suggestion wins.

Suggestions whose media was removed from the library sink to the bottom (and are cleaned up daily). When *live standings* are disabled, results are hidden from everyone — including the API — until the poll closes.

## Building from source

Prerequisites: .NET 10 SDK, Node.js 22, Python 3 (packaging only), Docker (end-to-end tests only).

```bash
cd web && npm ci && npm run build   # 1. build the web app (it is embedded into the plugin)
cd ..
dotnet build JellyPoll.slnx -c Release
dotnet test JellyPoll.slnx -c Release
```

To produce a distributable zip locally:

```bash
dotnet publish src/Jellyfin.Plugin.JellyPoll -c Release -o publish
python scripts/package.py --version 1.0.0.0 --tag v1.0.0 --repo zlx64/JellyPoll
```

### Development

For web-app development with hot reload, run `npm run dev` inside `web/` — the Vite dev server proxies plugin and Jellyfin API calls to `http://localhost:8096`.

### Tests

- **Unit tests** (`tests/Jellyfin.Plugin.JellyPoll.Tests`) — 66 xUnit tests covering the Borda calculator, poll service rules, concurrency, and cleanup.
- **End-to-end tests** (`tests/e2e`) — a Playwright suite that boots a real Jellyfin 12.1 server in Docker, installs the plugin, and drives the UI end to end (login → poll → suggestions → ranked ballot → standings). Point the media mount in `tests/e2e/docker-compose.yml` at a folder with a few media files, then run `node e2e.mjs`.

### CI & releases

Every push and pull request runs the build + unit tests (`.github/workflows/build.yml`). Pushing a tag `vX.Y.Z` cuts a release (`.github/workflows/release.yml`): the plugin is built and tested, packaged, attached to a GitHub release, and the plugin catalog (`manifest.json`) is updated automatically. Users get the new version as a normal plugin update.

## REST API

<details>
<summary>All endpoints live under `/JellyPoll` and require a standard Jellyfin `Authorization` header.</summary>

| Method | Route | Purpose |
|---|---|---|
| `GET` | `/JellyPoll/Polls` | list polls |
| `POST` | `/JellyPoll/Polls` | create a poll |
| `GET` | `/JellyPoll/Polls/{id}` | poll detail (suggestions, your ballot, standings) |
| `POST` | `/JellyPoll/Polls/{id}/Suggestions` | suggest an item (collections expand into their movies) |
| `GET` | `/JellyPoll/Collections` | browse collections the user can access |
| `DELETE` | `/JellyPoll/Polls/{id}/Suggestions/{sid}` | remove a suggestion |
| `PUT` | `/JellyPoll/Polls/{id}/Ballot` | save your ranked ballot |
| `POST` | `/JellyPoll/Polls/{id}/Close` | close the poll (creator/admin) |
| `POST` | `/JellyPoll/Polls/{id}/Reopen` | reopen the poll (creator/admin) |
| `DELETE` | `/JellyPoll/Polls/{id}` | delete the poll (creator/admin) |
| `DELETE` | `/JellyPoll/Polls/Closed` | delete all closed polls (admin) |
| `GET` | `/JellyPoll/Polls/{id}/Results` | final results |
| `GET` | `/JellyPoll/Polls/{id}/State?v=` | cheap change polling for live updates |
</details>

## FAQ

**The "Jelly Polls" entry doesn't show up.**
On Jellyfin 12 it lives in the user menu behind the top-right avatar (below *Profile*); on legacy layouts it's in the sidebar drawer. Hard-refresh the browser (Ctrl+Shift+R) after installing. You can always open `http://<your-server>/JellyPoll/Web/` directly.

**Does it modify my Jellyfin installation or database?**
No. All poll data is kept in a separate SQLite database (`jellypoll.db` under the plugin's configuration folder). The menu entry is injected into the served page only — nothing on disk is modified.

**Which clients are supported?**
The poll UI is browser-based and mobile-friendly; it follows the server's dark/light theme. TV apps don't get a menu entry, but any device with a browser can use the direct URL. For synchronized group playback, Jellyfin's built-in SyncPlay works alongside JellyPoll — the poll decides *what* to watch, SyncPlay handles watching *together*.

**Can users cheat by hitting the API directly?**
No more than through the UI — every endpoint re-authenticates, re-checks library access, and recomputes standings server-side. Hidden-standings mode is enforced on the server, not in the browser.

**What happens when someone deletes a movie that was suggested?**
Its suggestion sinks to the bottom of the standings, and a daily scheduled task (`JellyPoll: remove suggestions whose media is missing`, 3:00 AM by default) removes it from open polls. The task can also be run manually from **Dashboard → Scheduled Tasks**.

## License

MIT — see [LICENSE](LICENSE). Note that compiled plugin binaries link against Jellyfin's GPLv3 libraries; distributed binaries are therefore effectively GPLv3.
