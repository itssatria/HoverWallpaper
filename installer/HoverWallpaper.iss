#define MyAppName "Hover Wallpaper"
; build.ps1 passes the real version with /DMyAppVersion=x.y.z; this is only a fallback.
#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif
#define MyAppPublisher "Hover Wallpaper"
#define MyAppExeName "HoverWallpaper.exe"

[Setup]
AppId={{A7C7F2D8-34E1-4B8F-9D1C-6B20D0A8F123}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\Hover Wallpaper
DefaultGroupName=Hover Wallpaper
DisableProgramGroupPage=yes
OutputDir=Output
OutputBaseFilename=HoverWallpaper-Setup-{#MyAppVersion}
SetupIconFile=..\HoverWallpaper\Assets\HoverWallpaper.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
ArchitecturesInstallIn64BitMode=x64
PrivilegesRequired=lowest
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
; Publish output is copied here by build.ps1.
SourceDir=.

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"
Name: "startup"; Description: "Start Hover Wallpaper when I sign in to Windows"; GroupDescription: "Windows startup:"

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Hover Wallpaper"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\Hover Wallpaper"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch Hover Wallpaper"; Flags: nowait postinstall skipifsilent

[Code]
procedure CurStepChanged(CurStep: TSetupStep);
var
  StartupKey: string;
  ExePath: string;
begin
  if CurStep = ssPostInstall then
  begin
    StartupKey := 'Software\Microsoft\Windows\CurrentVersion\Run';
    ExePath := '"' + ExpandConstant('{app}\{#MyAppExeName}') + '" --startup';
    if WizardIsTaskSelected('startup') then
      RegWriteStringValue(HKCU, StartupKey, 'HoverWallpaper', ExePath)
    else
      RegDeleteValue(HKCU, StartupKey, 'HoverWallpaper');
  end;
end;
