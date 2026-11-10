# Changelog

All notable changes to MemoryPulse are listed here. Versions follow [Semantic Versioning](https://semver.org/).

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
