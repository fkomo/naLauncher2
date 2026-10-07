# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```powershell
dotnet build src/naLauncher2.slnx          # build (net10.0-windows, WPF)
dotnet run --project src/naLauncher2.Wpf   # build & launch the app
dotnet publish src/naLauncher2.Wpf -p:PublishProfile=FolderProfile   # -> %AppData%\Ujeby\naLauncher2
```

There is no test project and no linter config — build warnings are the only static feedback.

`settings.json` is read from next to the executable, so each build output has its own settings. `dotnet run` uses `naLauncher2.Wpf\bin\<Configuration>\net10.0-windows\settings.json`; the Debug one points at a separate dev library (`%AppData%\Ujeby\naLauncher2-dev\library-dev.json`). The real library and settings belong to the published app in `%AppData%\Ujeby\naLauncher2`. The `FolderProfile` publish writes straight into that live install folder.

### Solution layout

Everything that builds lives under `src/`: both projects, `naLauncher2.slnx`, `Directory.Build.props` and `Directory.Packages.props`. MSBuild finds the two props files by searching upward from each project.

- **`naLauncher2.Core`** (class library): the model (`GameInfo`, `Session`, enums), `GameLibrary`, `AppSettings`, `Log`, `JsonDefaults`, `PlayTimeFormat`, the metadata providers in `Api/`, playtime tracking in `Tracking/`, and the helpers in `Tools/`. `Tools/` holds `TimedBlock` (the `using var tb = new TimedBlock(...)` timing/logging idiom used throughout), `GZip` (library backups) and `StringExtensions.NormalizeCustom()` (title matching against IGDB/Steam). Core has no WPF references; keep it that way.
- **`naLauncher2.Wpf`** (WinExe, assembly name `naLauncher2`): `App`, `MainWindow`, controls and dialogs. It imports `naLauncher2.Core`, `.Api`, `.Tools` and `.Tracking` as global usings from its `.csproj`, so its files carry no `using` lines for them. It sets `UseWindowsForms` only for the tray icon (`TrayIcon`, a `NotifyIcon`). The WinForms/`System.Drawing` global usings are removed there, so refer to WinForms types fully qualified.
- Both projects target `net10.0-windows`. Shared properties (target framework, nullable, implicit usings, debug type) live in `Directory.Build.props`.
- **Central package management:** package versions are set only in `Directory.Packages.props` (`<PackageVersion>`). A project's `<PackageReference>` must not carry a `Version`.

## Architecture

No MVVM, no DI, no data binding for game content. Two process-wide singletons, both in Core, hold all state:

- **`AppSettings.Instance`** — `settings.json` next to the executable (`AppContext.BaseDirectory`). Loaded in `App.OnStartup`, saved in `App.OnExit` and by `SettingsDialog`.
- **`GameLibrary.Instance`** — a `ConcurrentDictionary<string, GameInfo>` persisted as JSON at `AppSettings.LibraryPath`.

**The dictionary key *is* the game title.** There is no id field. Consequences that keep biting: renaming a game (`GamePropertiesDialog`) is a remove + re-add of the key; `RefreshSources` must match on the shortcut path as well as the title, or a renamed game gets re-added as a duplicate on the next scan. "Removed"/uninstalled just means `GameInfo.Shortcut == null` — the entry stays in the library with its history.

### Startup flow

`App.OnStartup` (single-instance check → settings → `TwitchDevAuthz`) → `MainWindow.Window_Loaded` → `LoadLibraryAndSettings` (settings dialog if `LibraryPathMissing` → `GameLibrary.Load` → `Backup` → `SessionTracker.Restore` → `RefreshSources`) → measure layout, populate sections → `LauncherSync.Refresh` in the background → `RefreshNewGameDataInBackground` for any games the source scan discovered.

Only one instance runs per install folder (a named mutex keyed by a hash of `AppContext.BaseDirectory`, so the dev build and the published app can run side by side). A second instance signals the first through a named event to show its window, then exits.

### Persistence rules

- Every mutation is followed by an explicit `await GameLibrary.Instance.Save()` from the caller — nothing auto-saves.
- Both library and settings serialize through the shared `JsonDefaults.Options`; use it on any new read/write or round-tripping breaks. It writes enums **by name** (`JsonStringEnumConverter`) and still reads old numeric values. Reordering enum members is therefore safe, but **renaming one breaks loading** of files that store the old name.
- `Backup()` writes a GZip `.bak` named `<library>_<yyyyMMddHHmmss>.bak`, skips when the SHA-256 matches the previous backup, and keeps the 10 newest. DEBUG builds additionally drop an uncompressed `.json` next to each `.bak`.
- Library JSON passes through `GameLibrary.Migrate` (a `JsonNode` rewrite) before deserialization, for both `Load` and `Restore`, so old files and old backups keep loading. A breaking change to `GameInfo`'s shape needs a step there. When `Load` migrates, it writes the original next to the library as `<library>_premigration_<timestamp>.json` and saves right away. Current step: `Played` changed from `List<DateTime>` to `List<Session>`. Migrated sessions have only `Start` (unmeasured).
- `AppSettings.Load` copies each property one by one out of the deserialized instance. A new setting that isn't added to that copy block will silently never load.

### Metadata providers (`Api/`)

`IGameDataProvider<T>` has two implementations with very different mechanics:

- **`IgdbClient`** — real REST API (Apicalypse query strings POSTed to `api.igdb.com/v4`). Auth is `TwitchDevAuthz`, a `DelegatingHandler` that fetches and caches a client-credentials token and injects `Client-ID`/`Authorization`. `App.SettingsChanged()` builds it from the settings into `GameLibrary.TwitchDevAuthz`; without Twitch credentials it is null and constructing the client throws.
- **`SteamClient`** — no API; it scrapes the store HTML with regexes (`WebScraper`), rendered through headless Chromium via PuppeteerSharp. `WebScraper` lazily downloads a Chromium revision on first use (slow, one-time) and reuses one shared browser instance.

Both resolve a title to an id only on an unambiguous exact match after `NormalizeCustom()`; multiple matches are logged and treated as no match. Covers download into `ImageCachePath\IgdbCom` and `ImageCachePath\SteamDbInfo`; with no `ImageCachePath` set, images are skipped entirely.

Merge semantics differ per source and are deliberate: `GameInfo.UpdateFromIgdb` uses `??=` (never overwrites what's already there), while `UpdateFromSteam` overwrites `Summary`/`ImagePath` whenever Steam returns them. Steam therefore wins on description and cover art.

### UI layer

`MainWindow` is chrome-less (`WindowStyle=None`, `AllowsTransparency`, maximized; Escape closes, or hides to the tray while a game is tracked) and drives three `Canvas`-based sections — New Games, Recent Games (horizontal strips) and User Games (grid). Controls are positioned manually with `Canvas.SetLeft/SetTop` from constants at the top of `MainWindow.xaml.cs` (`Gap`, `SectionGap`) and `GameInfoControl.ControlWidth/ControlHeight`.

Each section has **two code paths that must stay in sync**: `PopulateHorizontalSection`/`PopulateGridSection` (initial fill from `Window_Loaded`) and `UpdateHorizontalSection`/`UpdateGridSection` (diff-based re-layout with move/fade animations, driven by `RefreshAllSections`). After any library mutation, call `RefreshAllSections()`.

`BuildGridLayout` is the single source of tile positions and content height for the user-games grid, used by both paths. Ungrouped it reproduces the plain `index -> row/column` layout. Grouped (`AppSettings.UserGamesGroupDividers`, the header label right of the game count) each new group starts on a fresh row under a `GroupDivider` heading labelled with the group and its size. `GameGroupLabel` decides the group from the active ordering — first letter for `Title`, year for `Added`/`Completed`/`Released`, a time range for `PlayTime`, `"Unknown"` for a missing value — and `CanGroupBy` gates which orderings offer the toggle at all; grouping relies on `GetUserGames` returning each group's games consecutively, which every one of those orderings does.

Dividers are reconciled **by position** rather than by label, because culture-sensitive title sorting can put the same letter in two non-adjacent groups.

Clicking a divider collapses or expands its group. A collapsed group keeps its divider, but its games get no slot (`GridSlot?` is null), so they lose their tiles just as filtered-out games do. `_userGamesShown` holds the games that do have a tile, and it is what viewport culling uses. `"Unknown"` groups start collapsed and every other group starts expanded. `_toggledGroups` records the groups flipped away from that default, per sort mode, for the session only. Tiles and dividers that are fading out carry `IsRemoving`, so a quick collapse-then-expand creates fresh tiles instead of reusing ones that are about to be removed.

Other things that are hand-rolled rather than framework-provided:

- **Scrolling** is momentum physics — mouse wheel adds an impulse, `OnScrollRendering` (a `CompositionTarget.Rendering` hook) applies friction each frame and updates `TranslateTransform`s. The user-games grid additionally culls off-screen controls via `_visibleControls` / `UpdateViewportCulling`.
- **Dropdowns and context menus** are `Border` panels on a canvas toggled through `Visibility` with a full-screen `DropdownOverlay`, not `ContextMenu`. `_contextMenuTargetId` carries the game a menu action applies to.
- **Dialogs** (`ConfirmationDialog`, `MessageDialog`, `SettingsDialog`, `RestoreDialog`, `GamePropertiesDialog`) are custom borderless `Window`s using `DragMove()` and `WindowDimHelper` to dim the owner.

### Playtime tracking (`Tracking/`)

The design and the decisions behind it are in `docs/todo/playtime-tracking.md`.

- **`Session`**: `Start`, `End`, a stored `Duration` (measured running time, sleep excluded), and `Via` (the launcher that saw the session). No `Duration` means unmeasured: migrated history, or a launch the tracker couldn't follow.
- **`SessionTracker.Instance`** is the only way games are launched (`MainWindow.RunGame` → `Launch`). It adds the session at once, then follows the game with an `IRunningDetector` polled every 2 s:
  - `steam://` launches use Steam's `Apps\<id>\Running` registry flag.
  - Executables and scripts are started by `GameLauncher` inside a **Job Object** (`JobDetector`), ORed with a `FolderDetector` when `GameInfo.InstallDir` is known.
  - Everything else (GOG Galaxy, Epic, …) uses the `FolderDetector` alone, or stays unmeasured.
  - A session ends 30 s after the game was last seen. `ApplySessionRules` then merges it into the previous session if it started within 2 min of that one's end, and drops it if shorter than `AppSettings.MinSessionDurationSeconds`.
  - It must be driven from the UI thread: its loop resumes on the captured context, so library mutations stay on the UI thread. Only detector checks run on the thread pool.
