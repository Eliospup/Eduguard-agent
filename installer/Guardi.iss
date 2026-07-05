; ---------------------------------------------------------------------------
;  Guardi — Inno Setup script
;
;  Produces a branded setup.exe a regular user double-clicks. Installs the
;  self-contained build, registers in "Add/Remove Programs", launches Guardi
;  for its own (Guardi-styled) first-run wizard, and on uninstall runs
;  EduGuardAgent.exe --uninstall FIRST so the full machine footprint (hosts,
;  DNS, browser policies, root certificate, scheduled tasks, all data folders)
;  is reversed and the self-protecting process cluster is stopped before any
;  file is deleted — leaving no trace.
;
;  Build it via build-installer.ps1 (which publishes, generates the branding
;  assets, then invokes ISCC with the SourceDir / AssetsDir / OutputDir defines).
; ---------------------------------------------------------------------------

#define AppName "Guardi"
#define AppPublisher "Guardi"
#define AppExeName "EduGuardAgent.exe"

#ifndef AppVersion
  #define AppVersion "0.8.43"
#endif
#ifndef SourceDir
  #define SourceDir "..\publish\GuardiSetup"
#endif
#ifndef AssetsDir
  #define AssetsDir "assets"
#endif
#ifndef OutputDir
  #define OutputDir "output"
#endif

