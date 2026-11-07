# Mistakes & fixes

Build log for Apps Manager. Each entry: what went wrong, what fixed it.

> Historical note: Chocolatey and Windows 7/8 support were removed later. The app is now
> winget-only on Windows 10 1809+; sections 11 and 14 below are kept as history.

## 1. Assumed winget was available

I checked `where winget` / the `WindowsApps` alias first and found nothing, but a first
draft of the plan treated updates as always present. Reality: this machine has no App
Installer, so every update path had to degrade gracefully.

Fix: `WingetService.FindWinget()` returns null and `CheckUpdates()` returns `"notfound"`;
the UI shows "winget (App Installer) is not installed — update checking is unavailable"
instead of an error dialog. Update/Check buttons stay usable; nothing throws.

## 2. First compile failed: CS0051

`public HostBridge(MainForm form)` exposed the internal `MainForm`.

Fix: constructor made `internal`. The class and its script-visible methods stay `public`
(COM/IDispatch needs that); only the constructor is internal.

## 3. WebBrowser defaults to IE7 mode

The control renders in IE7 quirks by default, where `JSON` does not exist, so the whole
page would fail before drawing a row. This is the trap when hosting HTML in WinForms.

Fix: `FEATURE_BROWSER_EMULATION` = 11000 for the exe name under HKCU (no admin) in
`Program.cs`, verified at runtime. The page itself is still written for IE8 (CSS 2.1,
ES3) so the registry tweak is only about the standards box model and JSON.

## 4. Details pane had a permanent horizontal scrollbar

`#details` had `overflow:auto` and the inner `.pad` added 14px padding on each side to a
100%-wide table, so the content was always a little wider than the pane.

Fix: padding moved to `#details` itself, `.pad` padding set to 0,
`overflow-x:hidden`, and the pane raised from 132px to 152px because the last rows
(Uninstall, Registry) were cut off.

## 5. Sort comparator had a duplicated branch

While adding header sorting I wrote the same if/else twice (size vs. everything else).

Fix: collapsed to one "unknown values sink to the bottom" block. Sorting is stable on
ties via a name comparison.

## 6. Screenshot/click test hit the wrong window

The first UI check captured the user's browser instead of the app: PowerShell is not
DPI-aware, `GetWindowRect` returned virtualized coordinates, and my synthetic clicks
landed outside the form. Nothing in the app was wrong; the test harness was.

Fix: `SetProcessDPIAware()` + `SetForegroundWindow()` before measuring, then click and
capture using physical pixels. After that the click-selection, details pane and
"sort by Size" all verified correctly.

## 7. Version column truncates long versions

The fixed column keeps the grid readable, but versions like `36.0.39.57346` show as
`36.0.39.573...`. Not a bug, but a usability miss.

Fix: `title` attribute on the version cell (and on name/publisher) so the full value
appears as a tooltip. Details pane always shows the full version.

## 8. Selection was too pale and the hover rule beat it

The first selection colour (`#dcebf8`) was hard to spot, and the modern "row hover"
rule (`.grid tbody tr:hover td`) has higher specificity than `.grid tr.sel td`, so
hovering a selected row washed the selection out.

Fix: selection is now solid `#2a6b9f` with white text (muted text and badge recoloured
for contrast), plus `.grid tbody tr.sel:hover td` inside the modern-only block so hover
cannot override it. Verified: RGB(42,107,159) across every cell of the selected row.

## 9. Right-click menu had to work without the WebBrowser menu

`IsWebBrowserContextMenuEnabled = false` only removes the IE menu; it does not give us
one. A native WinForms ContextMenuStrip would sit outside the page and would not know
which row was under the cursor.

Fix: a page-owned `<div id="menu">` positioned with `position:fixed` at `clientX/clientY`,
clamped to the viewport, hidden on click/Esc/scroll/resize. Items reuse the toolbar
actions, so there is one code path per action. Verified end to end: right-click selects
the row and "Copy install path" put `C:\Program Files\AMD\CIM\BIN64` on the clipboard.

## 10. Build script deleted half of `bin` when the app was still running

While rebuilding after the menu change, `AppsManager.exe` was still open. `rd /s /q bin`
deleted `app.html`, failed on the locked exe, then `mkdir bin` failed as well and the
script stopped with an unhelpful message. The folder was left without `app.html`, so a
stale exe would have run without its UI.

Fix, first attempt: verify that `bin` was removed and otherwise stop with "Close
AppsManager.exe and try again". That still deleted whatever it could before failing.

Fix, second attempt (current): check `tasklist` for a running `AppsManager.exe` **before**
touching anything, and never delete the output folder at all. `csc` overwrites the exe,
`app.html` is copied over. A failed build now leaves the last working build in place
instead of an empty folder.

## 11. winget is not an option on Windows 7

winget only exists from Windows 10 1809 on (App Installer), so "just install winget"
is not a Windows 7 answer. Chocolatey is the CLI package manager that still supports
Windows 7 SP1 (it needs .NET Framework 4.x and PowerShell 3+; WMF 5.1 recommended).

Decision: keep winget first, fall back to Chocolatey when `choco.exe` is found
(`ChocolateyInstall\bin\choco.exe`, `%ProgramData%\chocolatey\bin\choco.exe` or PATH).
Scoop was considered and skipped: smaller package coverage and a mostly per-user model,
so it would add a third provider for less.