- **Crash safety:** while tracking, open sessions are mirrored to `<library>.tracking.json` (at most every 60 s). `Restore()` at startup resumes or closes them.
- **Tray:** while anything is tracked, closing `MainWindow` hides it to a `TrayIcon` instead of exiting. Only the tray's *Exit* ends tracked sessions early. `App.OnSessionEnding` stops them synchronously on Windows logoff/shutdown.
- **`LauncherSync.Refresh`** (startup, and 15 s after each session ends) does two things:
  - Resolves shortcuts (`ShortcutResolver`) to fill `GameInfo.InstallDir` (never when `InstallDirIsManual`).
  - Imports `LauncherPlayTime` from Steam's `localconfig.vdf` (`SteamLocal`, with the small `VdfNode` parser) and GOG Galaxy's SQLite database (`GogGalaxy`, only `gog_` keys, read from a copy).
- **No double counting:** `GameInfo.TotalPlayTime` = launcher total + measured sessions the launcher hasn't seen. A session counts as already included when its `Via` is the launcher's `Source` and it ended before `LauncherPlayTime.ReadAt`, which is the time the total last *changed*. `Via = Steam` is observed from Steam's `RunningAppID` during the session, because direct launches of Steam games often relaunch through Steam.
- **UI:**
  - `GamesSortMode.PlayTime` sorts by `TotalPlayTime` and groups into `< 1 h` … `100 h+` / `Unknown`.
  - Tile badges come from the `TileBadges.All` list: the active sort's badge comes first and is always shown, the others appear on name hover. Add a badge by adding an entry there.
  - `GamePropertiesDialog` edits sessions through `SessionRow` working copies applied to the original `Session` objects, never by replacing the list, so a session the tracker holds is never clobbered.

### Refresh concurrency

Only one metadata refresh may run at a time; `TryStartRefreshAnimation()` doubles as the lock (`_isRefreshing`). If a per-game refresh is requested while the full pass is running, it goes on `_contextRefreshQueue` and is drained afterwards. `_stopRefreshRequested` is the cooperative cancel checked between games.

### Logging

`Log.WriteLine` always writes to `Debug`, and appends to `naLauncher2.log` only when `AppSettings.LogPath` is set. `LogPath` is a directory, not a file path. Long-running operations are wrapped in `TimedBlock` so the log doubles as timing data.
