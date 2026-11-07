; Apps Manager installer (Inno Setup 5)
;
; Full build:  C:\inno\ISCC.exe setup.iss
; Lite build:  C:\inno\ISCC.exe /DLITE setup.iss
; Or run build-setup.cmd to build both.
;
; Both installers put the 64-bit/AnyCPU exe on 64-bit Windows and the x86 exe on
; 32-bit Windows, into Program Files, and support Windows 10 version 1809 and newer.
;
; Full: ships the portable winget in tools\winget (~145 MB payload).
; Lite: ships no winget; the app uses the system winget, or downloads it into
;       tools\winget (that folder is made writable by the installer).

#define AppVersion "1.1"
#define AppPublisher "Apps Manager"
#define AppExe "AppsManager.exe"

#ifdef LITE
  #define AppName "Apps Manager Lite"
  #define BuildKind "Lite"
#else
  #define AppName "Apps Manager"
  #define BuildKind "Full"
#endif

[Setup]
AppId={{8F2B0C4A-6E1D-4A57-9B3E-2D7C5A1F4E90}
AppName={#AppName}
AppVerName={#AppName} {#AppVersion}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={pf}\Apps Manager
DefaultGroupName={#AppName}
UninstallDisplayIcon={app}\{#AppExe}
OutputDir=dist
OutputBaseFilename=AppsManager-Setup-{#AppVersion}-{#BuildKind}
SetupIconFile=app.ico
Compression=lzma2/max
SolidCompression=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x86 x64
ArchitecturesInstallIn64BitMode=x64
MinVersion=10.0.17763
DisableProgramGroupPage=yes
AllowNoIcons=yes
CloseApplications=no
RestartApplications=no
VersionInfoVersion=1.1.0.0
VersionInfoDescription={#AppName} Setup
VersionInfoProductName={#AppName}
VersionInfoProductVersion={#AppVersion}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "french"; MessagesFile: "compiler:Languages\French.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Dirs]
; winget is unpacked here on first use (packed in the full build, downloaded in the lite one)
Name: "{app}\tools\winget"; Permissions: users-modify

[InstallDelete]
; older builds shipped the unpacked ~145 MB tree; the packed copy replaces it
Type: filesandordirs; Name: "{app}\tools\winget\AppInstaller_x86"
#ifdef LITE
; upgrading from the full build must not leave the packed copy behind
Type: files; Name: "{app}\tools\winget\winget.zip"
#endif

[Files]
Source: "bin\AppsManager.exe"; DestDir: "{app}"; DestName: "{#AppExe}"; Flags: ignoreversion; Check: Is64BitInstallMode
Source: "bin\x86\AppsManager.exe"; DestDir: "{app}"; DestName: "{#AppExe}"; Flags: ignoreversion; Check: not Is64BitInstallMode
Source: "app.html"; DestDir: "{app}"; Flags: ignoreversion
Source: "lang\*.txt"; DestDir: "{app}\lang"; Flags: ignoreversion
Source: "tools\aria2c.exe"; DestDir: "{app}\tools"; Flags: ignoreversion
#ifndef LITE
Source: "bin\tools\winget\winget.zip"; DestDir: "{app}\tools\winget"; Flags: ignoreversion
#endif

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"
Name: "{commondesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[Code]
procedure KillAppsManager;
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/f /t /im {#AppExe}', '',
       SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

function InitializeSetup(): Boolean;
var
  Release: Cardinal;
begin
  Result := True;
  if (not RegQueryDWordValue(HKLM, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full',
                             'Release', Release)) or (Release < 528040) then
  begin
    MsgBox('{#AppName} needs Microsoft .NET Framework 4.8.' + #13#10 + #13#10 +
           'Install it from https://dotnet.microsoft.com/download/dotnet-framework/net48 ' +
           'and run this setup again.', mbError, MB_OK);
    Result := False;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  KillAppsManager;
  Result := '';
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
    KillAppsManager;
end;
