#ifndef AppVersion
  #error AppVersion required
#endif
#ifndef Channel
  #error Channel required
#endif
#ifndef PublishDir
  #error PublishDir required
#endif
#ifndef OutputDir
  #error OutputDir required
#endif
[Setup]
AppId=DisplayControlLab.{#Channel}
AppName=DisplayControlLab
AppVersion={#AppVersion}
AppPublisher=Kratosmax
DefaultDirName={localappdata}\Programs\DisplayControlLab-{#Channel}
DefaultGroupName=DisplayControlLab
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputDir}
OutputBaseFilename=DisplayControlLab-{#AppVersion}-{#Channel}-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\assets\branding\displaycontrol.ico
UninstallDisplayIcon={app}\DisplayControl.Controller.exe
CloseApplications=yes
[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
[Icons]
Name: "{group}\DisplayControl · 读取器"; Filename: "{app}\DisplayControl.Reader.exe"; WorkingDir: "{app}"
Name: "{group}\DisplayControl · 控制器"; Filename: "{app}\DisplayControl.Controller.exe"; WorkingDir: "{app}"
#if Channel == "Lite"
[Code]
function HasRuntimeAt(Root: String; Framework: String; RequiredDll: String): Boolean;
var FindRec: TFindRec; Base: String;
begin
  Result := False;
  if Root = '' then Exit;
  Base := AddBackslash(Root) + 'shared\' + Framework + '\';
  if FindFirst(Base + '8.*', FindRec) then
  begin
    try
      repeat
        if FileExists(Base + FindRec.Name + '\' + RequiredDll) then Result := True;
      until Result or not FindNext(FindRec);
    finally
      FindClose(FindRec);
    end;
  end;
end;

function HasDesktopRuntime(Root: String): Boolean;
begin
  Result := HasRuntimeAt(Root, 'Microsoft.WindowsDesktop.App', 'PresentationFramework.dll') and
            HasRuntimeAt(Root, 'Microsoft.NETCore.App', 'System.Private.CoreLib.dll');
end;

function InitializeSetup(): Boolean;
var Names: TArrayOfString; I: Integer; Found: Boolean; Root: String;
begin
  Found := False;
  if RegGetValueNames(HKLM64, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App', Names) then
    for I := 0 to GetArrayLength(Names)-1 do
      if Pos('8.', Names[I]) = 1 then Found := True;
  if not Found then Found := HasDesktopRuntime(ExpandConstant('{pf64}\dotnet'));
  if not Found and RegQueryStringValue(HKLM64, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64', 'InstallLocation', Root) then
    Found := HasDesktopRuntime(Root);
  if not Found then Found := HasDesktopRuntime(GetEnv('DOTNET_ROOT_X64'));
  if not Found then Found := HasDesktopRuntime(GetEnv('DOTNET_ROOT'));
  Result := Found;
  if not Found then SuppressibleMsgBox('Lite 包需要 .NET 8 Desktop Runtime x64。请从 https://dotnet.microsoft.com/download/dotnet/8.0 安装桌面运行库，或改用 Full 包。', mbError, MB_OK, IDOK);
end;
#endif
