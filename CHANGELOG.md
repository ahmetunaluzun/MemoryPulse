# Changelog

All notable changes to MemoryPulse are listed here. Versions follow [Semantic Versioning](https://semver.org/).

## [1.9.0] - 2026-11-18

- Sort the program list by clicking a column header. *Processes*, *CPU* and *Disk I/O* list the 25 programs highest in that column; *Program* sorts by name; *RAM* is the usual list. A second click on the same header reverses the order, and Windows' sort arrow shows which column is used. The list's title says what the programs were chosen by, and the choice is kept after a restart.
- Microsoft Store edition: *Clear cache* is now available. Clearing the cache needs administrator rights, so Windows asks for them (UAC) each time; the rest of the app keeps running without them. Clearing the cache automatically at the critical threshold stays in the GitHub edition only, because it never asks through UAC.
- The main window fits the screen it opens on: about 80% of the screen's height, centred, so the program list gets the room (before, it opened at a fixed 700×970 and nearly filled a 1080p screen). On a short screen it takes the whole height and scrolls.
- Settings moved to their own window, opened with *Settings…* under the program list; *Open log* is in that window now. The window has *Save* and *Cancel*.
- Fixed: when the cache was cleared through UAC, the app could say "The cache could not be cleared" although it had been cleared. The helper that does the clearing crashed while closing, after its work was done, and that was read as a failure.

## [1.8.2] - 2026-11-17

- The program list also shows programs that are busy on the CPU but small in RAM. Before, the list held only the 25 programs using the most RAM, so a program using a lot of CPU with little memory could be missing. Now up to 5 such programs (1% CPU or more) are listed below the 25. The list's tooltip says how the list is made.

## [1.8.1] - 2026-11-17

- Sizes right at a rounding edge are shown the same way as the others: a drive with 99.97 GB free now reads "100 GB free", not "100.0 GB", and 1023.97 MB reads "1.0 GB", not "1024 MB".

## [1.8.0] - 2026-11-16

- CPU in the main window: a line above the memory cards with the CPU use of the whole PC (all cores together, as in Task Manager) as a number and a bar, orange above 90%. Refreshed every 5 seconds.
- The program list has a *CPU* column: the share of the whole processor each program uses right now, all of its processes together.
- Usage history records CPU every minute: the whole PC and each recorded program. The programs using the most CPU are recorded too, even when they are small in RAM. *History* has a CPU chart and the highest one-minute CPU in the summary; the program table has *Average CPU* and *Peak CPU*; a program's *History (24 hours)* tab has a CPU chart. Days recorded before 1.8.0 have no CPU and show "—".
- Clearer Turkish names: the *Commit* card is now *Ayrılan bellek*, and *Süreç sayısı* is now *İşlem sayısı*, the word Task Manager uses. The program list's tooltip explains what the count means: how many processes the program runs as (Chrome, for example, starts one for each tab).
- The History window is wider and the program details window taller, to fit the new columns and chart.

## [1.7.1] - 2026-11-12

- The RAM shown for each program is now its private working set: the memory that belongs to that program alone, the same as the *Memory* column in Task Manager. Before, it was the full working set, which also counts memory shared with other programs (such as common DLLs) once in every program that uses it, so the list could add up to more than the RAM in use. This applies to the program list, the tray tip, the program details window and the usage history.
- Below the program list, one line splits the memory in use: programs (all of them, not just the listed ones), Windows, drivers and shared memory, and the total in use.
- Usage history recorded before 1.7.1 keeps the old (full working set) values, so program RAM in older days can look higher than in newer ones.

## [1.7.0] - 2026-11-12

- Disks in the main window: one bar per drive with free and total space (amber below 10% free, red below 5%), and live disk activity for the whole PC: read speed, write speed and active time, refreshed every 5 seconds.
- The program list has a *Disk I/O* column: how fast each program is reading and writing right now. Windows counts all file operations here, including reads answered from the cache, so it can be higher than the physical disk activity.
- New *Disk space* window (button in the window and tray menu): scan a drive or a folder to see which folders take the room, as a tree sorted by size with each folder's share of its parent, file and folder counts and last change; the 100 largest files; and the file types that take the most space. Right-click to open a folder in Explorer, copy its path or scan again from it. The scan only reads (nothing is deleted or moved), runs at low priority, does not follow links (junctions, symbolic links) and counts the folders it was not allowed to read. Nothing it finds is saved.
- The main window is a little wider and taller, and scrolls when the screen is too small for it.

## [1.6.0] - 2026-11-11

- Usage history. Every minute MemoryPulse records available RAM, each program's memory and read/write (I/O), disk and network speed for the whole PC and free disk space, and keeps 7 days (files older than that are deleted). Turn it off with *Record usage history* in the settings.
- New *History* window (button in the window and tray menu): charts for the last hour, 6 hours, 24 hours, 7 days or a custom range; a table of programs with average and peak RAM, when the peak was, read/write and network totals; and the crashes, hangs, blue screens, unexpected shutdowns and restarts Windows logged in that range, also marked on the charts.
- Network usage per program: right-click a program → *Watch network usage*. GitHub edition only, while running as administrator (*Start with Windows* starts it that way).
- The details window has a *History (24 hours)* tab with that program's memory, read/write totals and its crashes and hangs. Right-click a program → *History* opens it.

## [1.5.0] - 2026-11-10

- Microsoft Store edition. The same exe, started from the Store package, works without administrator rights, as the Store requires: it does not clear the cache, has no *Check for updates* (the Store updates it) and makes no network connections. *Start with Windows* uses the Store's startup setting, which you can also manage in Task Manager → Startup apps. The GitHub edition is unchanged.
- `tools/make-msix.ps1` builds the Store package; privacy policy in `PRIVACY.md`.

## [1.4.0] - 2026-11-09

- *Check for updates* now checks in the background instead of opening the browser: it asks GitHub for the latest version number and tells you whether you are up to date. If a newer version exists, it offers to open the download page.
- This request is made only when you click; MemoryPulse still never connects anywhere on its own.

## [1.3.0] - 2026-11-09

- Removed the automatic new version check added in 1.2.0. MemoryPulse again makes no network connections at all.
- Instead, *Check for updates* in the window and the tray menu opens the releases page in your browser, only when you click it. To get an e-mail about new releases, use *Watch → Custom → Releases* on the GitHub page.
- The window is back to its previous size.

## [1.2.0] - 2026-11-09

- New version notice: once a day MemoryPulse asks GitHub for the latest version number. When a new release is out it shows a notification (once per release), a download item at the top of the tray menu and a link at the bottom of the window. Nothing about your PC is sent. Turn it off with *Check for new versions* in the settings; then MemoryPulse makes no network connections at all.
- The window is slightly taller so the program list keeps its size with the new setting.
- Details window: "1 process" instead of "1 processes" when a program has a single running copy.

## [1.1.1] - 2026-11-09

- Windows' own kernel processes (Memory Compression, System, Registry, Secure System, Idle, vmmem) no longer show a misleading "not readable (needs administrator rights)" in the details window. The window now explains what the process is, for example that Memory Compression holds compressed pages that would otherwise go to disk.

## [1.1.0] - 2026-11-09

- Program details: double-click a program in the list (or right-click → Details) to see each running copy with its file location, description, publisher, working folder and memory use.
- For script hosts and shared runtimes (node, python, java, PowerShell, WebView2, svchost and similar) the command line is shown too, so you can tell which script, app or Windows service is using the memory. Values that look like passwords, tokens or keys are hidden.
- "Open file location" in the details window and in the list's right-click menu.
- The selected program stays selected when the list refreshes.

## [1.0.0] - 2026-11-09

First release.

- Live memory view: available, cached (standby + modified, matches Task Manager), free, modified, commit, total, with explanations.
- Programs using the most RAM, grouped by name.
- Tray icon colored by state (warning and critical thresholds) and a notification at the critical threshold listing the top programs.
- Cache (standby list) clearing: manual with an explanation, or automatic at the critical threshold.
- Start with Windows (scheduled task, approved once).
- English and Turkish, following the Windows language by default.
- No network access, no telemetry; settings and log in `%APPDATA%\MemoryPulse\`.
