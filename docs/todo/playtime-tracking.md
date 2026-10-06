# Playtime tracking – options analysis

Goal: fill `Session.End` (and therefore `Session.Duration`) for every game session, as reliably as possible,
across all the ways games in the library get launched.

Status: **undecided** – this document collects the candidate techniques so we can pick one (or a combination).
Things marked *(verify)* are from memory/documentation and should be confirmed on a real machine before relying on them.

---

## 1. Current state

- `MainWindow.RunGame` does `Process.Start(new ProcessStartInfo(game.Shortcut) { UseShellExecute = true })`,
  then appends `new Session(DateTime.Now)` to `GameInfo.Played`. Nothing observes the game afterwards.
- `Session { DateTime Start; DateTime? End; }`. All sessions migrated from the old `List<DateTime>` have `End == null`.
- The process `Process.Start` returns is discarded.

### What the library actually contains (`C:\Users\filip\Games`, 118 shortcuts, Oct 2026)

| # | Kind | Example target | What `Process.Start` returns |
|---|------|----------------|------------------------------|
| 69 | `.lnk` → game exe | `D:\Games\Commandos Origins\Commandos.exe` | the exe process; sometimes a bootstrapper that spawns the real game and exits |
| (subset) | `.lnk` → shared runtime | `C:\Program Files\ScummVM\scummvm.exe`, `.bat` wrappers | the runtime / `cmd.exe` |
| 21 | `.url` → `steam://rungameid/<appid>` | | **`null`** – Steam (already running) starts the game |
| 13 | `.lnk` → `GalaxyClient.exe /command=runGame /gameId=<id> /path="D:\..."` | | GOG Galaxy client – long-lived, unrelated to game lifetime |
| 12 | `.cmd` → DOSBox Staging | `"…\dosbox.exe" "d:\GamesOld\BlueForce\blue.exe"` | `cmd.exe`, which blocks until DOSBox exits |
| 2 | EA | | launcher / `null` |
| 1 | `.url` → Epic (`com.epicgames.launcher://…`) | | `null` |

Two facts drive everything below:

1. **~30 % of launches never give us a process at all** (Steam/Epic URLs) or give us the wrong one (GOG Galaxy, EA).
2. **Exe path is not a unique game identity**: every DOSBox game runs `dosbox.exe`, every ScummVM game runs `scummvm.exe`.

So no single technique covers the library; the realistic answer is a small set of *detectors* behind one tracker.

---

## 2. What do we want to measure?

Decide this first, because it changes which signals matter:

| Definition | Meaning | Needs |
|------------|---------|-------|
| **Running time** | game process(es) alive | process detection only |
| **Active time** | running *and* foreground *and* user not AFK | process detection + window focus + input idle detection |
| **Launcher-reported time** | whatever Steam / GOG count | reading their data files |

Running time is simple and predictable; it over-counts when a game sits paused in the background or overnight.
Active time is closer to "I played X hours" but has its own failure modes (see §3.7, gamepads).
A common compromise: record running time, but cut a session short after N minutes without input.

---

## 3. Detection techniques

### 3.1 Wait on the process we started

`var p = Process.Start(...); await p.WaitForExitAsync();`

- **Works for:** `.lnk`/`.exe` pointing straight at the real game; `.cmd`/`.bat` (cmd waits for GUI programs started from a batch file); DOSBox/ScummVM wrappers.
- **Fails for:**
  - `.url` launches – `Process.Start` returns `null`.
  - GOG Galaxy / EA – we'd be waiting on the launcher, which never exits (or exits immediately when it hands off to an already-running instance).
  - **Bootstrappers** – e.g. Unreal games where `Game.exe` starts `Game-Win64-Shipping.exe` and exits; many publisher pre-launchers. The session would be a few seconds long.
- **Cost:** trivial. Event-driven, no polling.
- **Verdict:** fine as a baseline, but needs a "process exited suspiciously fast" fallback to be trustworthy.

### 3.2 Job Object around the launched process tree (recommended core for direct launches)

