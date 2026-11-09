# Changelog

All notable changes to MemoryPulse are listed here. Versions follow [Semantic Versioning](https://semver.org/).

## [1.0.0] - 2026-11-09

First release.

- Live memory view: available, cached (standby + modified, matches Task Manager), free, modified, commit, total, with explanations.
- Programs using the most RAM, grouped by name.
- Tray icon colored by state (warning and critical thresholds) and a notification at the critical threshold listing the top programs.
- Cache (standby list) clearing: manual with an explanation, or automatic at the critical threshold.
- Start with Windows (scheduled task, approved once).
- English and Turkish, following the Windows language by default.
- No network access, no telemetry; settings and log in `%APPDATA%\MemoryPulse\`.
