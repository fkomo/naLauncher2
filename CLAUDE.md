# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```powershell
dotnet build naLauncher2.slnx              # build (net10.0-windows, WPF)
dotnet run --project naLauncher2.Wpf       # build & launch the app
dotnet publish naLauncher2.Wpf -p:PublishProfile=FolderProfile   # -> %AppData%\Ujeby\naLauncher2
```

There is no test project and no linter config — build warnings are the only static feedback.

### Ujeby.Core

The project has a raw DLL `<Reference>` to `..\..\Ujeby\publish\Ujeby.Core.dll` (i.e. `<repos>\fkomo\Ujeby\publish\Ujeby.Core.dll`), built from the sibling `fkomo/Ujeby` solution. It is not on NuGet; if that DLL is missing the build fails. Used for `Ujeby.Tools.TimedBlock` (the `using var tb = new TimedBlock(...)` timing/logging idiom used throughout), `Ujeby.Tools.GZip` (library backups), and the `Ujeby.Extensions.NormalizeCustom()` string extension (title matching against IGDB/Steam).

## Architecture

Single WPF project, no MVVM, no DI, no data binding for game content. Two process-wide singletons hold all state:

- **`AppSettings.Instance`** — `settings.json` next to the executable (`AppContext.BaseDirectory`). Loaded in `App.OnStartup`, saved in `App.OnExit` and by `SettingsDialog`.
- **`GameLibrary.Instance`** — a `ConcurrentDictionary<string, GameInfo>` persisted as JSON at `AppSettings.LibraryPath`.

**The dictionary key *is* the game title.** There is no id field. Consequences that keep biting: renaming a game (`GamePropertiesDialog`) is a remove + re-add of the key; `RefreshSources` must match on the shortcut path as well as the title, or a renamed game gets re-added as a duplicate on the next scan. "Removed"/uninstalled just means `GameInfo.Shortcut == null` — the entry stays in the library with its history.

### Startup flow

`App.OnStartup` (settings → `TwitchDevAuthz`) → `MainWindow.Window_Loaded` → `LoadLibraryAndSettings` (settings dialog if `LibraryPathMissing` → `GameLibrary.Load` → `Backup` → `RefreshSources`) → measure layout, populate sections → `RefreshNewGameDataInBackground` for any games the source scan discovered.

### Persistence rules

- Every mutation is followed by an explicit `await GameLibrary.Instance.Save()` from the caller — nothing auto-saves.
- Both library and settings serialize through the shared `App.JsonSerializerOptions`; use it on any new read/write or round-tripping breaks.
- `Backup()` writes a GZip `.bak` named `<library>_<yyyyMMddHHmmss>.bak`, skips when the SHA-256 matches the previous backup, and keeps the 10 newest. DEBUG builds additionally drop an uncompressed `.json` next to each `.bak`.
- `AppSettings.Load` copies each property one by one out of the deserialized instance. A new setting that isn't added to that copy block will silently never load.

### Metadata providers (`Api/`)

`IGameDataProvider<T>` has two implementations with very different mechanics:

- **`IgdbClient`** — real REST API (Apicalypse query strings POSTed to `api.igdb.com/v4`). Auth is `TwitchDevAuthz`, a `DelegatingHandler` that fetches and caches a client-credentials token and injects `Client-ID`/`Authorization`. Without Twitch credentials in settings, `App.TwitchDevAuthz` is null and constructing the client throws.
- **`SteamClient`** — no API; it scrapes the store HTML with regexes (`WebScraper`), rendered through headless Chromium via PuppeteerSharp. `WebScraper` lazily downloads a Chromium revision on first use (slow, one-time) and reuses one shared browser instance.

Both resolve a title to an id only on an unambiguous exact match after `NormalizeCustom()`; multiple matches are logged and treated as no match. Covers download into `ImageCachePath\IgdbCom` and `ImageCachePath\SteamDbInfo`; with no `ImageCachePath` set, images are skipped entirely.

Merge semantics differ per source and are deliberate: `GameInfo.UpdateFromIgdb` uses `??=` (never overwrites what's already there), while `UpdateFromSteam` overwrites `Summary`/`ImagePath` whenever Steam returns them. Steam therefore wins on description and cover art.

### UI layer

`MainWindow` is chrome-less (`WindowStyle=None`, `AllowsTransparency`, maximized; Escape closes) and drives three `Canvas`-based sections — New Games, Recent Games (horizontal strips) and User Games (grid). Controls are positioned manually with `Canvas.SetLeft/SetTop` from constants at the top of `MainWindow.xaml.cs` (`Gap`, `SectionGap`) and `GameInfoControl.ControlWidth/ControlHeight`.

Each section has **two code paths that must stay in sync**: `PopulateHorizontalSection`/`PopulateGridSection` (initial fill from `Window_Loaded`) and `UpdateHorizontalSection`/`UpdateGridSection` (diff-based re-layout with move/fade animations, driven by `RefreshAllSections`). After any library mutation, call `RefreshAllSections()`.

Other things that are hand-rolled rather than framework-provided:

- **Scrolling** is momentum physics — mouse wheel adds an impulse, `OnScrollRendering` (a `CompositionTarget.Rendering` hook) applies friction each frame and updates `TranslateTransform`s. The user-games grid additionally culls off-screen controls via `_visibleControls` / `UpdateViewportCulling`.
- **Dropdowns and context menus** are `Border` panels on a canvas toggled through `Visibility` with a full-screen `DropdownOverlay`, not `ContextMenu`. `_contextMenuTargetId` carries the game a menu action applies to.
- **Dialogs** (`ConfirmationDialog`, `MessageDialog`, `SettingsDialog`, `RestoreDialog`, `GamePropertiesDialog`) are custom borderless `Window`s using `DragMove()` and `WindowDimHelper` to dim the owner.

### Refresh concurrency

Only one metadata refresh may run at a time; `TryStartRefreshAnimation()` doubles as the lock (`_isRefreshing`). If a per-game refresh is requested while the full pass is running, it goes on `_contextRefreshQueue` and is drained afterwards. `_stopRefreshRequested` is the cooperative cancel checked between games.

### Logging

`Log.WriteLine` always writes to `Debug`, and appends to `naLauncher2.log` only when `AppSettings.LogPath` is set. Long-running operations are wrapped in `TimedBlock` so the log doubles as timing data.
