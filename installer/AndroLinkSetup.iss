; ============================================================================
;  AndroLink - Android Subsystem for Windows
;  Inno Setup 6 script. Build with:
;    ISCC.exe installer\AndroLinkSetup.iss
;  (expects a published build in out\publish - see scripts\build.ps1 -Publish)
; ============================================================================

#define MyAppName "AndroLink"
#define MyAppVersion "0.2.2"
#define MyAppPublisher "AndroLink Project"
#define MyAppExeName "AndroLink.Manager.exe"
#define MyAppId "{{8B6F4C7A-52D1-4E9B-9C33-A1B2C3D4E5F6}"

[Setup]
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
LicenseFile=..\LICENSE
OutputDir=Output
OutputBaseFilename=AndroLinkSetup
SetupIconFile=..\src\AndroLink.Manager\assets\androlink.ico
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
; User data (shared storage + settings) is intentionally NOT touched on uninstall.
UninstallFilesDir={app}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "userpathentry"; Description: "Add AndroLink to the user PATH (for the androlink manager executable)"; Flags: unchecked

[Files]
; Main application (published output)
Source: "..\out\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; Docs
Source: "..\docs\*"; DestDir: "{app}\docs"; Flags: ignoreversion recursesubdirs createallsubdirs; Check: DocsExist

[Dirs]
; Per-user shared storage root is created on first run by the app itself.

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
; PATH entry for the user task (uninstalled automatically by Inno on removal)
Root: HKCU; Subkey: "Environment"; ValueType: expandsz; ValueName: "Path"; \
    ValueData: "{olddata};{app}"; Tasks: userpathentry; \
    Check: NeedsPathEntry('{app}')

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; \
    Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Keep userdata and settings: they contain the user's emulated device files.
; (Nothing is deleted here on purpose - see docs\FILE_ACCESS.md)

[Code]
function DocsExist(): Boolean;
begin
  Result := DirExists(ExpandConstant('{src}\..\docs'));
end;

// Returns True when '{app}' is not yet in the user's PATH, so the registry
// entry is only written once.
function NeedsPathEntry(InstallPath: string): Boolean;
var
  PathValue: string;
begin
  Result := True;
  if RegQueryStringValue(HKEY_CURRENT_USER, 'Environment', 'Path', PathValue) then
  begin
    Result := Pos(LowerCase(InstallPath), LowerCase(PathValue)) = 0;
  end;
end;

// ---------------------------------------------------------------------------
// .NET 8 Desktop Runtime check: AndroLink targets .NET 8 (framework-dependent
// publish). If the runtime is missing, offer to open the download page.
// ---------------------------------------------------------------------------
function IsDotNet8DesktopInstalled(): Boolean;
var
  KeysFound: Boolean;
begin
  // Runtime can be x64 or arm64; the registry path differs per architecture.
  KeysFound :=
    RegKeyExists(HKEY_LOCAL_MACHINE, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App') or
    RegKeyExists(HKEY_LOCAL_MACHINE, 'SOFTWARE\dotnet\Setup\InstalledVersions\arm64\sharedfx\Microsoft.WindowsDesktop.App') or
    RegKeyExists(HKEY_LOCAL_MACHINE, 'SOFTWARE\WOW6432Node\dotnet\Setup\InstalledVersions\x86\sharedfx\Microsoft.WindowsDesktop.App');
  Result := KeysFound;
end;

function InitializeSetup(): Boolean;
var
  ErrorCode: Integer;
begin
  Result := True;
  if not IsDotNet8DesktopInstalled() then
  begin
    if MsgBox(
        'AndroLink requires the .NET 8 Desktop Runtime, which does not appear to be installed.' + #13#10 + #13#10 +
        'Open the download page now? (You can cancel setup, install the runtime, and run setup again.)',
        mbConfirmation, MB_YESNO) = IDYES then
    begin
      ShellExec('open', 'https://dotnet.microsoft.com/download/dotnet/8.0/runtime', '', '', SW_SHOW, ewNoWait, ErrorCode);
    end;
    // Continue installing anyway - the user may install the runtime afterwards.
  end;
end;

function UpdateReadyMemo(Space, NewLine, MemoUserInfoInfo, MemoDirInfo, MemoTypeInfo,
  MemoComponentsInfo, MemoGroupInfo, MemoTasksInfo: String): String;
begin
  Result := MemoDirInfo + NewLine + NewLine +
    'Your emulated device files live OUTSIDE this folder (default:' + NewLine +
    '  %USERPROFILE%\AndroLink\userdata) and stay fully editable' + NewLine +
    'in Explorer - even while AndroLink is running.' + NewLine +
    'Uninstalling {#MyAppName} does NOT delete that data.';
end;
