; CodeForge for Windows installer (Inno Setup 6).
; Build: iscc /DAppVersion=0.2.1 /DSourceDir=..\publish\CodeForge installer\CodeForge.iss
; It installs for the current user (no administrator rights needed, and updates install silently the same way);
; choosing "Install for all users" in the first page installs into Program Files instead.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\publish\CodeForge"
#endif
#define AppName "CodeForge"
#define AppExe "CodeForge.exe"

[Setup]
; Never change AppId: Windows uses it to recognize CodeForge for upgrades and uninstalling.
AppId={{A057E3E4-175E-47B1-91A2-1D700AA35E2A}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=Ahmed Al-Bazzaz
AppPublisherURL=https://github.com/ahmedalbazzaz1987/CodeForge-Windows
AppSupportURL=https://github.com/ahmedalbazzaz1987/CodeForge-Windows/issues
AppUpdatesURL=https://github.com/ahmedalbazzaz1987/CodeForge-Windows/releases
VersionInfoVersion={#AppVersion}
VersionInfoDescription=CodeForge Setup
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=..\dist
OutputBaseFilename=CodeForge-{#AppVersion}-setup
SetupIconFile=..\src\CodeForge\CodeForge.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
ArchitecturesAllowed=x64compatible arm64
ArchitecturesInstallIn64BitMode=x64compatible arm64
CloseApplications=yes
RestartApplications=no
ChangesAssociations=yes
LicenseFile=..\LICENSE

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Shortcuts:"
Name: "openwith"; Description: "Add ""Open with CodeForge"" when you right-click a folder"; GroupDescription: "Explorer:"
Name: "associate"; Description: "Open VB.NET and C# project files (.vbproj, .csproj, .sln) with CodeForge"; GroupDescription: "Explorer:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; Right-click a folder (or the empty space inside one) ▸ Open with CodeForge.
Root: HKA; Subkey: "Software\Classes\Directory\shell\CodeForge"; ValueType: string; ValueData: "Open with CodeForge"; Tasks: openwith; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Directory\shell\CodeForge"; ValueName: "Icon"; ValueType: string; ValueData: "{app}\{#AppExe}"; Tasks: openwith
Root: HKA; Subkey: "Software\Classes\Directory\shell\CodeForge\command"; ValueType: string; ValueData: """{app}\{#AppExe}"" ""%V"""; Tasks: openwith
Root: HKA; Subkey: "Software\Classes\Directory\Background\shell\CodeForge"; ValueType: string; ValueData: "Open with CodeForge"; Tasks: openwith; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Directory\Background\shell\CodeForge"; ValueName: "Icon"; ValueType: string; ValueData: "{app}\{#AppExe}"; Tasks: openwith
Root: HKA; Subkey: "Software\Classes\Directory\Background\shell\CodeForge\command"; ValueType: string; ValueData: """{app}\{#AppExe}"" ""%V"""; Tasks: openwith
; "Open with" for project files.
Root: HKA; Subkey: "Software\Classes\CodeForge.Project"; ValueType: string; ValueData: "Project (CodeForge)"; Tasks: associate; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\CodeForge.Project\DefaultIcon"; ValueType: string; ValueData: "{app}\{#AppExe},0"; Tasks: associate
Root: HKA; Subkey: "Software\Classes\CodeForge.Project\shell\open\command"; ValueType: string; ValueData: """{app}\{#AppExe}"" ""%1"""; Tasks: associate
Root: HKA; Subkey: "Software\Classes\.vbproj\OpenWithProgids"; ValueName: "CodeForge.Project"; ValueType: string; ValueData: ""; Tasks: associate; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\.csproj\OpenWithProgids"; ValueName: "CodeForge.Project"; ValueType: string; ValueData: ""; Tasks: associate; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\.sln\OpenWithProgids"; ValueName: "CodeForge.Project"; ValueType: string; ValueData: ""; Tasks: associate; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\.slnx\OpenWithProgids"; ValueName: "CodeForge.Project"; ValueType: string; ValueData: ""; Tasks: associate; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#AppExe}"; Description: "Start CodeForge"; Flags: nowait postinstall skipifsilent

[Code]
{ CodeForge needs the .NET 10 Desktop Runtime (included in the .NET 10 SDK, which is also what builds .NET projects). }
function HasDesktopRuntime(): Boolean;
var
  FindRec: TFindRec;
  Roots: array of String;
  I: Integer;
begin
  Result := False;
  SetArrayLength(Roots, 2);
  Roots[0] := ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App\');
  Roots[1] := ExpandConstant('{localappdata}\Microsoft\dotnet\shared\Microsoft.WindowsDesktop.App\');
  for I := 0 to GetArrayLength(Roots) - 1 do
    if FindFirst(Roots[I] + '10.*', FindRec) then
    begin
      Result := True;
      FindClose(FindRec);
      Exit;
    end;
end;

function InitializeSetup(): Boolean;
var
  ErrorCode: Integer;
begin
  Result := True;
  if HasDesktopRuntime() or WizardSilent() then Exit;
  if MsgBox('CodeForge needs the .NET 10 SDK (or at least the .NET 10 Desktop Runtime), which isn''t installed yet.' + #13#10#13#10 +
            'Open the download page now? You can also install it later with:  winget install Microsoft.DotNet.SDK.10' + #13#10#13#10 +
            'Choose Yes to open the page and continue, No to continue without it.', mbConfirmation, MB_YESNO) = IDYES then
    ShellExec('open', 'https://dotnet.microsoft.com/download/dotnet/10.0', '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
end;
