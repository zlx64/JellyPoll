# JellyPoll

A [Jellyfin](https://jellyfin.org) **12.x** plugin for group polls: suggest titles from your library, rank them in the order you want to watch them, and crown the **gold / silver / bronze** winners — then watch together.

🥇🥈🥉

## Features

- **Any user can create a poll**; the creator or an admin closes it and the podium becomes final.
- **Concurrent suggestions** of movies, TV episodes, and TV series (per-poll toggles) directly from the Jellyfin library — Jellyfin's own permissions and parental controls apply.
- **Ranked ballots**: each user drags the suggestions into their personal watch order (partial ballots allowed).
- **Live standings** computed with [Borda count](https://en.wikipedia.org/wiki/Borda_count) and updated for everyone within seconds.
- **"Movie Polls" button in the Jellyfin web UI menu** (added to `config.json` `menuLinks` automatically on request, with a one-click fallback from the plugin's dashboard page).
- Self-contained, theme-matched web UI served by the plugin itself — no web client modification beyond the optional menu link.

## Requirements

- Jellyfin server **12.1** or newer (plugin targets `12.1.0.0`, .NET 10).
- The built-in Jellyfin **web client** (the poll UI runs in the browser).

## Installation

1. In Jellyfin: **Dashboard → Plugins → Repositories → +** and add:

   ```
   https://raw.githubusercontent.com/zlx64/JellyPoll/main/manifest.json
   ```

2. Open the **Catalogue**, find **JellyPoll**, install it, and **restart Jellyfin**.
3. Optional: open **Dashboard → Plugins → JellyPoll**, set your defaults, and click **"Add Movie Polls button to menu"**.
4. Hard-refresh the web client (Ctrl+Shift+R). You'll find **Movie Polls** in the navigation menu.

### Manual menu button (fallback)

If your `config.json` is read-only (e.g., a Docker bind mount), add this to the web client's `config.json` `menuLinks` array manually — its location depends on your install method (see [Jellyfin web config docs](https://jellyfin.org/docs/general/clients/web-config/)):

```json
"menuLinks": [
  { "name": "Movie Polls", "url": "/JellyPoll/Web/", "icon": "how_to_vote" }
]
```

The poll UI itself is always reachable directly at `http://<your-server>/JellyPoll/Web/` even without a menu entry.

## Usage

1. **Create a poll** from the polls list.
2. Everyone opens the poll room, searches the library, and **suggests titles** (multiple users can suggest at the same time).
3. Each user **ranks the suggestions** — position 1 is their top pick; your ranking saves automatically.
4. Watch the **live podium** update, then the poll creator or an admin **closes the poll** — gold is "Watch next".

## Configuration (Dashboard → Plugins → JellyPoll)

| Setting | Default | Meaning |
|---|---|---|
| Allow TV episodes in new polls | on | default for new polls |
| Allow TV series in new polls | on | default for new polls |
| Max suggestions per user per poll | 0 | 0 = unlimited |
| Show live standings while polls are open | on | if off, results are revealed only after closing |

## How voting works

Standard **Borda count**: with N suggestions, your #1 pick gets N−1 points, #2 gets N−2, …, unranked get 0. Ties are broken by number of first-place ranks, then head-to-head preference, then earliest suggestion. See [docs/03-VOTING.md](docs/03-VOTING.md) for the full spec and [docs/01-DESIGN.md](docs/01-DESIGN.md) for the architecture.

## Building from source

Prerequisites: .NET 10 SDK, Node 22.

```bash
cd web && npm ci && npm run build   # builds the SPA into the plugin's embedded resources
cd ..
dotnet build JellyPoll.slnx -c Release
dotnet test JellyPoll.slnx -c Release
```

To produce a distributable package locally:

```bash
dotnet publish src/Jellyfin.Plugin.JellyPoll -c Release -o publish
python scripts/package.py --version 1.0.0.0 --tag v1.0.0 --repo zlx64/JellyPoll
```

Releases are automated: pushing a tag `v1.2.3` runs the GitHub Actions workflow, which builds the web app, publishes the plugin, attaches the zip to a GitHub Release, and updates `manifest.json`.

## Development

Detailed design documents live in [`docs/`](docs/) (01-DESIGN → 06-DEVELOPMENT, numbered in development order), and progress is tracked in [`PROGRESS.md`](PROGRESS.md).

For plugin development with hot reload of the SPA:

```bash
cd web && npm run dev   # Vite dev server proxying /JellyPoll, /Items, /Users, /System to localhost:8096
```

## FAQ

**The "Movie Polls" button doesn't appear.**
The menu link is written to the web client's `config.json`; depending on layout it appears in the top bar (Modern layout) or the drawer (Legacy). Hard-refresh the browser. If your install serves `config.json` from a read-only mount, add the entry manually (see above).

**Can I open the polls without the menu button?**
Yes: navigate to `/JellyPoll/Web/` on your Jellyfin server.

**Does it modify my Jellyfin database?**
No — all poll data lives in a separate SQLite file under the plugin's configuration folder.

**Which clients work?**
The poll UI is browser-based (any modern browser, mobile-friendly). Playback of the winner uses Jellyfin's own apps — for synchronized group playback, Jellyfin's built-in SyncPlay works alongside JellyPoll.

## License

MIT — see [LICENSE](LICENSE). Note that compiled plugin binaries link against GPLv3 Jellyfin libraries; distributed binaries are therefore effectively GPLv3.
