[Setup]
AppName=VxFiles
AppVersion=1.0.0
AppPublisher=VxFiles Contributors
AppPublisherURL=https://github.com/hoavu2025/VxFiles
AppSupportURL=https://github.com/hoavu2025/VxFiles/issues
AppUpdatesURL=https://github.com/hoavu2025/VxFiles/releases
DefaultDirName={localappdata}\Programs\VxFiles
DefaultGroupName=VxFiles
AllowNoIcons=yes
OutputDir=..\artifacts
OutputBaseFilename=VxFiles-Setup-win-x64
Compression=lzma
SolidCompression=yes
PrivilegesRequired=lowest
UninstallDisplayIcon={app}\VxFiles.exe
SetupIconFile=..\src\Files.App\Assets\AppTiles\Dev\Logo.ico

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\artifacts\staging\VxFiles-portable-win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; NOTE: Don't use "Flags: ignoreversion" on any shared system files

[Icons]
Name: "{group}\VxFiles"; Filename: "{app}\VxFiles.exe"
Name: "{group}\{cm:UninstallProgram,VxFiles}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\VxFiles"; Filename: "{app}\VxFiles.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\VxFiles.exe"; Description: "{cm:LaunchProgram,VxFiles}"; Flags: nowait postinstall skipifsilent
