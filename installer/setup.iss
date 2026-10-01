#define MyAppName "Rodičovský zámek PC"
#define MyAppVersion "0.1.0"
#define MyAppExeName "FoXKidLockAgent.exe"

[Setup]
AppId={{87B0CA77-D885-44D1-AD0E-9FC503BB49B0}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
DefaultDirName={autopf}\RodicovskyZamekPC
DefaultGroupName={#MyAppName}
OutputDir=..\dist\installer
OutputBaseFilename=Instalator-RodicovskyZamek-Windows
Compression=lzma2
SolidCompression=yes
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayName={#MyAppName}

[Files]
Source: "..\dist\agent-win64\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "agentsettings.json"; DestDir: "{app}"; Flags: onlyifdoesntexist

[Run]
Filename: "{sys}\schtasks.exe"; Parameters: "/Create /TN ""FoXKidLockAgent"" /TR """"{app}\{#MyAppExeName}"""" /SC ONLOGON /RL HIGHEST /F"; Flags: runhidden
Filename: "{app}\{#MyAppExeName}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\schtasks.exe"; Parameters: "/Delete /TN ""FoXKidLockAgent"" /F"; Flags: runhidden; RunOnceId: "DeleteFoXKidLockAgentTask"
