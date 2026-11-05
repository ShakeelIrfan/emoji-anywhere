; Inno Setup script for Emoji Anywhere.
; Build:  iscc installer\EmojiAnywhere.iss /DAppVersion=0.1.0 /DSourceExe=path\to\EmojiAnywhere.exe
; Installs for the current user only, so no admin prompt. Nothing is downloaded during setup.

#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#ifndef SourceExe
  #define SourceExe "..\out\standalone\EmojiAnywhere.exe"
#endif

[Setup]
AppId={{6F1C9E2A-4B7D-4E8A-9C3F-EA0E1D2B7A55}
AppName=Emoji Anywhere
AppVersion={#AppVersion}
AppVerName=Emoji Anywhere {#AppVersion}
AppPublisher=Shakeel Irfan
AppPublisherURL=https://github.com/ShakeelIrfan/emoji-anywhere
AppSupportURL=https://github.com/ShakeelIrfan/emoji-anywhere/issues
DefaultDirName={localappdata}\Programs\Emoji Anywhere
DefaultGroupName=Emoji Anywhere
DisableProgramGroupPage=yes
DisableDirPage=yes
PrivilegesRequired=lowest
OutputDir=..\dist
OutputBaseFilename=EmojiAnywhere-Setup
SetupIconFile=..\src\EmojiAnywhere\Assets\app.ico
UninstallDisplayIcon={app}\EmojiAnywhere.exe
UninstallDisplayName=Emoji Anywhere
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=force
LicenseFile=..\LICENSE

[Tasks]
Name: "startup"; Description: "Start Emoji Anywhere when Windows starts (recommended)"; GroupDescription: "Options:"
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Options:"; Flags: unchecked

[Files]
Source: "{#SourceExe}"; DestDir: "{app}"; DestName: "EmojiAnywhere.exe"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\Emoji Anywhere"; Filename: "{app}\EmojiAnywhere.exe"
Name: "{autodesktop}\Emoji Anywhere"; Filename: "{app}\EmojiAnywhere.exe"; Tasks: desktopicon

[Registry]
; Same entry the app's own "Start with Windows" setting uses, so both stay in sync
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "EmojiAnywhere"; ValueData: """{app}\EmojiAnywhere.exe"""; Tasks: startup; Flags: uninsdeletevalue

[Run]
Filename: "{app}\EmojiAnywhere.exe"; Description: "Start Emoji Anywhere now"; Flags: nowait postinstall

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM EmojiAnywhere.exe"; Flags: runhidden; RunOnceId: "StopApp"

[UninstallDelete]
; Settings and emoji usage counts. Typed text is never saved, so there is nothing else to remove.
Type: filesandordirs; Name: "{userappdata}\EmojiAnywhere"

[Code]
// A tray app has no window to close, so stop it before updating its files
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM EmojiAnywhere.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := '';
end;

// After a normal uninstall, open the optional "why did you uninstall" page in the browser.
// The app itself never goes online; this is just a web page the person can close.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ErrorCode: Integer;
begin
  if (CurUninstallStep = usPostUninstall) and (not UninstallSilent) then
    ShellExec('open', 'https://emoji-anywhere.vercel.app/bye?v={#AppVersion}', '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
end;

// If the user unticks "start with Windows", make sure the app's own setting does not turn it back on
procedure CurStepChanged(CurStep: TSetupStep);
var
  SettingsFile: String;
begin
  if (CurStep = ssPostInstall) and (not WizardIsTaskSelected('startup')) then
  begin
    SettingsFile := ExpandConstant('{userappdata}\EmojiAnywhere\settings.json');
    if not FileExists(SettingsFile) then
    begin
      ForceDirectories(ExtractFileDir(SettingsFile));
      SaveStringToFile(SettingsFile, '{"StartWithWindows": false}', False);
    end;
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'EmojiAnywhere');
  end;
end;
