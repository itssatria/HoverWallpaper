# Changelog

All notable changes to Hover Wallpaper are listed here. The newest release is first.

## v1.0 - 2026-11-05

### Added
- **Per-image fit options.** Image 1 and Image 2 each get their own fit: Fill, Fit, Stretch, Tile, Center, or Custom crop.
- **Custom crop editor.** Drag a crop rectangle over the image. The rectangle is locked to your screen's shape, so the result is not distorted. Choosing a new file resets that image's crop.
- **Close to tray.** Closing the settings window keeps the wallpaper running in the system tray. Use the tray menu to open the settings or exit.
- **Single instance.** Starting the app again opens the settings of the running copy instead of starting a second one.
- Fit and crop settings are saved in `settings.json`.
- The tray icon now uses the app icon.
- Changelog.

### Fixed
- **Apply did nothing on Windows 11 24H2 and newer.** On these builds the desktop icons are a child of `Progman`, not of a separate `WorkerW` window, so the wallpaper window was attached to the wrong place. It now attaches to `Progman`, directly under the icons.
- **The wallpaper window was never painted.** Windows does not draw a plain child window there. The window is now layered, so it shows up.
- **Start with Windows was registered incorrectly.** The registry key path and the quoted command were written with doubled backslashes, so the startup entry did not point to the app.
- The settings preview shows the hover zone over Image 1 and reports image loading errors in the window.

### Changed
- Images are no longer always stretched. New installs start with **Fill**, which keeps proportions. Choose **Stretch** to get the old behavior.
- The settings window and the tray are managed by one object, so Stop and Apply work the same whether the app started normally or from Windows startup.

### Known limitations
- One hover zone only.
- Image switching is instant, with no fade.
- Fit options and crops apply to the whole virtual desktop, not to each monitor.
- Tested on a single-monitor Windows 11 machine only.
- The installer is not code signed, so SmartScreen can show a warning.

## v0 - 2026-11-05

First prototype.

- Two-image wallpaper: Image 1 is shown normally, Image 2 is shown while the cursor is inside the hover zone.
- One rectangular hover zone, set as percentages of the virtual desktop.
- Visual hover-zone editor with drag-to-select, presets and a live hover test.
- Settings saved to `%LOCALAPPDATA%\HoverWallpaper\settings.json`.
- Optional launch when you sign in to Windows, running in the background with a tray icon.
- Self-contained Windows x64 build script and Inno Setup installer.
- GitHub Actions workflow that builds the installer.
