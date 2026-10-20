; Script Inno Setup pour SystemeCaisse
#define MyAppName "SystemeCaisse"
#define MyAppVersion "4.8.0"
#define MyAppPublisher "Hippocampe Import-Export"
#define MyAppExeName "SystemeCaisse.UI.exe"
#define BuildDir "SystemeCaisse.UI\bin\Release\net8.0-windows10.0.19041\win-x64\publish"

[Setup]
AppId={{5C1B9F5E-49AB-4F43-9C1D-EEA773F4BB9E}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputBaseFilename=Setup_SystemeCaisse
OutputDir=Distribution
Compression=lzma2/ultra
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64
PrivilegesRequired=admin

[Languages]
Name: "french"; MessagesFile: "compiler:Languages\French.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: checkedonce
Name: "setupprincipal"; Description: "Configurer ce poste en tant que Poste Principal (Serveur PostgreSQL + Tailscale autonome)"; GroupDescription: "Architecture Multi-Postes (Production) :"; Flags: unchecked

[Files]
Source: "{#BuildDir}\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Parameters: "--setup-principal"; Description: "Lancer {#MyAppName} et provisionner automatiquement PostgreSQL (Poste Principal)"; Tasks: setupprincipal; Flags: nowait postinstall skipifsilent runascurrentuser
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Tasks: not setupprincipal; Flags: nowait postinstall skipifsilent
