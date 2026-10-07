; PenWin kurulum betiği (Inno Setup 6).
; Derleme: ISCC.exe /DAppVersion=1.0.0 installer\penwin.iss  (önce build.cmd)
; Kullanıcı başına kurar: yönetici izni gerekmez, %LOCALAPPDATA%\Programs\PenWin.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

[Setup]
AppId={{8F1C2B7E-4A5D-4C1B-9E3F-6A2D7B9C0E15}
AppName=PenWin
AppVersion={#AppVersion}
AppVerName=PenWin {#AppVersion}
AppPublisher=EmreOzhan
AppPublisherURL=https://github.com/emreozhan/penwin
AppSupportURL=https://github.com/emreozhan/penwin/issues
DefaultDirName={autopf}\PenWin
DefaultGroupName=PenWin
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\dist
OutputBaseFilename=PenWin-Setup-{#AppVersion}
SetupIconFile=..\assets\penwin.ico
UninstallDisplayIcon={app}\penwin.exe
LicenseFile=..\LICENSE
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ShowLanguageDialog=auto
CloseApplications=yes

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "tr"; MessagesFile: "compiler:Languages\Turkish.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "..\bin\penwin.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.tr.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\PenWin"; Filename: "{app}\penwin.exe"
Name: "{autodesktop}\PenWin"; Filename: "{app}\penwin.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\penwin.exe"; Description: "{cm:LaunchProgram,PenWin}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Bağlantı anahtarı
Type: filesandordirs; Name: "{localappdata}\PenWin"
