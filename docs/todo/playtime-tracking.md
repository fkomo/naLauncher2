# Playtime tracking

Goal: fill `Session.End` (and the session's duration) for every game launched from naLauncher,
across all the ways games in the library get launched, and add the playtime the launchers (Steam, GOG Galaxy) already recorded – without counting anything twice.

Status: **implemented** (Oct 2026). See §0 (decisions), §1 (design) and §2 (assumptions). §3 lists where the implementation differs from the design and what wasn't verified.
The original options analysis is kept as reference in §A–§E; section numbers in the decisions below point there.
*(verified)* = checked on the dev machine (Oct 2026); *(verify)* = from documentation/memory, confirm before relying on it.

---

## 0. Decisions

| # | Topic | Decision |
|---|-------|----------|
| D1 | What to measure (§B) | **Running time** (game processes alive). No focus/AFK detection (§C.7 dropped). **Plus launcher-reported time** from Steam and GOG Galaxy as an addition. |
| D2 | naLauncher closed during play (§D.1) | **Close to tray while a session is being tracked.** Only **one instance** of the app may run. |
| D3 | Short sessions (§D.3) | **Minimum session duration**, default **1 min**, configurable in settings. |
| D4 | Games started outside naLauncher (§D.4) | **Not tracked.** Steam/GOG sync covers part of it. No always-on watcher (§E option C dropped). |
| D5 | Launch path (§C.2) | OK to replace `ShellExecute` with `CreateProcess` + Job Object for direct shortcuts. |
| D6 | Import launcher history (§C.6) | **Yes** – Steam and GOG Galaxy. Adding `Microsoft.Data.Sqlite` for GOG is fine. |
| D7 | Data model | Whatever is needed, but **imported and tracked time must never be counted twice** (rule in §1.4). |

Decided while writing this up (shout if you disagree):

| # | Topic | Decision |
|---|-------|----------|
| D8 | Time spent in sleep/hibernate | **Not counted.** The tracker measures the duration itself (§D.2), stores it on the session, and doesn't derive it from `End - Start`. This also makes it immune to DST changes. |
| D9 | Several games at once | Allowed; one tracker per session. |
| D10 | Tray icon implementation | WinForms `NotifyIcon` (`UseWindowsForms` in the Wpf project, with `<Using Remove="System.Windows.Forms" />` so WinForms types don't clash with WPF ones). No extra package. |
| D11 | Timing constants | Wait up to **3 min** for the game to appear (Steam may need to start first). End the session after **30 s** with nothing detected. Fixed in code, not settings. |

Answers to the first round of open questions:

| # | Topic | Decision |
|---|-------|----------|
| D12 | Session shorter than the minimum (was Q1) | **Delete it entirely.** It doesn't count as played: no play-count increase, and the game doesn't stay in Recent Games because of it. A session the tracker *failed to measure* is still kept as unmeasured. |
| D13 | Quick relaunch (was Q2) | **Merge** into the previous session when a new session of the same game starts within **2 min** of the previous one's end. The gap isn't counted. The minimum-duration check (D12) runs on the merged session. |
| D14 | Where playtime shows (was Q3) | **Tile overlay in the top-left corner while sorting by playtime**, like the rating badge in rating sort. Also in **`GamePropertiesDialog`**. Nowhere else for now. |
| D15 | Manual session editing (was Q4) | **Yes**, in `GamePropertiesDialog` (§1.8). |
| D16 | Last tracked session ends while hidden in the tray (was Q5) | **Stay in the tray** until the user opens the window (tray *Show*, or launching naLauncher again). |
| D17 | Very long sessions (was Q6) | **Keep as measured.** Fix by hand via D15 if needed. |
| D18 | Sort mode (was Q7) | **New "Play time" entry** in the user-games sort menu. The existing **Played** (session count) stays. |
| D19 | Badge visibility and position (was Q8) | **Same rules as the rating badge:** a badge is always visible while sorting by its value, otherwise shown **on hover**. The **active sort's badge is always in the top-left corner**; the other badges line up to its right. Built generically, so more badges can be added later (§1.7). |
| D20 | Group dividers (was Q9) | **Yes**, buckets `< 1 h`, `1–10 h`, `10–50 h`, `50–100 h`, `100 h+`. |

---

## 1. Design

### 1.1 Overview

A `SessionTracker` in **naLauncher2.Core** (no UI) owns all running sessions. `MainWindow.RunGame` asks it to launch the game,
instead of calling `Process.Start` itself. For each launch it picks one **detector** based on the shortcut:

| Shortcut (current library) | Detector | Notes |
|----------------------------|----------|-------|
| `steam://rungameid/<appid>` (21) | **Steam registry** (§C.5) | Watch `HKCU\Software\Valve\Steam\Apps\<appid>\Running`. *(verified: key and value exist; `RunningAppID` is 0 when idle)* |
| `GalaxyClient.exe /command=runGame /gameId=<id> /path="…"` (13) | **Install folder** (§C.4) | Folder from the `/path=` argument. Start Galaxy with `ShellExecute` as today; don't put it in a Job Object, because Galaxy is long-lived. |
| Epic `.url` (1), EA (2) | **Install folder** | Epic: folder from the manifests (*(verified)* `C:\ProgramData\Epic\…\Manifests` doesn't exist on this machine, so it may need a manual folder). EA: manual folder. |
| `.lnk` → exe, `.exe`, `.cmd`, `.bat` (≈80) | **Job Object** (§C.2) | Covers bootstrappers, DOSBox/ScummVM wrappers and batch files. |
| Job Object can't be used (elevation required, `.lnk` can't be resolved) | **Install folder** if one is known; otherwise a **plain launch** with an unmeasured session | Fallback. A launch must never fail because of tracking. |
| Job empties within ~30 s and the game has its own (not shared) folder | switch to **Install folder** | Covers launchers that hand off to an already-running instance and similar cases. |

Session life cycle:

1. **Launch** → session created with `Start = now`, saved right away. From the user's point of view this is the same as today: the game shows up in Recent Games immediately.
2. **Waiting** – up to 3 min for the detector to see the game (D11).
   If it never does → the session stays **unmeasured** (`End` and duration null, like migrated sessions). Nothing is deleted on a tracking failure.
3. **Running** – the duration accumulates in poll-sized steps (§D.2); steps much longer than the poll interval (sleep) are dropped.
4. **Ended** – nothing has been detected for 30 s → `End = last time seen`, `Duration = accumulated`.
5. **Merge** (D13) – if the game's previous session ended less than 2 min before this one started, this session is folded into it:
   the previous session's `End` moves to this `End`, the durations are added, and this session is removed.
6. **Minimum duration** (D12) – if the (merged) `Duration < MinSessionDuration`, the session is **deleted**.
   The game was put at the front of Recent Games at launch (step 1), so it drops back to where it was, or returns to New Games if this was its only session.
7. Save, `RefreshAllSections()`, then re-read launcher playtime for that game (§1.4).

### 1.2 Single instance + tray (D2)

- A named `Mutex` in `App.OnStartup`. A second instance signals the first one (named `EventWaitHandle`) to show and activate its window, then exits.
- **While at least one session is being tracked**: Escape / closing the window **hides** it, and a tray icon stays. Tray menu: *Show*, *Exit*.
  *Exit* while tracking asks for confirmation, then ends all sessions at the current time.
- **When the last tracked session ends while the window is hidden**: stay in the tray (D16). The tray icon remains until the user picks *Show*
  (or starts naLauncher again, which activates the running instance) or *Exit*.
- **When nothing is being tracked and the window is visible**: Escape closes the app as today.
- Windows logoff/shutdown (`SessionEnding`): end all sessions at the current time and save.
- Crash or kill while tracking: open sessions are written to a small state file next to the library (`<library>.tracking.json`: game, start, accumulated duration, last seen, detector info), refreshed about every 60 s.
  This keeps the 600 KB library from being rewritten every minute. On the next start:
  - if the detector can still find the game (Steam registry, install folder), resume tracking;
  - otherwise end the session at *last seen*;
  - then delete the state file.

### 1.3 Data model

```csharp
public class Session
{
    public DateTime Start { get; set; }
    public DateTime? End { get; set; }          // wall-clock end; null = unmeasured (migrated, or tracking failed)
    public TimeSpan? Duration { get; set; }     // measured running time (sleep excluded) – stored, no longer computed from End
    public LaunchVia? Via { get; set; }         // launcher that saw this session (§1.4); null = none / unknown
}

public enum LaunchVia { Steam, Gog }

// GameInfo additions
public string? InstallDir { get; set; }         // used by the install-folder detector
public bool InstallDirIsManual { get; set; }    // set from GamePropertiesDialog; auto-resolve never overwrites it
public LauncherPlayTime? LauncherPlayTime { get; set; }

public class LauncherPlayTime
{
    public LaunchVia Source { get; set; }       // Steam or Gog
    public TimeSpan Total { get; set; }         // the launcher's own total (minutes in both sources)
    public DateTime ReadAt { get; set; }        // when the total last changed (see §3)
}

// computed: TrackedPlayTime, TotalPlayTime (see §1.4)

// AppSettings addition – remember the property copy block in AppSettings.Load
public int MinSessionDurationSeconds { get; set; } = 60;
```

`Session.Duration` changes from a computed property to a stored one. That's fine: no migrated session has `End`, so nothing has to be backfilled.
A migration step is needed only if Q1 below ends up changing the meaning of existing data. No JSON shape change breaks existing files: all new fields are optional.

### 1.4 Combining tracked and launcher time without double counting (D7)

The launcher's total already contains every session that went **through** that launcher, so those sessions must not be added again.
Sessions that **didn't** go through it (a direct `.exe` launch of a GOG game, for example) are invisible to the launcher, so they have to be added.

```
Total = LauncherPlayTime.Total
      + Σ Duration of sessions where Via != LauncherPlayTime.Source         // launcher never saw these
      + Σ Duration of sessions where Via == Source and End > ReadAt         // launcher saw them, but after our last read
```

If the game has no `LauncherPlayTime`, `Total = Σ Duration`.

What makes this work:

- **`Via` is observed, not inferred from the shortcut.** During a session the tracker checks Steam's `RunningAppID`: if it equals the game's
  appid at any point, then `Via = Steam`. This matters because many Steam games launched by a direct `.lnk` to their exe restart themselves through
  Steam (`SteamAPI_RestartAppIfNecessary`), so Steam counts them even though the shortcut isn't a `steam://` URL.
  For GOG, `Via = Gog` when launched through `GalaxyClient.exe`. *(verify whether Galaxy also counts games started outside it while it runs)*
- **When the launcher total is read:** at startup, and after each tracked session ends (with a short delay, because Steam writes `localconfig.vdf` when the game exits).
  Never while a session of that game is running – otherwise `ReadAt` falls inside a session and that session gets counted half-twice.
- **Which launcher a game belongs to:** Steam if the game has an appid (from the `steam://` URL, or else `Extensions["SteamAppId"]`) **and** Steam
  reports playtime for it. Otherwise GOG if a GOG id is known (from the `/gameId=` argument, or `goggame-<id>.info` in the install folder) and Galaxy has a row for it.
  One source per game. If both have data, use Steam, because it's the more reliable file to read.
- **GOG Galaxy also stores other platforms' time.** Its database holds `steam_…`, `epic_…`, `xboxone_…` and other release keys, copied from Galaxy's integrations
  *(verified: hundreds of non-`gog_` keys)*. **Read only `gog_<id>` keys**, or Steam time gets counted a second time through Galaxy.
- Launcher totals include play on other PCs. That's desirable: it's "my playtime for this game".

### 1.5 Launcher data sources (D6)

| Source | Location | Details |
|--------|----------|---------|
| Steam | `<SteamPath>\userdata\<accountid>\config\localconfig.vdf` → `…\apps\<appid>\Playtime` (minutes) | `SteamPath` from `HKCU\Software\Valve\Steam`. Needs a small VDF (text KeyValues) parser. *(verified: one account on this machine, 154 apps with `Playtime`)*. With several accounts, use the one marked `MostRecent` in `config\loginusers.vdf`. |
| GOG Galaxy | `C:\ProgramData\GOG.com\Galaxy\storage\galaxy-2.0.db` – table `GameTimes(userId, releaseKey, minutesInGame)`; also `LastPlayedDates(userId, gameReleaseKey, lastPlayedDate)` | *(verified schema)*. Galaxy keeps the file open: copy it to a temp file and read the copy (*(verified)* opening with `FileShare.ReadWrite` works), or open SQLite read-only. |

`LastPlayed` (Steam) and `lastPlayedDate` (GOG) could also update `GameInfo`'s recent-games ordering for games played outside naLauncher.
Not planned (D4), but it's cheap if wanted later.

### 1.6 Install folder resolution

When a game is added or refreshed (and on demand), fill `InstallDir` unless `InstallDirIsManual` is set:

- GOG Galaxy shortcut → `/path=` argument.
- Steam → `libraryfolders.vdf` → `appmanifest_<appid>.acf` → `steamapps\common\<installdir>`. Only used to resolve the GOG/Steam overlap and as a fallback; the Steam detector itself doesn't need it.
- Epic → manifests, when present.
- Direct `.lnk`/`.exe` → the target's folder, **unless** it's shared (DOSBox, ScummVM, a drive root, a folder that also holds other games' targets).
  Only used for the Job Object fallback.
- Manual field in `GamePropertiesDialog`, which sets `InstallDirIsManual`.

Install-folder detector exclusion list (processes ignored even if they live in the game folder): `UnityCrashHandler*.exe`, `CrashReportClient.exe`,
`vc_redist*.exe`, `DXSETUP.exe`, `*Uninstall*.exe`, `unins*.exe`, `EasyAntiCheat*`, `BEService*`. Extend as needed.

### 1.7 UI (D14)

**Tile badge** (D19) – a `PlayTimeBadge` showing `TotalPlayTime`, behaving exactly like `RatingBadge`:

| Sort | Rating badge | Playtime badge |
|------|--------------|----------------|
| Rating | always | on hover |
| Play time | on hover | always |
| any other | on hover | on hover |

**Layout:** all badges live in one horizontal `StackPanel` (a badge strip) in the top-left corner. A collapsed badge takes no space.

- **The active sort's badge comes first**, so it's always in the corner and never moves.
- The other badges follow in a fixed default order (rating, playtime, then any added later). They appear to the right of the active one on hover.
- Example, playtime sort on a game with a rating: `[34 h]` normally, `[34 h][87]` on hover.
  Rating sort: `[87]` normally, `[87][34 h]` on hover. Any other sort: nothing normally, `[87][34 h]` on hover.
- A badge without a value (no rating, no known time) is never shown, and the next badge takes its place.

**Generic, so more badges can be added later:**

- Each badge is described once: which sort mode it belongs to, how to get its value from `GameInfo` (none → no badge), how to format it, and how to color it (rating keeps its metacritic colors).
- `GameInfoControl` builds the strip from that list, putting the badge whose sort mode is active first. It no longer takes one bool per badge.
  Instead of today's `isRatingSortActive` / `isReleaseDateSortActive` flags, it receives the active `GamesSortMode`.
  `isReleaseDateSortActive` controls something other than a badge; keep it, or derive it from the same sort mode.
  Both `PopulateGridSection` and `UpdateGridSection` must pass the sort mode.
- Hover logic shows or hides every non-active badge together, replacing today's rating-specific code in the hover handlers and in the code that refreshes a tile after a metadata update.
- Adding a badge later = one new entry in the list (plus its sort mode, if it gets one).
- Format: `45 min` under an hour, `12 h` from 1 h up, `1 234 h` with a thousands separator. Games with no known time have no badge.

**Sort mode** (D18): `GamesSortMode.PlayTime`, sorting by `TotalPlayTime`. Settings store enums by name, so it can go anywhere in the enum. Add it to the sort menu next to **Played**, and to the switch in `MainWindow` that orders the games.

**Group dividers** (D20): `CanGroupBy(PlayTime)` returns true, and `GameGroupLabel` maps `TotalPlayTime` to buckets:

| Group label | Range |
|-------------|-------|
| `< 1 h` | more than 0, under 1 h |
| `1–10 h` | 1 h up to (not including) 10 h |
| `10–50 h` | 10 h up to 50 h |
| `50–100 h` | 50 h up to 100 h |
| `100 h+` | 100 h and more |
| `Unknown` | no known time (never played, or only unmeasured sessions) – the label the other orderings already use for a missing value |

Sorting by time already keeps each bucket's games together, which grouping relies on. Bucket order follows the sort direction.

**`GamePropertiesDialog`** – a playtime block:
- total, plus the split: tracked by naLauncher / reported by Steam or GOG (with when it was read);
- the session list (§1.8).

### 1.8 Manual session editing (D15)

In `GamePropertiesDialog`, a list of the game's sessions, newest first: start, end, duration, and a marker for unmeasured or still running sessions.
- **Edit** a session's duration (and its end, which follows from it).
- **Delete** a session.
- **Add** a session by hand (start + duration), for play the tracker missed.
- A session that is being tracked right now can't be edited.
- Launcher-reported time is read-only: it's re-read from Steam/GOG and would be overwritten anyway.
- Edits go through the usual `GameLibrary.Instance.Save()` + `RefreshAllSections()`.

### 1.9 Implementation order

1. Data model + settings (`MinSessionDurationSeconds`) + single instance + tray.
2. `SessionTracker` with the Job Object detector, the session life cycle and the crash-state file. This alone gives durations for ≈80 games.
3. Steam registry detector + `Via` observation.
4. Install-folder resolution + install-folder detector (GOG, Epic, EA, fallbacks) + manual folder field.
5. Steam `localconfig.vdf` + GOG database readers, `LauncherPlayTime`, `TotalPlayTime`.
6. UI: generic badge strip (moving the rating badge onto it first, no visible change), then the playtime badge + sort mode + group dividers (§1.7), then the properties dialog playtime block + session editing (§1.8).

---

## 2. Still open

Nothing. All questions are answered (D12–D20).

Assumptions written into the design (say if you disagree):

- The badge and the sort use **`TotalPlayTime`** (tracked + launcher, §1.4), not just naLauncher's own tracking.
- Games with no known time (never played, or only unmeasured sessions such as all migrated history) sort as **0**: at the bottom in descending order, without a badge, and in the `Unknown` group.
- The badge refactor (§1.7) also moves the existing rating badge onto the new badge strip. Its look and behaviour in rating sort and on hover stay the same.

---

## 3. Implementation notes

Code: `src/naLauncher2.Core/Tracking/` (`SessionTracker`, `GameLauncher`, `Detectors`, `ShortcutResolver`, `LauncherSync`, `SteamLocal`, `GogGalaxy`, `Native`, `Vdf`), `src/naLauncher2.Core/PlayTimeFormat.cs`, and in the Wpf project `TrayIcon`, `TileBadges`, plus changes to `App`, `MainWindow`, `GameInfoControl`, `GamePropertiesDialog` and `SettingsDialog`.

Where the implementation differs from the design above:

- **Job Object + install folder are combined, not switched (§1.1).** For direct launches, the game counts as running while *either* the Job Object has a process *or* (when `InstallDir` is known) something runs from the install folder. The design switched to the folder only if the job emptied within 30 s. The combined check covers the same cases (hand-off to an already-running instance, relaunch through a launcher) with less special-casing.
- **`LauncherPlayTime.ReadAt` is when the total last *changed*, not when it was last read (§1.3, §1.4).** It's bumped only when the launcher's total differs from the stored one.
  - Steam recorded the session: the total changed after the session ended, so the session falls before `ReadAt` and isn't added again.
  - Steam didn't record it (offline, failed to write): the total is unchanged, `ReadAt` stays older than the session, and the session is still added.
  - Edge case: a total that changes later only because of play on another PC moves `ReadAt` past an unrecorded session, which then stops being added.
- **Resumed sessions (§1.2)** don't count the time naLauncher wasn't running. That time is unknown and could include sleep.
- **The first poll after the game appears isn't counted**, so a session can be up to ~2 s short (a 15 s test run measured 12 s).
- **Sessions added by hand** have no `Via`, so they're always added on top of a launcher total. If Steam already counted that play, it's counted twice; delete the session in that case.
- **Exiting from the tray while tracking** ends the sessions at that moment, even if the games keep running.
- **Tray icon:** WinForms `NotifyIcon` (D10). Its *Show* also brings the window back when naLauncher is started a second time.
- **Dependency:** `Microsoft.Data.Sqlite` 10.0.12. 10.0.0 pulled in a native SQLite build with a known high-severity advisory (NU1903).

Verified on the dev machine:
- Shortcut resolution over all 118 shortcuts: 68 executables, 14 scripts, 21 Steam, 13 GOG Galaxy, 1 Epic, none marked "run as administrator".
- Install folders resolved for 97 of 117 installed games; Steam playtime imported for 83 games and GOG playtime for 7. The sync takes about 0.5 s.
- A Job Object followed a process tree after the launching script exited.
- End-to-end tracking: the session was measured, a too-short session was dropped, and the state file was written and then removed.
- Crash recovery, including the merge rule.
- Single instance: a second start signalled the first instance and exited.
- Playtime sort with dividers and badges in the running app.

Not verified yet:
- Hiding to the tray, then Show and Exit from the tray.
- Hover order of the badges.
- The properties dialog's playtime block and session editing.
- A real Steam / GOG Galaxy / Epic launch end to end.
- An elevated game falling back to shell launch.
- Windows logoff/shutdown while tracking.

---
---

# Reference: original options analysis

## A. Library at analysis time

`MainWindow.RunGame` does `Process.Start(new ProcessStartInfo(game.Shortcut) { UseShellExecute = true })`, then appends `new Session(DateTime.Now)` to `GameInfo.Played`.
The returned process is discarded, and nothing observes the game afterwards.

`C:\Users\filip\Games`, 118 shortcuts, Oct 2026:

| # | Kind | Example target | What `Process.Start` returns |
|---|------|----------------|------------------------------|
| 69 | `.lnk` → game exe | `D:\Games\Commandos Origins\Commandos.exe` | the exe process; sometimes a bootstrapper that spawns the real game and exits |
| (subset) | `.lnk` → shared runtime | `C:\Program Files\ScummVM\scummvm.exe`, `.bat` wrappers | the runtime / `cmd.exe` |
| 21 | `.url` → `steam://rungameid/<appid>` | | **`null`** – Steam (already running) starts the game |
| 13 | `.lnk` → `GalaxyClient.exe /command=runGame /gameId=<id> /path="D:\..."` | | GOG Galaxy client – long-lived, unrelated to game lifetime |
| 12 | `.cmd` → DOSBox Staging | `"…\dosbox.exe" "d:\GamesOld\BlueForce\blue.exe"` | `cmd.exe`, which blocks until DOSBox exits |
| 2 | EA | | launcher / `null` |
| 1 | `.url` → Epic (`com.epicgames.launcher://…`) | | `null` |

Two facts drive everything:

1. **~30 % of launches never give us a process at all** (Steam/Epic URLs) or give us the wrong one (GOG Galaxy, EA).
2. **Exe path is not a unique game identity**: every DOSBox game runs `dosbox.exe`, every ScummVM game runs `scummvm.exe`.

## B. What to measure

| Definition | Meaning | Needs |
|------------|---------|-------|
| **Running time** ✅ D1 | game process(es) alive | process detection only |
| **Active time** | running *and* foreground *and* user not AFK | process detection + window focus + input idle detection |
| **Launcher-reported time** ✅ D1 (addition) | whatever Steam / GOG count | reading their data files |

## C. Detection techniques

### C.1 Wait on the process we started – *superseded by C.2*

`var p = Process.Start(...); await p.WaitForExitAsync();`
Works for direct exes and `.cmd`/`.bat`. It fails for `.url` launches (`null`), for launchers (we'd wait on Galaxy/EA) and for bootstrappers (the session would last a few seconds).

### C.2 Job Object around the launched process tree – ✅ chosen for direct launches

Windows *Job Objects* group a process and **all processes it spawns** (children, grandchildren, even after the parent exits).
The session ends when the job has zero active processes.

1. Resolve the shortcut ourselves instead of `UseShellExecute`:
   - `.lnk` → `IShellLinkW` (COM) or `WScript.Shell.CreateShortcut`: target path, arguments, working directory.
   - `.cmd`/`.bat` → `cmd.exe /c "<file>"`.
   - `.exe` → itself.
2. Create the process **suspended** (`CreateProcess` with `CREATE_SUSPENDED`), `AssignProcessToJobObject`, then `ResumeThread`. This avoids the race where a child spawns before the process is in the job.
   (Win10+ alternative: `PROC_THREAD_ATTRIBUTE_JOB_LIST` in `STARTUPINFOEX`.)
3. Do **not** set `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`: the game must survive naLauncher exiting.
4. Observe the job either event-driven (an I/O completion port signals `JOB_OBJECT_MSG_ACTIVE_PROCESS_ZERO`) or by polling `JobObjectBasicAccountingInformation.ActiveProcesses`.

Gotchas:
- No help for launcher-mediated games: the game is the launcher's child, not ours.
- **Elevation:** "Run as administrator" shortcuts and exes with an admin manifest make `CreateProcess` fail with `ERROR_ELEVATION_REQUIRED` (740). Fall back to `ShellExecute` + `runas` and the install-folder detector.
- Nested jobs work since Windows 8.
- `.lnk` resolution must replicate Explorer: environment variables, working directory, `ShowCmd`. MSI "advertised" shortcuts have no plain target.
- A bug here breaks launching, not just tracking. Keep `UseShellExecute` as the fallback.

### C.3 Parent-PID tree polling – *rejected*

Same coverage as C.2 without changing the launch path, but it loses the chain when a bootstrapper exits faster than the poll interval. C.2 is strictly better.

### C.4 Match processes by install folder – ✅ chosen for launcher-mediated games and fallbacks

Every few seconds, list processes and check whether each one's image path is under the game's install folder.

- Path: `QueryFullProcessImageName` with `OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION)`. `Process.MainModule` throws for elevated, 32↔64-bit and protected processes.
  Protected anti-cheat *services* refuse even limited access, but they aren't the game process *(verify with an EAC/BattlEye game)*.
- Cost: ~300 processes every 5 s – negligible. Cache by (PID, creation time).
- WMI/ETW process events exist, but either poll internally (WMI `__InstanceCreationEvent`) or need admin (`Win32_ProcessStartTrace`, ETW). Not worth it.

Folder sources:

| Source | How to get the folder |
|--------|-----------------------|
| Direct `.lnk`/`.exe` | directory of the target exe (unless shared – see §1.6) |
| GOG Galaxy `.lnk` | `/path="…"` argument; the folder also contains `goggame-<id>.info` (JSON with `playTasks`) |
| Steam | `libraryfolders.vdf` → `appmanifest_<appid>.acf` → `installdir` → `steamapps\common\<installdir>` |
| Epic | `C:\ProgramData\Epic\EpicGamesLauncher\Data\Manifests\*.item` (JSON: `AppName`, `InstallLocation`, `LaunchExecutable`) |
| EA app | no simple mapping; manual |
| Ubisoft | `HKLM\SOFTWARE\WOW6432Node\Ubisoft\Launcher\Installs\<id>\InstallDir` *(verify)* |
| MS Store / Xbox | package install location via `PackageManager` *(verify access to `WindowsApps`)* |

Pitfalls: shared folders (runtimes, drive roots); helper processes that linger (crash handlers, in-folder launchers) – hence the exclusion list;
games whose main exe lives elsewhere (Java games via a system JRE) – manual override.

### C.5 Steam's own "running" flag – ✅ chosen for Steam

While a Steam game runs: `HKCU\Software\Valve\Steam\RunningAppID` = its appid, and `HKCU\Software\Valve\Steam\Apps\<appid>\Running` = 1 *(verified: keys present)*.
Reading them is cheap; `RegNotifyChangeKeyValue` makes it event-driven. Use the appid from the `steam://` URL, which is authoritative.
*(verify)* offline mode, and whether `Running` resets after a crash.

### C.6 Launcher-recorded playtime – ✅ chosen as an addition (§1.4, §1.5)

| Launcher | Where |
|----------|-------|
| Steam | `localconfig.vdf` → `Playtime` (minutes), `LastPlayed` (unix time). Also available through the Steam Web API with a key. |
| GOG Galaxy | `galaxy-2.0.db` → `GameTimes`, `LastPlayedDates` |
| Epic | no known local playtime store *(verify)* |
| EA / Ubisoft | nothing practical |

### C.7 Window focus and input idle – *dropped (D1)*

Foreground check (`GetForegroundWindow`) and idle check (`GetLastInputInfo`). Caveat: `GetLastInputInfo` doesn't see XInput gamepad input.

### C.8 Manual entry / correction – see Q4

## D. Cross-cutting concerns

### D.1 naLauncher closing while a game runs – ✅ tray (D2) + crash-state file (§1.2)

Options considered: persist the open session and reconcile on restart; hide to tray while tracking; a separate watcher process (rejected: more moving parts, concurrent writes to the library).

### D.2 Sleep / hibernate / clock changes – ✅ D8

Accumulate time in poll-sized steps and drop steps much longer than the poll interval, so time spent suspended isn't counted.
`DateTime.Now` is local time without offset, so `End - Start` is off by an hour across a DST change. Storing the measured `Duration` avoids both problems.

### D.3 Multiple games / relaunches – ✅ D9; relaunch merging is Q2

### D.4 Games started outside naLauncher – ✅ not tracked (D4)

### D.5 Performance and UI thread

All polling runs on background timers/tasks, never in `CompositionTarget.Rendering`. Library mutations go back through `GameLibrary.Instance.Save()` + `RefreshAllSections()` on the dispatcher.

## E. Candidate solutions considered

- **A. Minimal** – wait on the process + Steam registry. Leaves GOG/EA/Epic and bootstrapper games unmeasured.
- **B. Hybrid tracker** – ✅ chosen, refined into §1.
- **C. Always-on watcher** – naLauncher resident at login, tracking everything. Dropped (D4).