Two consequences handled in code:
- Chocolatey needs admin, so the upgrade is started with `Verb = "runas"` (one UAC
  prompt) and we can only read an exit code, not the output.
- Chocolatey package ids are not ARP display names ("7zip" vs "7-Zip 26.03 (x64)").
  A pure prefix match would let package `git` claim "GitHub Desktop", so matching also
  requires the installed version to be equal (or a dotted prefix). Plain prefix matching
  was rejected on purpose; unusual version schemes will miss instead of updating the
  wrong program.

## 12. Hard-coded IE11 emulation would have broken Windows 7 + IE8

`FEATURE_BROWSER_EMULATION` was set to 11000 unconditionally. On a Windows 7 machine that
only has IE8, 11000 is not a valid mode: the WebBrowser control ignores it and falls back
to its default (IE7), where `JSON` does not exist and the whole page dies before the first
row. Windows 7 with IE8 is exactly the case this build is meant to support.

Fix: read the installed IE version (`svcVersion`/`Version`) and clamp the value to
11000 / 10000 / 9000 / 8000. The value is written to every relevant registry view (a
32-bit exe on 64-bit Windows uses the 32-bit view). The page itself stays IE8-safe, so the
clamped mode renders the same UI.

## 13. x86 build produced CS1607 warnings

`build.cmd x86` passed `/platform:x86` to the 64-bit `csc.exe`, which references the
64-bit framework assemblies; the compiler warned six times that those assemblies "target a
different processor".

Fix: the script now picks `Framework\v4.0.30319\csc.exe` (32-bit compiler, 32-bit
references) for x86 builds and `Framework64` otherwise, falling back if a path is missing.
Verified: `bin\x86\AppsManager.exe` runs as a 32-bit process on this 64-bit PC and renders
the full list, while `bin\AppsManager.exe` (AnyCPU) runs as 64-bit.

## 14. "Bundling Chocolatey" without shipping PowerShell

Copying only `choco.exe` from the package does not work: Chocolatey needs its
`helpers\`, `tools\` (7z, shimgen, checksum), the redirect shim and a machine-wide
`ChocolateyInstall` variable and PATH entry.

First attempt: bundle the official `install.ps1` and run it elevated with
`-ChocolateyDownloadUrl <nupkg> -UseNativeUnzip`. That works, but it makes the app depend
on Windows PowerShell and on the execution policy. It also ties the setup to a script
whose parameters can change.

Current approach: do what the script does, in C#. The nupkg already contains the complete
`tools\chocolateyInstall` tree, so the app:
1. starts a second copy of itself elevated (`AppsManager.exe /install-chocolatey`),
2. unzips the nupkg with `ZipFile.ExtractToDirectory`,
3. copies `tools\chocolateyInstall\*` to `%ProgramData%\chocolatey` and its
   `redirects\*` to `%ProgramData%\chocolatey\bin`,
4. creates `lib`, `lib-bad`, `lib-bkp`, `logs`, `temp` and puts the nupkg in
   `lib\chocolatey\chocolatey.nupkg`,
5. writes the machine `ChocolateyInstall` variable and appends the bin folder to the
   machine PATH (plus a `WM_SETTINGCHANGE` broadcast),
6. verifies with `choco.exe -v` and exits with a code the parent reads.

No PowerShell process and no install script are involved. The elevation is the app's own
executable, so the UI stays in one place and the result is a plain exit code.

Still true: Chocolatey runs package scripts with PowerShell (3+), so a machine with only
PowerShell 2.0 can have Chocolatey installed but not upgrade packages. That is a
Chocolatey requirement, not something this installer can remove.

Not done on purpose: no download from chocolatey.org at click time (works offline), no
silent background install (UAC must be visible), no auto-upgrade of Chocolatey itself
(`choco upgrade chocolatey` remains the official route), no ACL tweaks (the folder
inherits `%ProgramData%` permissions).

## 15. Button icons vanished on hover

The buttons gained 16×16 icon backgrounds with `.b-*` classes, but the existing
`.btn:hover`, `.btn:active` and `.btn.primary` rules used the `background` shorthand
(`background: #eef4fb`). A background shorthand also resets `background-image`, and those
selectors have higher specificity than `.b-refresh`/`.b-uninstall`, so every button icon
disappeared the moment the mouse touched it (and the primary button never showed its
white refresh icon).

Fix: those rules now set `background-color` only. The same rule applies to any future
state that wants to keep an icon.

## 16. Context-menu icons tiled on hover

The same shorthand bug as #15, one level deeper. `.mi` sets `background-repeat: no-repeat`
and `background-position: 12px center`, while the icon itself comes from an ID selector
(`#mi-uninstall`). The hover rule was `.mi.hov { background: #eef4fb; }`: the shorthand
reset repeat/position to `repeat`/`0 0`, and `.mi.hov` beats `.mi` on specificity. The ID
kept the image, so the icon did not vanish — it tiled across the row.

Fix: `.mi.hov { background-color: #eef4fb; }`.

## Lessons worth keeping

- Compile early with the real `csc.exe`. Two C# 5-only constraints (no string
  interpolation, no `?.`) shaped the code from the start instead of after a failed build.
- IE8 rules are cheap to follow when the HTML is written that way from line one:
  no flex, no grid, absolute panels, table grid, border triangles for the sort arrows.
- Changing the machine (uninstall/update) is not a test. Those two paths are documented
  as untested instead of being "proven" by a destructive click.
