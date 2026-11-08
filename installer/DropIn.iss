#ifndef SourceDir
  #error "SourceDir must point to a published DropIn directory."
#endif
#ifndef OutputDir
  #error "OutputDir must point to the installer output directory."
#endif
#ifndef TargetRid
  #error "TargetRid must identify the target runtime."
#endif

#define AppName "DropIn"
#define AppVersion "1.0.0"

#if TargetRid == "win-x64"
  #define RuntimeArch "x64"
#elif TargetRid == "win-x86"
  #define RuntimeArch "x86"
#elif TargetRid == "win-arm64"
  #define RuntimeArch "arm64"
#else
  #error "Unsupported target runtime."
#endif

[Setup]
AppId={{E12A6EA5-F560-4A32-9A14-469251C87F6E}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=Sidik Hadi Kurniadi
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableReadyPage=yes
DisableFinishedPage=yes
PrivilegesRequired=admin
#if TargetRid == "win-x64"
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
#elif TargetRid == "win-arm64"
ArchitecturesAllowed=arm64
ArchitecturesInstallIn64BitMode=arm64
#endif
OutputDir={#OutputDir}
OutputBaseFilename={#AppName}-Setup-{#TargetRid}
SetupIconFile=..\src\app_icon.ico
UninstallDisplayIcon={app}\DropIn.Installer.exe
UninstallDisplayName={#AppName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\DropIn Installer"; Filename: "{app}\DropIn.Installer.exe"; WorkingDir: "{app}"
Name: "{group}\DropIn Packager"; Filename: "{app}\DropIn.Packager.exe"; WorkingDir: "{app}"

[Run]
Filename: "{app}\DropIn.Installer.exe"; WorkingDir: "{app}"; Flags: nowait skipifsilent runasoriginaluser

[UninstallRun]
Filename: "{app}\DropIn.Installer.exe"; Parameters: "--unregister"; WorkingDir: "{app}"; Flags: waituntilterminated; RunOnceId: "UnregisterDropIn"

[Code]
const
  UninstallRegistryKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{E12A6EA5-F560-4A32-9A14-469251C87F6E}_is1';
  RuntimeRegistryKey = 'SOFTWARE\dotnet\Setup\InstalledVersions\{#RuntimeArch}\sharedfx\Microsoft.WindowsDesktop.App';

var
  MaintenancePage: TInputOptionWizardPage;
  ExistingUninstaller: String;
  LeavingForUninstall: Boolean;

function HasDesktopRuntime(RootKey: Integer): Boolean;
var
  Versions: TArrayOfString;
  I, Patch: Integer;
  Installed: Cardinal;
begin
  Result := False;
  if not RegGetValueNames(RootKey, RuntimeRegistryKey, Versions) then
    Exit;
  for I := 0 to GetArrayLength(Versions) - 1 do
  begin
    if Copy(Versions[I], 1, 4) = '8.0.' then
    begin
      Patch := StrToIntDef(Copy(Versions[I], 5, Length(Versions[I])), -1);
      if (Patch >= 0) and
         RegQueryDWordValue(RootKey, RuntimeRegistryKey, Versions[I], Installed) and
         (Installed = 1) then
      begin
        Result := True;
        Exit;
      end;
    end;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if not HasDesktopRuntime(HKLM32) and
     not (IsWin64 and HasDesktopRuntime(HKLM64)) then
    Result := 'DropIn requires the {#RuntimeArch} .NET 8 Desktop Runtime. ' +
      'Install it from https://dotnet.microsoft.com/download/dotnet/8.0 and run setup again.';
end;

function FindExistingInstallation(RootKey: Integer): Boolean;
var
  Command: String;
begin
  Result := False;
  if not RegQueryStringValue(RootKey, UninstallRegistryKey, 'UninstallString', Command) then
    Exit;
  Command := RemoveQuotes(Command);
  if not FileExists(Command) then
    Exit;
  ExistingUninstaller := Command;
  Result := True;
end;

procedure InitializeWizard();
begin
  if FindExistingInstallation(HKLM32) or
    (IsWin64 and FindExistingInstallation(HKLM64)) then
  begin
    MaintenancePage := CreateInputOptionPage(wpWelcome, 'DropIn maintenance',
      'DropIn is already installed.',
      'Choose whether to reinstall or uninstall DropIn.', True, False);
    MaintenancePage.Add('Reinstall or update DropIn');
    MaintenancePage.Add('Uninstall DropIn');
    MaintenancePage.SelectedValueIndex := 0;
  end;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  ResultCode: Integer;
begin
  Result := True;
  if (MaintenancePage <> nil) and (CurPageID = MaintenancePage.ID) and
    (MaintenancePage.SelectedValueIndex = 1) then
  begin
    Result := False;
    if not Exec(ExistingUninstaller, '', '', SW_SHOWNORMAL, ewNoWait, ResultCode) then
    begin
      MsgBox('Could not start the DropIn uninstaller: ' + SysErrorMessage(ResultCode), mbError, MB_OK);
      Exit;
    end;
    LeavingForUninstall := True;
    WizardForm.Close;
  end;
end;

procedure CancelButtonClick(CurPageID: Integer; var Cancel, Confirm: Boolean);
begin
  if LeavingForUninstall then
    Confirm := False;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = wpSelectDir then
    WizardForm.NextButton.Caption := SetupMessage(msgButtonInstall);
end;
