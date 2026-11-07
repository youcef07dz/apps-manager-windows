# Apps Manager — task list

## Ideas to add

Small, no new dependencies:

- [x] Missing-install flag: red "Missing" pill and filter for rows whose install folder no longer exists
- [x] Duplicate-version flag: same name with more than one version, listed in details
- [x] Run program from the list (context menu), using `DisplayIcon` / the exe
- [x] F5 refreshes; double-clicking a row opens its folder
- [x] Status bar shows "cached · rescanning…" while the startup background scan runs
- [x] Follow the Windows light/dark setting on first run (the user's Dark/Light choice still wins)
- [x] Update log panel ("Show log") keeps the whole session, not just the last command

Medium:

- [x] Ignore/pin an update per app so "Update all" skips it
- [x] Publisher filter dropdown next to Search
- [x] Sizes: run for all rows on demand, with progress and cancel (keep it manual so idle CPU stays low)
- [ ] MSIX/Store apps (needs WinRT; separate uninstall path)
- [x] Startup impact: "starts with Windows" from the Run keys and the Startup folder

## Skipped on purpose

Silent uninstall, bulk uninstall, leftovers removal, an app-store/browse view, Scoop, telemetry,
self-updater, Windows 7/8 support, remembering the last tab.
