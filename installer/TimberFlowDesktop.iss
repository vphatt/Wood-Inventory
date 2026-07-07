; Inno Setup script cho TimberFlow ERP Desktop (WPF thuần .NET + SQLite)
; Đóng gói bản publish self-contained thành file cài đặt Windows hoàn chỉnh.
; Chạy offline 100%% — máy đích không cần cài .NET hay bất kỳ thành phần web nào.

#define MyAppName "TimberFlow ERP Desktop"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "TimberFlow"
#define MyAppExeName "TimberFlowDesktop.exe"
#define MyAppSource "..\build\desktop-app"

[Setup]
AppId={{8A1C4F27-53D0-4B7E-9A64-2F1B7C9D0E33}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=..\build\installer
OutputBaseFilename=TimberFlowDesktop-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
; Cài per-machine (Program Files). Database ghi tại %APPDATA%\TimberFlowDesktop
; nên thư mục cài chỉ-đọc không gây vấn đề.
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Tạo lối tắt ngoài &Desktop"; GroupDescription: "Lối tắt bổ sung:"

[Files]
Source: "{#MyAppSource}\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Gỡ cài đặt {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Khởi động {#MyAppName}"; Flags: nowait postinstall skipifsilent
