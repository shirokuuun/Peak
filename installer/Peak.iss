; Compile through scripts/package-installer.ps1. The first freeware release may be explicitly packaged unsigned.
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\dist\Peak"
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts\releases"
#endif
#ifdef UnsignedTest
  #define PackageSuffix "-unsigned-test"
#else
  #define PackageSuffix ""
#endif

#ifndef PublisherName
  #define PublisherName "neorangel"
#endif

[Setup]
AppId={{6C0D517B-90AB-4F39-B942-42070A79946B}
AppName=Peak
AppVersion={#AppVersion}
AppPublisher={#PublisherName}
AppPublisherURL=https://peakforwindows.vercel.app
AppSupportURL=https://peakforwindows.vercel.app/support/
AppUpdatesURL=https://peakforwindows.vercel.app/download/
DefaultDirName={localappdata}\Programs\Peak
DefaultGroupName=Peak
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
AppMutex=Local\UsageNotch.SingleInstance
SetupIconFile=..\src\UsageNotch.App\Assets\Notch.ico
UninstallDisplayIcon={app}\Peak.exe
OutputDir={#OutputDir}
OutputBaseFilename=Peak-{#AppVersion}-Windows-x64-Setup{#PackageSuffix}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
LicenseFile=..\src\UsageNotch.App\Policies\Terms.md
CloseApplications=yes
RestartApplications=no
Uninstallable=yes
#ifdef SignRelease
SignTool=peak
SignedUninstaller=yes
#endif

[Files]
Source: "{#PublishDir}\*.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\*.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\*.deps.json"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\*.runtimeconfig.json"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\publishing.json"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\bridge\*.exe"; DestDir: "{app}\bridge"; Flags: ignoreversion
Source: "{#PublishDir}\bridge\*.dll"; DestDir: "{app}\bridge"; Flags: ignoreversion
Source: "{#PublishDir}\bridge\*.deps.json"; DestDir: "{app}\bridge"; Flags: ignoreversion
Source: "{#PublishDir}\bridge\*.runtimeconfig.json"; DestDir: "{app}\bridge"; Flags: ignoreversion
Source: "{#PublishDir}\licenses\*"; DestDir: "{app}\licenses"; Flags: ignoreversion recursesubdirs createallsubdirs

Source: "{#PublishDir}\Policies\*"; DestDir: "{app}\Policies"; Flags: ignoreversion

[Icons]
Name: "{userprograms}\Peak"; Filename: "{app}\Peak.exe"

[Run]
Filename: "{app}\Peak.exe"; Description: "Open Peak"; Flags: nowait postinstall skipifsilent

[Code]
function InitializeUninstall(): Boolean;
var
  ExitCode: Integer;
  Arguments: String;
begin
  Result := False;
  Arguments := '--prepare-uninstall';
  if not UninstallSilent then
    if MsgBox('Also delete Peak preferences and all owned local data? Provider connections will be safely removed first. Choose No to preserve preferences for reinstall. Close Peak and provider CLI sessions before continuing.', mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
      Arguments := '--delete-local-data';
  if UninstallSilent then Arguments := Arguments + ' --quiet';
  if not Exec(ExpandConstant('{app}\Peak.exe'), Arguments, '', SW_HIDE, ewWaitUntilTerminated, ExitCode) then
  begin
    if not UninstallSilent then MsgBox('Peak could not disconnect its provider connections. Uninstall was stopped so restoration backups stay available.', mbError, MB_OK);
    Exit;
  end;
  if ExitCode <> 0 then
  begin
    if not UninstallSilent then MsgBox('Close Peak and existing Codex/Claude sessions, then try again. If cleanup still fails, disconnect providers in Peak Settings. Uninstall was stopped.', mbError, MB_OK);
    Exit;
  end;
  Result := True;
end;
