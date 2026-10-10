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
AppId=DisplayControl.Reader.{#Channel}
AppName=DisplayControl Reader
AppVersion={#AppVersion}
AppPublisher=Kratosmax
DefaultDirName={localappdata}\Programs\DisplayControlReader-{#Channel}
DefaultGroupName=DisplayControl Reader
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputDir}
OutputBaseFilename=DisplayControl-Reader-{#AppVersion}-{#Channel}-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\DisplayControl.Reader.exe
CloseApplications=yes
[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
[Icons]
Name: "{group}\DisplayControl Reader"; Filename: "{app}\DisplayControl.Reader.exe"; WorkingDir: "{app}"
#if Channel == "Lite"
[Code]
function InitializeSetup(): Boolean;
var Names: TArrayOfString; I: Integer; Found: Boolean;
begin
  Found := False;
  if RegGetValueNames(HKLM64, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App', Names) then
    for I := 0 to GetArrayLength(Names)-1 do
      if Pos('10.', Names[I]) = 1 then Found := True;
  Result := Found;
  if not Found then MsgBox('This Lite package requires .NET 10 Desktop Runtime (x64). Install it from dotnet.microsoft.com, or download the Full package.', mbError, MB_OK);
end;
#endif