Windows *Job Objects* group a process and **all processes it spawns** (children, grandchildren, even after the parent exits).
Instead of watching one PID we watch the job: the session ends when the job has zero active processes.

How:

1. Resolve the shortcut ourselves instead of `UseShellExecute`:
   - `.lnk` → `IShellLinkW` (COM) or `WScript.Shell.CreateShortcut`: target path, arguments, working directory.
   - `.cmd`/`.bat` → `cmd.exe /c "<file>"`.
   - `.exe` → itself.
2. Create the process **suspended** (`CreateProcess` with `CREATE_SUSPENDED` via P/Invoke), `AssignProcessToJobObject`, then `ResumeThread`.
   This avoids the race where the process spawns a child before it's in the job.
   (Alternative on Win10+: `PROC_THREAD_ATTRIBUTE_JOB_LIST` in `STARTUPINFOEX` assigns it atomically.)
3. Do **not** set `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE` – the game must survive naLauncher exiting.
4. Observe the job:
   - **Event-driven:** associate the job with an I/O completion port (`JobObjectAssociateCompletionPortInformation`) and wait for
     `JOB_OBJECT_MSG_ACTIVE_PROCESS_ZERO`. No polling at all.
   - or poll `QueryInformationJobObject(JobObjectBasicAccountingInformation).ActiveProcesses`.

Pros:
- Exact for bootstrappers, batch wrappers and shared runtimes (DOSBox, ScummVM) – we track *our* tree, not an exe name.
- Event-driven, effectively zero cost.
- Covers ~80 of the 118 current shortcuts.

Cons / gotchas:
- Doesn't help with launcher-mediated games (Steam/Epic/GOG/EA): the game is a child of the *launcher*, not of us.
- **Elevation:** a shortcut marked "Run as administrator", or an exe whose manifest requires admin, makes `CreateProcess` fail with `ERROR_ELEVATION_REQUIRED` (740).
  We'd have to fall back to `ShellExecute` with the `runas` verb, and a non-elevated naLauncher can't put an elevated process in its job.
  Fallback: technique 3.4 for those games.
