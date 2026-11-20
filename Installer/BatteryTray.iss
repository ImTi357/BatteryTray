[Setup]
AppId={{B7A2E9C1-4D8F-4E2A-9B1C-8F3D2E1A7C90}
AppName=BatteryTray
AppVersion=1.3.0
AppPublisher=BatteryTray Monitor
DefaultDirName={autopf}\BatteryTray
DefaultGroupName=BatteryTray
PrivilegesRequired=lowest
AppMutex=Global\BatteryTraySingleInstanceMutex
CloseApplications=yes
OutputDir=.
OutputBaseFilename=BatteryTray_Setup_v1.3.0
SetupIconFile=..\app.ico
UninstallDisplayIcon={app}\BatteryTray.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startup"; Description: "Khởi động cùng Windows (Start with Windows)"; GroupDescription: "Tùy chọn khởi chạy:"

[Files]
Source: "..\bin\Release\net10.0-windows\win-x64\publish\BatteryTray.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\BatteryTray"; Filename: "{app}\BatteryTray.exe"
Name: "{group}\Gỡ cài đặt BatteryTray"; Filename: "{uninstallexe}"
Name: "{autodesktop}\BatteryTray"; Filename: "{app}\BatteryTray.exe"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "BatteryTrayMonitor"; ValueData: """{app}\BatteryTray.exe"""; Flags: uninsdeletevalue; Tasks: startup

[Run]
Filename: "{app}\BatteryTray.exe"; Description: "Mở BatteryTray ngay sau khi cài đặt"; Flags: nowait postinstall skipifsilent