[Setup]
; A stable AppId keeps upgrades / uninstall entries consistent — do not change it.
AppId={{B7E6B2A0-9C3E-4F5D-9A1B-7E2C4D8F0A11}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=auto
PrivilegesRequired=admin
OutputDir={#OutputDir}
OutputBaseFilename=GuardiSetup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
SetupIconFile={#AssetsDir}\guardi.ico
WizardImageFile={#AssetsDir}\wizard-large.bmp
WizardSmallImageFile={#AssetsDir}\wizard-small.bmp
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\{#AppExeName}

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch {#AppName} now"; Flags: nowait postinstall skipifsilent shellexec

[UninstallDelete]
; Safety net: anything the exe teardown missed (e.g. re-created by AuditLog after deletion).
Type: filesandordirs; Name: "{commonappdata}\EduGuard"
Type: filesandordirs; Name: "{userappdata}\EduGuard"
Type: filesandordirs; Name: "{localappdata}\EduGuard"

[UninstallRun]
; Safety net: remove native-messaging registry keys if the exe teardown failed silently.
Filename: "reg.exe"; Parameters: "delete ""HKLM\Software\Mozilla\NativeMessagingHosts\com.guardi.eduguard"" /f"; Flags: runhidden; RunOnceId: "nm_hklm_moz"
Filename: "reg.exe"; Parameters: "delete ""HKLM\Software\Google\Chrome\NativeMessagingHosts\com.guardi.eduguard"" /f"; Flags: runhidden; RunOnceId: "nm_hklm_chr"
Filename: "reg.exe"; Parameters: "delete ""HKLM\Software\Microsoft\Edge\NativeMessagingHosts\com.guardi.eduguard"" /f"; Flags: runhidden; RunOnceId: "nm_hklm_edg"
Filename: "reg.exe"; Parameters: "delete ""HKCU\Software\Mozilla\NativeMessagingHosts\com.guardi.eduguard"" /f"; Flags: runhidden; RunOnceId: "nm_hkcu_moz"
Filename: "reg.exe"; Parameters: "delete ""HKCU\Software\Google\Chrome\NativeMessagingHosts\com.guardi.eduguard"" /f"; Flags: runhidden; RunOnceId: "nm_hkcu_chr"
Filename: "reg.exe"; Parameters: "delete ""HKCU\Software\Microsoft\Edge\NativeMessagingHosts\com.guardi.eduguard"" /f"; Flags: runhidden; RunOnceId: "nm_hkcu_edg"

[Messages]
WelcomeLabel1=Welcome to Guardi Setup
WelcomeLabel2=Guardi and its mascot watch over screen time and browsing.%n%nThis wizard will install Guardi on this computer. All configuration (PIN, mode, limits) is done inside the app on first launch.%n%nClick Next to continue.
FinishedHeadingLabel=Guardi is ready!
FinishedLabelNoIcons=Guardi has been installed on this computer.
ClickFinish=Click Finish — Guardi will open for initial setup.
ConfirmUninstall=Are you sure you want to uninstall Guardi?%n%nAll protections (hosts, DNS, browser policies, certificate, scheduled tasks) will be reversed and all Guardi data will be deleted. No trace will remain on this computer.

[Code]
{ Light Guardi-blue accent on the welcome / finished pages, kept intentionally
  minimal so it stays robust across Inno Setup versions. }
procedure InitializeWizard;
begin
  WizardForm.WelcomeLabel1.Font.Color := $008A3A1E;   { PrimaryDark #1E3A8A (BGR) }
  WizardForm.WelcomeLabel1.Font.Style := [fsBold];
  WizardForm.FinishedHeadingLabel.Font.Color := $008A3A1E;
end;

{ Uninstall gate. Runs EduGuardAgent.exe --uninstall FIRST (while the exe still exists):
  it shows the Guardi self-lock / PIN gate and, only when authorized, stops the protected
  cluster and reverses the whole system footprint + deletes all data. The exit code decides
  whether Inno Setup goes on to remove the program files:
     0 = authorized & torn down -> proceed
     1 = PIN missing / cancelled -> abort
     2 = self-lock active        -> abort }
{ True when Guardi has ever been configured (a PIN verifier or the mode marker exists in the
  hardened secure-state folder). Used to fail closed when the gate exe is missing: renaming or
  deleting EduGuardAgent.exe must NOT open a free, PIN-less uninstall path. }
function GuardiWasConfigured(): Boolean;
var
  SecureDir: String;
begin
  SecureDir := ExpandConstant('{commonappdata}\EduGuard\secure');
  Result :=
    FileExists(SecureDir + '\exit_pin.dat') or
    FileExists(SecureDir + '\self_lock.dat') or
    FileExists(SecureDir + '\.mode_configured') or
    FileExists(SecureDir + '\agent_mode.json');
end;

function InitializeUninstall(): Boolean;
var
  ExePath: String;
  ResultCode: Integer;
begin
  ExePath := ExpandConstant('{app}\{#AppExeName}');
  if not FileExists(ExePath) then
  begin
    { The gate exe is gone. If Guardi was ever configured, the exe was removed/renamed to
      dodge the PIN gate — fail closed. The parent can reinstall Guardi over the top (which
      restores the exe) and then uninstall properly with the PIN. Only a never-configured
      install (no secure state) is allowed to uninstall freely. }
    if GuardiWasConfigured() then
    begin
      MsgBox('Guardi cannot be uninstalled: its verification file is missing '
        + '(EduGuardAgent.exe was moved, renamed, or deleted).'#13#10#13#10
        + 'Reinstall Guardi over the current installation, then uninstall it '
        + 'normally with the PIN.', mbError, MB_OK);
      Result := False;
      exit;
    end;

    Result := True;   { Never configured — nothing to gate. }
    exit;
  end;

  if not Exec(ExePath, '--uninstall', '', SW_SHOW, ewWaitUntilTerminated, ResultCode) then
  begin
    MsgBox('Unable to launch the Guardi uninstall verification.', mbError, MB_OK);
    Result := False;
    exit;
  end;

  if ResultCode = 0 then
    Result := True
  else if ResultCode = 2 then
  begin
    MsgBox('Guardi is in self-lock mode. Uninstallation is not possible until the lock period has ended.', mbError, MB_OK);
    Result := False;
  end
  else
  begin
    MsgBox('PIN required or incorrect. Guardi uninstallation has been cancelled.', mbError, MB_OK);
    Result := False;
  end;
end;
