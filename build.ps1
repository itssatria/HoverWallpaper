<#
.SYNOPSIS
    Builds Hover Wallpaper and its installer, with an automatic version number.

.DESCRIPTION
    The version is worked out from CHANGELOG.md, so it only changes when you record changes:

      * Start from the newest released heading, e.g. "## v1.2.0 - 2026-11-05".
      * Count the top-level bullets under "## Unreleased":
          - each bullet under "### Added"                  = one new feature -> minor +1
          - each bullet under any other heading            = one fix/change  -> patch +1
            (Fixed, Changed, Removed, Security, ...)
      * With new features:  MAJOR . (MINOR + features) . fixes
        With only fixes:    MAJOR . MINOR . (PATCH + fixes)

    Rebuilding without touching the changelog gives the same version again.

.PARAMETER Version
    Skip the automatic calculation and use this version, e.g. -Version 2.0.0 (for a major bump).

.PARAMETER Release
    After a successful build, stamp the "Unreleased" entries in CHANGELOG.md as this version
    (adds "## vX.Y.Z - date"), so the next build counts from it.

.EXAMPLE
    .\build.ps1            # build with the auto version
    .\build.ps1 -Release   # build, then mark this version as released in CHANGELOG.md
#>
param (
    [string]$Version = "",
    [switch]$Release
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root "HoverWallpaper\HoverWallpaper.csproj"
$publish = Join-Path $root "publish"
$changelogPath = Join-Path $root "CHANGELOG.md"
$utf8 = New-Object System.Text.UTF8Encoding($false)

# ── Work out the version ────────────────────────────────────────────────
function Get-AutoVersion([string]$changelog) {
    # Last released version: the first "## vX", "## vX.Y" or "## vX.Y.Z" heading.
    $major = 0; $minor = 0; $patch = 0
    $m = [regex]::Match($changelog, '(?m)^##[ \t]+v(\d+)(?:\.(\d+))?(?:\.(\d+))?')
    if ($m.Success) {
        $major = [int]$m.Groups[1].Value
        if ($m.Groups[2].Success) { $minor = [int]$m.Groups[2].Value }
        if ($m.Groups[3].Success) { $patch = [int]$m.Groups[3].Value }
    }

    # Count top-level bullets in the "Unreleased" section.
    $added = 0; $fixed = 0
    $u = [regex]::Match($changelog, '(?ms)^##[ \t]+Unreleased[ \t]*\r?\n(.*?)(?=^##[ \t]|\z)')
    if ($u.Success) {
        $section = ""
        foreach ($line in ($u.Groups[1].Value -split "\r?\n")) {
            if ($line -match '^###\s+(.+?)\s*$') { $section = $Matches[1]; continue }
            if ($line -match '^- ') {
                if ($section -eq "Added") { $added++ } else { $fixed++ }
            }
        }
    }

    Write-Host "Last release: v$major.$minor.$patch | Unreleased: $added feature(s), $fixed fix/change(s)"

    if ($added -gt 0) { $minor += $added; $patch = $fixed }
    else              { $patch += $fixed }
    return "$major.$minor.$patch"
}

$changelog = ""
if (Test-Path $changelogPath) { $changelog = [System.IO.File]::ReadAllText($changelogPath, [System.Text.Encoding]::UTF8) }

if ([string]::IsNullOrWhiteSpace($Version)) { $Version = Get-AutoVersion $changelog }
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version must look like 1.2.3 (got '$Version')" }
$fileVersion = "$Version.0"
Write-Host "Building Hover Wallpaper v$Version"

# ── Publish the app ─────────────────────────────────────────────────────
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }

dotnet publish $project -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:Version=$Version -p:FileVersion=$fileVersion -p:InformationalVersion=$Version -o $publish
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

# ── Build the installer ─────────────────────────────────────────────────
# Inno Setup can be installed per machine (Program Files) or per user (winget default).
$candidates = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
)
$iscc = $candidates | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
if (-not $iscc) {
    $cmd = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($cmd) { $iscc = $cmd.Source }
}
if (-not $iscc) { throw "Inno Setup 6 not found. Install it from https://jrsoftware.org/isinfo.php" }
Write-Host "Using Inno Setup: $iscc"

& $iscc "/DMyAppVersion=$Version" (Join-Path $root "installer\HoverWallpaper.iss")
if ($LASTEXITCODE -ne 0) { throw "Inno Setup compilation failed" }
Write-Host "Installer created: installer\Output\HoverWallpaper-Setup-$Version.exe"

# ── Optionally mark this version as released in the changelog ───────────
if ($Release) {
    if ($changelog -match "(?m)^##[ \t]+v$([regex]::Escape($Version))\b") {
        Write-Host "CHANGELOG.md already has a v$Version entry; leaving it as is."
    }
    else {
        $nl = if ($changelog.Contains("`r`n")) { "`r`n" } else { "`n" }
        $date = Get-Date -Format "yyyy-MM-dd"
        $re = [regex]'(?m)^##[ \t]+Unreleased[ \t]*(?=\r?$)'
        $stamped = $re.Replace($changelog, "## Unreleased$nl$nl## v$Version - $date", 1)
        [System.IO.File]::WriteAllText($changelogPath, $stamped, $utf8)
        Write-Host "CHANGELOG.md: Unreleased entries stamped as v$Version. Commit it with your release."
    }
}
