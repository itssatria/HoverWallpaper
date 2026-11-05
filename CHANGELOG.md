# Changelog

All notable changes to Hover Wallpaper are listed here. The newest release is first.

## Unreleased

### Added
- **Multiple hover zones.** Settings now starts with one hover zone and has an **Add hover zone** button; each zone gets its own image, fit, crop and screen area, and can be removed again (up to 12 zones). Entering a zone reveals that zone's image; leaving all zones restores Image 1. When zones overlap, the first zone in the list wins. Each zone preview shows Image 1 with that zone's rectangle.
- **Fade animation toggle and duration sliders.** Preferences now has a **Fade animation between images** switch and separate **Fade in** / **Fade out** sliders (0–2000 ms, 0 = instant), saved to `settings.json`. The wallpaper cross-fades with a smooth ease instead of switching instantly, and a fade reversed mid-way starts from the current blend instead of jumping.
- **Saved presets.** A new **Presets** section saves the whole scene — Image 1 path, fit and crop, every hover zone's image, fit, crop and X/Y/width/height, plus the fade toggle and durations — under a name. Presets can be loaded back into the editor or deleted, and are stored in `%LOCALAPPDATA%\HoverWallpaper\presets.json`. Startup-at-sign-in is deliberately excluded because it is an app preference, not a scene. Loading a preset does not touch the desktop until **Apply**; saving over an existing name asks first, and saving with missing image paths confirms before continuing.

### Changed
- **Settings layout.** Image 2's fixed card became a per-zone card list, so the settings window scrolls through zones instead of packing two images into fixed side-by-side cards.
- **Settings migration.** Existing `settings.json` files are read as a single hover zone (`Image2`/`X`/`Y`/`Width`/`Height`/`Fit2`/`Crop2`) and are upgraded to the new `Zones` list on the next save. The legacy fields are no longer written.

## v1.0 - 2026-11-05

### Added
- **Windows 11 Fluent settings window.** Replaced the plain settings form with a Mica-backed Fluent UI and thumbnail cards for both wallpaper images; click a preview or Choose image to browse.
- **Per-image fit options.** Image 1 and Image 2 each get their own fit: Fill, Fit, Stretch, Tile, Center, or Custom crop.
- **Custom crop editor.** Drag a crop rectangle over the image. The rectangle is locked to your screen's shape, so the result is not distorted. Choosing a new file resets that image's crop.
- **Close to tray.** Closing the settings window keeps the wallpaper running in the system tray. Use the tray menu to open the settings or exit.
- **Single instance.** Starting the app again opens the settings of the running copy instead of starting a second one.
- Fit and crop settings are saved in `settings.json`.
- The tray icon now uses the app icon.
- App logo at the top of the settings window. The logo and icon are built into the exe.
- Changelog.

### Fixed
- **The hover-zone preview ignored the fit setting.** The settings preview and the visual zone editor now show Image 1 and Image 2 with their fit and crop applied, so the zone you draw matches the real wallpaper.
- **Buttons were off-screen at 125% display scaling.** Apply crop, Apply Selection and Cancel in the crop and zone editors are now docked to the right edge and always visible.
- **Build script could not find Inno Setup** when it was installed per user by winget. `build.ps1` now also checks `%LOCALAPPDATA%\Programs`, `Program Files` and `PATH`.
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
