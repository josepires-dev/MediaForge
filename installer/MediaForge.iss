; Inno Setup. Pré-requisito: correr scripts\publish.ps1 (gera artifacts\publish).
; Compilar: iscc installer\MediaForge.iss
#define AppName "MediaForge"
#define AppVersion "1.0.0"
#define AppExe "MediaForge.exe"

[Setup]
AppId={{6F3C2B1A-4D5E-4F60-9A7B-8C1D2E3F4A5B}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=MediaForge
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
OutputDir=..\artifacts\installer
OutputBaseFilename=MediaForge-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
SetupIconFile=..\Assets\MediaForge.ico
UninstallDisplayIcon={app}\{#AppExe}
WizardStyle=modern

[Languages]
Name: "portuguese"; MessagesFile: "compiler:Languages\Portuguese.isl"

[Tasks]
Name: "desktopicon"; Description: "Criar atalho no ambiente de trabalho"; Flags: unchecked

[Files]
Source: "..\artifacts\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "Abrir {#AppName}"; Flags: nowait postinstall skipifsilent
