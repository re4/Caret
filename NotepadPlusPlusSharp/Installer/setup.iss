#define MyAppName "Notepad++ #"
#define MyAppNameShort "NotepadPlusPlusSharp"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "NotepadPlusPlusSharp"
#define MyAppURL "https://github.com/ptraced/NotepadPlusPlusSharp"
#define MyAppExeName "NotepadPlusPlusSharp.exe"
#define ProjectDir ".."
#define PublishDir "..\bin\Release\net10.0-windows\win-x64\publish"

[Setup]
AppId={{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppNameShort}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
OutputDir=..\Output
OutputBaseFilename=NotepadPlusPlusSharp_Setup_{#MyAppVersion}
SetupIconFile={#ProjectDir}\App.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/fast
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: checkedonce
Name: "contextmenu"; Description: "Add ""Edit with {#MyAppName}"" to Windows right-click context menu"; GroupDescription: "Windows integration:"; Flags: checkedonce

[Files]
Source: "{#PublishDir}\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\*.pdb"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "{#ProjectDir}\Icon.png"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#ProjectDir}\App.ico"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\App.ico"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\App.ico"; Tasks: desktopicon

[Registry]
Root: HKCR; Subkey: "*\shell\NotepadPlusPlusSharp"; ValueType: string; ValueName: ""; ValueData: "Edit with {#MyAppName}"; Tasks: contextmenu; Flags: uninsdeletekey
Root: HKCR; Subkey: "*\shell\NotepadPlusPlusSharp"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\App.ico"""; Tasks: contextmenu
Root: HKCR; Subkey: "*\shell\NotepadPlusPlusSharp\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Tasks: contextmenu; Flags: uninsdeletekey
Root: HKCR; Subkey: "Directory\Background\shell\NotepadPlusPlusSharp"; ValueType: string; ValueName: ""; ValueData: "Open {#MyAppName}"; Tasks: contextmenu; Flags: uninsdeletekey
Root: HKCR; Subkey: "Directory\Background\shell\NotepadPlusPlusSharp"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\App.ico"""; Tasks: contextmenu
Root: HKCR; Subkey: "Directory\Background\shell\NotepadPlusPlusSharp\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"""; Tasks: contextmenu; Flags: uninsdeletekey

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: files; Name: "{app}\*"
Type: dirifempty; Name: "{app}"