- A child can escape the job only if the job allows breakaway (we wouldn't) *and* it asks for it. Processes started indirectly through COM/brokers
  (e.g. a game asking Explorer to open something) are not in the job – irrelevant for games in practice.
- Nested jobs are supported since Windows 8, so it's fine if naLauncher itself is running inside a job (e.g. started from some terminals).
- `.lnk` resolution has to replicate what Explorer does: environment variables in paths, working directory, `ShowCmd`.
  MSI "advertised" shortcuts don't have a plain target path (rare for games).
- Changes the launch path for every game – a bug here breaks launching, not just tracking. Keep `UseShellExecute` as a fallback when resolution fails.

### 3.3 Parent-PID tree polling (Job Object alternative)

Poll a process snapshot (`CreateToolhelp32Snapshot` / `Process.GetProcesses` + parent PID) every 1–2 s and keep a set
"PIDs that descend from the one we launched".

- Same coverage as 3.2, without changing how processes are started (still `UseShellExecute`).
- **Weaker:** a bootstrapper that starts the game and exits faster than the poll interval leaves an orphan whose parent PID
  points at a dead process – we can lose the chain. PID reuse requires comparing process creation times.
- Only worth it if we decide not to touch the launch code. 3.2 is strictly better.

### 3.4 Match processes by install folder (core for launcher-mediated games)

Every few seconds, list processes, get each one's full image path and check whether it lives under the game's install folder.
The session lasts as long as at least one matching process exists.

**Getting process paths:** `QueryFullProcessImageName` with `OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION)`.
`Process.MainModule` is unusable – it needs more access rights and throws for elevated / 32↔64-bit / protected processes.
Limited-information access works across elevation for normal processes; protected processes (some anti-cheat services) refuse it,
but the anti-cheat *service* isn't the game process, so this rarely matters *(verify with an EAC/BattlEye game)*.

**Cost:** ~300 processes every 5 s, one `OpenProcess` + one call each – negligible. Cache by (PID, creation time) to avoid repeat lookups.

**Event-driven alternatives (probably not worth it):**
- WMI `__InstanceCreationEvent WITHIN 2 WHERE TargetInstance ISA 'Win32_Process'` – no admin needed, but WMI polls internally anyway and is heavy.
- WMI `Win32_ProcessStartTrace` / ETW kernel process provider – real events, but **need admin**.

**Where the install folder comes from (per launcher):**

| Source | How to get the folder |
|--------|-----------------------|
| Direct `.lnk`/`.exe` | directory of the target exe (but see "too broad" below) |
| GOG Galaxy `.lnk` | `/path="…"` argument of the shortcut; the folder also contains `goggame-<id>.info` (JSON with `playTasks`, the real exe) |
| Steam `steam://rungameid/<appid>` | `<Steam>\steamapps\libraryfolders.vdf` lists library roots → `<root>\steamapps\appmanifest_<appid>.acf` → `installdir` → `<root>\steamapps\common\<installdir>`. Steam's location: `HKCU\Software\Valve\Steam\SteamPath` |
| Epic `com.epicgames.launcher://apps/<ns>%3A<catalog>%3A<appname>` | `C:\ProgramData\Epic\EpicGamesLauncher\Data\Manifests\*.item` (JSON: `AppName`, `InstallLocation`, `LaunchExecutable`) |
| EA app (`link2ea://…`) | no simple public mapping; registry uninstall keys or manual entry *(verify)* |
| Ubisoft (`uplay://launch/<id>`) | `HKLM\SOFTWARE\WOW6432Node\Ubisoft\Launcher\Installs\<id>\InstallDir` *(verify)* |
| MS Store / Xbox (`shell:AppsFolder\…`) | package install location via `PackageManager`; process paths under `C:\Program Files\WindowsApps` *(verify access)* |
| anything | manual override field in `GamePropertiesDialog` |

The resolved folder would be stored on `GameInfo` (e.g. `InstallDir`), so resolution happens once, not on every launch.

**Pitfalls:**
- **Folder too broad:** a `.lnk` to `C:\Program Files\ScummVM\scummvm.exe` or `…\DOSBox Staging\dosbox.exe` gives a folder shared by many games.
  A `.bat` in `D:\Games` would give the whole games drive. Guard: don't auto-derive folders for known runtimes / drive roots / folders that
  contain other games' targets – use 3.2 for those instead.
- **Helpers in the folder:** `UnityCrashHandler64.exe`, `CrashReportClient.exe`, `vc_redist.x64.exe`, `DXSETUP.exe`, anti-cheat bootstrappers,
  launchers inside the game dir (`Launcher.exe` that stays open in the tray). Need an exclusion list; otherwise crash handlers that linger
  keep the session open.
- **Games outside their folder:** some games run their main exe from a different place (e.g. a shared engine runtime, Proton-like wrappers, Java games
  running `javaw.exe` from a system JRE). Rare, handled by the manual override.

**Bonus:** because this doesn't depend on *how* the game was started, the same scan can also detect games launched from Steam/GOG/Explorer
directly – i.e. tracking even when naLauncher didn't launch the game (only while naLauncher is running; see §4.4).

### 3.5 Steam's own "running" flag (cheap, exact for Steam)

While a Steam game runs, Steam writes:

- `HKCU\Software\Valve\Steam\RunningAppID` = appid of the running game (0 when none)
- `HKCU\Software\Valve\Steam\Apps\<appid>\Running` = 1

Reading a registry value every few seconds is essentially free; `RegNotifyChangeKeyValue` makes it event-driven.

- Exact start/end for all 21 Steam games without any folder resolution.
- Also catches Steam games started outside naLauncher.
- Requires the appid – we already have it in the `.url` (`steam://rungameid/<appid>`) and often in `Extensions["SteamAppId"]` (from metadata; usually the same id, but the URL one is authoritative).
- Non-Steam games added to Steam as shortcuts get large synthetic ids – irrelevant here.
- *(verify)* behaviour when Steam is in offline mode, and whether `Running` is reliably reset after a crash.

### 3.6 Launcher-recorded playtime (read after the fact)

Launchers keep their own counters. Useful for **backfilling history** and as a cross-check, not as live detection.

| Launcher | Where | Notes |
|----------|-------|-------|
| Steam | `<Steam>\userdata\<accountid>\config\localconfig.vdf` → `UserLocalConfigStore/Software/Valve/Steam/apps/<appid>`: `Playtime` (minutes), `LastPlayed` (unix time) | text VDF, needs a small parser; updated when a game exits. Steam Web API `GetOwnedGames` gives the same with an API key. |
| GOG Galaxy | `C:\ProgramData\GOG.com\Galaxy\storage\galaxy-2.0.db` (SQLite) – tables like `GameTimes`, `LastPlayedDates` | needs `Microsoft.Data.Sqlite`; schema is undocumented *(verify table/column names)* |
| Epic | no known local playtime store *(verify)* | |
| EA / Ubisoft | nothing practical | |

Two ways to use it:
- **One-time import** into a separate field (e.g. `GameInfo.ImportedPlayTime`), shown as "+ N h before naLauncher". Not as fake sessions – we don't know when they happened.
- **Steam as the source of truth** for Steam games: take Steam's total delta after each session. Accurate but makes the numbers inconsistent with our own tracking for other games.

### 3.7 Window focus and input idle (refines "running" into "active")

Add-ons on top of any detector above:

- **Foreground:** `GetForegroundWindow` → `GetWindowThreadProcessId` → is that PID one of the session's processes? Count time only while true.
- **Idle:** `GetLastInputInfo` gives ms since the last keyboard/mouse input (session-wide, no hooks needed). Stop counting after N minutes idle.
- **Gamepads:** `GetLastInputInfo` does **not** see XInput controller input. If you play with a pad, idle detection would cut sessions wrongly.
  Workaround: poll `XInputGetState` for each controller and watch `dwPacketNumber` change (cheap). DirectInput/other pads would need more.
- **Fullscreen exclusive games** can make focus checks flaky during alt-tab; tolerate short gaps.

This turns a session into "start, end, and *active* duration" – so it may need an extra field (e.g. `Session.ActiveDuration`) rather than just `End`.

### 3.8 Manual entry / correction

Whatever detection we choose, some sessions will be wrong (game crashed and lingered, PC left on overnight, launched outside naLauncher).
An editable session list in `GamePropertiesDialog` (edit end, delete session, add session) is cheap insurance and should be part of any solution.

---

## 4. Cross-cutting concerns

### 4.1 naLauncher closing while a game runs

Escape closes the window, and the app often won't be running for the whole session. Options:

1. **Persist the open session** – the session is saved immediately with `End = null` and a periodically updated `LastSeen` (every ~60 s).
   On next startup: if the game is still running (3.4 / 3.5 can re-find it; a job handle from 3.2 is gone, but the tree can be re-found by folder) –
   resume tracking; otherwise close it at `LastSeen`. Robust against crashes too.
2. **Don't exit while tracking** – Escape hides to the tray while a session is active. Simple, but changes app behaviour.
3. **Separate watcher process** – a tiny helper started at launch time that outlives naLauncher and writes the result. More moving parts,
   and concurrent writes to `library.json` need care.

Note: `End == null` currently means both "still running" and "never measured" (migrated). If we persist open sessions we need to tell them apart –
e.g. a `LastSeen` field (null for migrated sessions) or an explicit state.

### 4.2 Sleep / hibernate / clock changes

- Accumulate time in poll-sized steps and ignore any step much longer than the poll interval → time spent suspended isn't counted, no power-event handling needed.
- `DateTime.Now` is local time without offset (that's what the library stores today). Durations across a DST change are off by an hour.
  For new fields consider `DateTimeOffset` or UTC; existing `Start` values stay local.

### 4.3 Multiple games at once / relaunches

- Two games can run simultaneously (rare, but e.g. a game left open). Tracker must be per-session, not a single global.
- Relaunching the same game within a short window (crash → restart) – merge into one session or keep two? Probably merge if the gap < ~1 min.

### 4.4 Games started outside naLauncher

Only 3.4 and 3.5 can see these. Questions:
- Do we want them recorded at all? They'd create sessions and move the game in "Recent Games".
- They're only visible while naLauncher is running – full coverage needs naLauncher (or a watcher) to run at login, in the tray.

### 4.5 Performance and UI thread

All polling belongs on a background timer/task, never `CompositionTarget.Rendering`. Library mutations go back through the usual
`GameLibrary.Instance.Save()` + `RefreshAllSections()` on the dispatcher.

---

## 5. Coverage matrix (current library)

| Technique | Direct exe (69) | DOSBox `.cmd` (12) | Steam `.url` (21) | GOG Galaxy (13) | EA/Epic (3) | Outside naLauncher | Effort |
|-----------|:---:|:---:|:---:|:---:|:---:|:---:|:---:|
| 3.1 wait on process | ⚠ bootstrappers | ✅ | ❌ | ❌ | ❌ | ❌ | XS |
| 3.2 Job Object | ✅ (⚠ elevated) | ✅ | ❌ | ❌ (job would hold Galaxy) | ❌ | ❌ | M |
| 3.3 parent-PID polling | ⚠ fast bootstrappers | ✅ | ❌ | ❌ | ❌ | ❌ | M |
| 3.4 install folder | ✅ (⚠ shared runtimes) | ❌ shared `dosbox.exe` | ✅ via acf | ✅ via `/path` | ⚠ Epic ✅, EA manual | ✅ while running | M–L |
| 3.5 Steam registry | – | – | ✅ exact | – | – | ✅ | S |
| 3.6 launcher data | – | – | ✅ totals only | ✅ totals only | ❌ | ✅ totals only | S–M |
| 3.7 focus/idle | refinement for all | | | | | | S (+gamepad M) |
| 3.8 manual edit | correction for all | | | | | | S |

---

## 6. Candidate solutions

### A. Minimal
3.1 + "suspiciously short session → discard end" + 3.5 for Steam.
- ~1 day of work, no launch-path changes.
- Leaves GOG/EA/Epic and bootstrapper games unmeasured.

### B. Hybrid tracker (recommended starting point)
A `SessionTracker` that picks a detector per game:
1. Steam URL → 3.5.
2. Launcher-mediated with a resolvable folder (GOG, Epic, manual) → 3.4.
3. Everything else → 3.2 (Job Object); if the job empties within ~30 s and the game has a non-shared folder → fall back to 3.4.
4. Common rules: wait up to ~2–3 min for the game to appear; end after ~15–30 s with nothing detected; persist open session (§4.1 option 1).
5. Plus 3.8 manual correction.
- Covers essentially the whole current library.
- Medium effort; most of the risk is in `.lnk` resolution and the helper-process exclusion list.

### C. Always-on watcher
B, plus naLauncher starting at login into the tray and scanning all known install folders + Steam registry continuously.
- Captures play started anywhere.
- Changes the app's nature (resident process), needs tray UI and a decision on §4.4.

Orthogonal add-ons to any of the above: 3.6 import of Steam/GOG history, 3.7 active-time refinement.

---

## 7. Decisions needed

1. **Running time vs active time** (§2)? If active – do you play with a gamepad (affects idle detection)?
2. **Behaviour when naLauncher is closed during play** (§4.1): persist + reconcile, hide to tray, or watcher process?
3. **Track games launched outside naLauncher** (§4.4)? If yes – should naLauncher run at login?
4. **OK to change how games are launched** (`CreateProcess` + Job Object instead of `ShellExecute`) for direct shortcuts?
5. **Import historical playtime** from Steam/GOG (adds a SQLite dependency for GOG)?
6. **Data model additions** depending on the above: `Session.LastSeen`, `Session.ActiveDuration`, `GameInfo.InstallDir`, `GameInfo.ImportedPlayTime`, maybe a "Play time" sort mode.
