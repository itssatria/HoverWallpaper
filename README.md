<div align="center">

<img src="HoverWallpaper/Assets/HoverWallpaper.png" alt="Hover Wallpaper app icon" width="112" />

# Hover Wallpaper

### A desktop wallpaper that reacts to your mouse.

Set a normal wallpaper, define a hover zone, and reveal a second image whenever your cursor enters that area.

[![Windows 11](https://img.shields.io/badge/Platform-Windows%2011-0078D4?logo=windows11&logoColor=white)](#requirements)
[![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&logoColor=white)](#requirements)
[![Architecture](https://img.shields.io/badge/Architecture-x64-64748b)](#build-from-source)
[![License](https://img.shields.io/badge/License-Not%20specified-lightgrey)](#license)

**Hover Wallpaper** is a lightweight Windows desktop experiment built around one simple idea: your wallpaper can have an interactive area.

</div>

---

## ✨ Features

| Feature | Description |
|---|---|
| **Two-image wallpaper** | Choose **Image 1** for the normal desktop and **Image 2** for the hover effect. |
| **Visual zone editor** | Interactive canvas with drag-to-select, presets, and live hover testing. |
| **Per-image fit options** | Choose how each image fills the desktop: Fill, Fit, Stretch, Tile, Center, or a custom crop. |
| **Custom crop editor** | Drag a crop rectangle over the image. It is locked to your screen's shape, so the result never looks distorted. |
| **Mouse-triggered reveal** | Image 2 appears when the cursor enters the configured rectangular area. |
| **Automatic restore** | Leaving the hover area switches back to Image 1. |
| **Fade animation** | Optionally cross-fade into and out of Image 2, with independently adjustable fade-in and fade-out durations. |
| **Configurable hover zone** | Set the area's X position, Y position, width, and height as percentages of the virtual desktop or draw it visually. |
| **Close to tray** | Closing the settings window keeps the wallpaper running in the system tray. Use the tray menu to open settings or exit. |
| **Single instance** | Launching the app again opens the settings of the running copy instead of starting a second one. |
| **Common image formats** | Supports JPG, JPEG, PNG, BMP, and WebP file selection. Actual decoding depends on Windows image codec support. |
| **Windows startup** | The installer can register the app to start when the current Windows user signs in. |
| **System tray mode** | Startup mode runs the wallpaper in the background with a tray icon, an Open command, and an Exit command. |
| **Self-contained publishing** | The build script publishes a Windows x64 app with the .NET runtime bundled. |
| **Installer packaging** | Inno Setup creates a Windows installer with the custom app icon and shortcuts. |

## 🖼️ How it works

1. **Choose Image 1** — the regular wallpaper shown by default.
2. **Choose Image 2** — the alternate image revealed by the hover interaction.
3. **Pick a fit for each image** — Fill, Fit, Stretch, Tile, Center, or Custom crop.
4. **Define the hover zone** — position and size are set using percentages of the full virtual desktop.
5. **Apply** — the app displays Image 1 and monitors the mouse position.
6. **Hover** — when the cursor enters the zone, the app displays Image 2. When it leaves, Image 1 returns.
7. **Close the window** — the wallpaper keeps running from the system tray.

### Fade animation

**Preferences → Fade animation between images** cross-fades between the two images instead of switching instantly. **Fade in** is how long Image 2 takes to appear when the cursor enters the hover area; **Fade out** is how long Image 1 takes to return when it leaves. Each slider goes from 0 to 2000 ms; 0 means instant.

### Fit options

| Option | What it does |
|---|---|
| **Fill** | Scales the image to cover the whole desktop and keeps its proportions. Edges that do not fit are cropped. This is the default. |
| **Fit** | Shows the whole image with its proportions kept. Black bars fill any leftover space. |
| **Stretch** | Stretches the image to the desktop size and ignores its proportions. |
| **Tile** | Repeats the image at its original size. |
| **Center** | Places the image at its original size in the middle of the desktop, on a black background. |
| **Custom crop** | Opens the crop editor. Drag outside the rectangle to draw a new area, or drag inside it to move it. The rectangle keeps your screen's shape. |

Each image has its own fit, so Image 1 can be cropped while Image 2 is stretched. Choosing a new file resets that image's crop.

### Hover zone coordinates

The hover zone is a rectangle measured against the virtual desktop:

- `X` — distance from the left edge, in percent.
- `Y` — distance from the top edge, in percent.
- `Width` — rectangle width, in percent.
- `Height` — rectangle height, in percent.

For example, `X = 60`, `Y = 30`, `Width = 25`, and `Height = 35` places a rectangular trigger area toward the right side of the desktop.

> **Tip:** Use two images with the same dimensions and composition for the cleanest visual switch. If the two images use different fit options, the picture can shift when the cursor enters the zone.

## 📦 Project structure

```text
HoverWallpaper/
├── .github/
│   └── workflows/
│       └── windows-installer.yml   # GitHub Actions Windows build
├── HoverWallpaper/
│   ├── Assets/
│   │   ├── HoverWallpaper.ico      # Windows executable / installer icon
│   │   └── HoverWallpaper.png      # Repository / README icon
│   ├── HoverWallpaper.csproj       # .NET Windows Forms project
│   ├── Program.cs                  # Entry point, settings, settings UI, wallpaper window
│   ├── TrayApp.cs                  # Tray icon, wallpaper lifetime, single-instance handling
│   ├── ImageFit.cs                 # Fit modes (fill, fit, stretch, tile, center, crop)
│   ├── CropEditorForm.cs           # Drag-to-crop editor for the custom crop option
│   ├── ZoneEditorForm.cs           # Visual drag-to-select zone editor & live preview
│   └── app.manifest                # Windows application manifest
├── installer/
│   └── HoverWallpaper.iss         # Inno Setup installer definition
├── build.ps1                       # One-command Windows build script
├── .gitignore
├── CHANGELOG.md                    # Release history
└── README.md
```

## 🧰 Requirements

### To run the installed application

- Windows 11, 64-bit.
- A supported Windows desktop environment using Explorer.

The published installer is configured as self-contained, so end users do **not** need to install the .NET runtime separately.

### To build the project from source

Install these tools on a Windows 11 x64 machine:

1. **.NET 8 SDK** (required for compiling code; note that the *.NET Runtime* alone is not sufficient)
   - Via PowerShell:
     ```powershell
     winget install Microsoft.DotNet.SDK.8
     ```
   - Or direct installer: **[.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)**
2. **Inno Setup 6** (required for creating the setup installer)
   - Via PowerShell:
     ```powershell
     winget install JRSoftware.InnoSetup
     ```
   - Or direct installer: **[Inno Setup 6](https://jrsoftware.org/isinfo.php)**
3. **[Git](https://git-scm.com/downloads)** (optional, for cloning the repository)

> **Note:** After installing via `winget`, close and reopen your PowerShell terminal so your environment `PATH` refreshes. You can verify the SDK is detected by running:
> ```powershell
> dotnet --list-sdks
> ```

## 🚀 Build and run from source

### 1. Get the source

Clone your repository:

```powershell
git clone https://github.com/itssatria/HoverWallpaper.git
cd HoverWallpaper
```

Or download the repository ZIP from GitHub and extract it.

### 2. Quick run (Development / Testing)

To test and run the app directly without creating an installer:

```powershell
dotnet run --project .\HoverWallpaper\HoverWallpaper.csproj
```

### 3. Build the standalone installer

To package the app into a self-contained single-file `.exe` installer:

Open PowerShell in the project root and run:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\build.ps1
```

The script will:

1. Clean previous `publish` output.
2. Publish the app in Release mode for `win-x64` (`--self-contained true -p:PublishSingleFile=true`).
3. Locate Inno Setup 6 (`ISCC.exe`) and compile the installer.

### 4. Find the installer

When the build completes successfully, the installer will be created here:

```text
installer/Output/HoverWallpaper-Setup-1.0.0.exe
```

> **Troubleshooting:**
> - If you get `No .NET SDKs were found`, you only have the .NET Runtime installed. Install the **.NET 8 SDK** using the command above.
> - If you get `Inno Setup 6 not found`, verify Inno Setup 6 is installed at its default location (`${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe`) or update `$iscc` in `build.ps1`.

## ⚙️ GitHub Actions: build on GitHub

This repository includes a workflow at `.github/workflows/windows-installer.yml`.

The workflow runs on a Windows GitHub-hosted runner and:

- Installs .NET 8.
- Installs Inno Setup.
- Runs `build.ps1`.
- Uploads `HoverWallpaper-Setup-1.0.0.exe` as a workflow artifact.

### Run it manually

1. Push the project to a GitHub repository.
2. Open the repository's **Actions** tab.
3. Select **Build Windows installer**.
4. Choose **Run workflow**.
5. When the workflow finishes, open its run summary and download the `HoverWallpaper-Windows11-Installer` artifact.

### Build when creating a version tag

Push a tag beginning with `v` to trigger the same workflow:

```powershell
git tag v1.0.0
git push origin v1.0.0
```

The workflow currently uploads the installer as a GitHub Actions artifact. It does not automatically publish a GitHub Release.

## 🪟 Installation and usage

1. Run `HoverWallpaper-Setup-1.0.0.exe`.
2. Follow the installer.
3. Optionally select:
   - **Create a desktop shortcut**
   - **Start Hover Wallpaper when I sign in to Windows**
4. Finish installation and launch Hover Wallpaper.
5. Browse for Image 1 and Image 2.
6. Choose a fit for each image. For **Custom crop**, click **Crop…** and drag the area you want.
7. Adjust the hover area, either with the number fields or with **Visual Editor…**.
8. Click **Apply wallpaper**.

### Closing the window and exiting

Closing the settings window does **not** stop the wallpaper. It hides the window and the app keeps running in the system tray. The first time, a balloon tip tells you where it went.

- **Open Hover Wallpaper** (tray menu or double-click the tray icon) shows the settings again.
- **Stop** in the settings window removes the wallpaper but leaves the app in the tray.
- **Exit** in the tray menu quits the app completely. The desktop goes back to your normal Windows background.
- Starting the app a second time opens the settings of the copy that is already running.

### Automatic startup

The installer adds a per-user startup entry under:

```text
HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run
```

The startup command launches the app with the `--startup` argument. In this mode, the wallpaper starts in the background and a tray icon provides:

- **Open Hover Wallpaper** — opens the settings window.
- **Exit** — closes the wallpaper process.

The settings window also includes a **Launch when I sign in to Windows** checkbox. Applying settings updates the same per-user startup registration.

## 🗂️ Settings and data

The application stores its image paths, fit options, crop areas, hover-zone and fade settings at:

```text
%LOCALAPPDATA%\HoverWallpaper\settings.json
```

The settings file is created when you click **Apply wallpaper**.

## 🧱 Technology

- **C#**
- **.NET 8**
- **Windows Forms**
- **Win32 API** for cursor position, window parenting, and Windows desktop integration
- **Windows Registry** for per-user startup registration
- **Inno Setup 6** for installer creation
- **GitHub Actions** for Windows CI packaging

## ⚠️ Current prototype limitations

Hover Wallpaper is an early prototype. Please keep these limitations in mind:

- **One hover zone:** only one rectangular trigger area is supported.
- **One canvas for all monitors:** fit options and crops are applied to the whole virtual desktop, not to each monitor on its own.
- **Needs a wallpaper layer:** the app draws above the Windows background. If Windows keeps resetting Personalization to a solid color, the desktop behind the app can stay black when the app is not running.
- **Windows desktop integration:** the app attaches its wallpaper window to Explorer's `WorkerW` desktop host. Explorer updates, Windows configurations, and multi-monitor setups can behave differently. The app attempts to reattach if the host disappears, but this needs broader testing.
- **Startup behavior:** startup is registered per Windows user, not system-wide.
- **WebP support:** decoding depends on codecs available on the target Windows installation.
- **No code signing:** the installer is not digitally signed, so Windows SmartScreen may show an unrecognized-app warning.

This project has not yet been validated across a range of Windows 11 devices. Test it on your own setup before relying on it or distributing it broadly.

## 🛠️ Troubleshooting

| Problem | Things to check |
|---|---|
| `No .NET SDKs were found` error | You have the .NET Runtime installed, but not the SDK. Install it with `winget install Microsoft.DotNet.SDK.8` and reopen your terminal. |
| `Inno Setup 6 not found` error | Inno Setup is missing. Install with `winget install JRSoftware.InnoSetup` or install to default path. |
| The installer does not build | Confirm .NET 8 SDK and Inno Setup 6 are installed. Run `.\build.ps1` from the repository root. |
| The wallpaper does not appear behind desktop icons | Explorer's desktop host can vary by system. Restart Explorer and try again; report the Windows build and monitor setup when filing an issue. |
| The hover area is in the wrong place | Remember that X/Y/width/height are percentages of the entire virtual desktop, not a single monitor. |
| The desktop stays unchanged or turns black after Apply | Windows 11 24H2 and newer place the desktop icons under a different window than older versions. Update to the latest build of this app. If it persists, report your Windows build number. |
| The image looks cropped or has black bars | That comes from the fit option. Use **Stretch** to fill without cropping, **Fit** to see the whole image, or **Custom crop** to pick the area yourself. |
| The window closes but the wallpaper stays | This is expected. The app runs from the system tray. Choose **Exit** in the tray menu to quit. |
| An image does not load | Try a standard PNG or JPEG and confirm the file still exists at the saved path. |
| Startup does not work | Check the current user's Run registry key and confirm the saved image paths remain valid. |

## 🗺️ Roadmap

Ideas for future versions:

- [x] Visual hover-zone editor with drag-to-select.
- [ ] Multiple hover zones with independent alternate images.
- [x] Smooth fade transitions with separate fade-in and fade-out durations.
- [ ] Per-monitor image and hover-zone configuration.
- [ ] Better handling of Explorer restarts and desktop changes.
- [x] Per-image fit options and a custom crop editor.
- [x] Keep running in the system tray after the settings window is closed.
- [ ] More tray quick controls (pause, switch images).
- [ ] Installer signing and automated GitHub Releases.
- [ ] Settings backup, import, and export.

## 📝 Changelog

See [CHANGELOG.md](CHANGELOG.md) for the release history.

## 🤝 Contributing

Contributions, bug reports, and ideas are welcome.

When reporting a bug, please include:

- Windows 11 version and build.
- Number of monitors and their resolutions/scaling.
- Steps to reproduce the issue.
- Whether the app was launched normally or through Windows startup.
- Any relevant error message.

For code changes, keep pull requests focused and include clear reproduction or testing notes.

## 📄 License

No open-source license has been selected yet. Until a license is added, all rights are reserved by the copyright holder. If you intend to publish this repository for public contributions or reuse, add a license file first.

---

<div align="center">

**Hover Wallpaper** · A small experiment in interactive desktop customization.

Made for Windows 11.

</div>
