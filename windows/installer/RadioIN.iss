; Build with Inno Setup 6.7.4 or later. Keep AppId for future updates.
#define ReleaseDir "..\..\RadioIN-Beta-1.3.2"
#ifdef InstallerVerification
  #define RunKey "Software\RadioIN\InstallerVerification\Run"
#else
  #define RunKey "Software\Microsoft\Windows\CurrentVersion\Run"
#endif

[Setup]
#ifdef InstallerVerification
AppId={{E7FBC8AB-8428-4C3A-ACAE-982835D14882}
#else
AppId={{1A790AB8-CA10-43DA-A9E8-059608B4D531}
#endif
AppName=Radio IN
AppVersion=Bêta 1.3.2
AppVerName=Radio IN — Bêta 1.3.2
AppPublisher=Radio IN
AppPublisherURL=https://radio-in.duckdns.org/public/radio_in
AppSupportURL=https://radio-in.duckdns.org/public/radio_in
VersionInfoVersion=1.3.2.0
VersionInfoProductName=Radio IN
VersionInfoProductVersion=1.3.2.0
VersionInfoProductTextVersion=1.3.2-beta
VersionInfoDescription=Installation de Radio IN — Bêta 1.3.2
DefaultDirName={localappdata}\Programs\Radio IN
#ifdef InstallerVerification
DefaultGroupName=Radio IN Verification Beta 1.3
#else
DefaultGroupName=Radio IN
#endif
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
SetupMutex=RadioIN.Public.Setup
CloseApplications=yes
RestartApplications=no
UninstallDisplayName=Radio IN — Bêta 1.3
UninstallDisplayIcon={app}\RadioIN.exe
Uninstallable=yes
SetupIconFile=..\assets\app.ico
WizardSmallImageFile=..\assets\app.png
WizardStyle=modern dark includetitlebar
DisableProgramGroupPage=yes
DisableDirPage=no
DisableWelcomePage=yes
OutputDir=..\..
OutputBaseFilename=RadioIN-Beta-1.3.2-Setup
Compression=lzma2/ultra64
SolidCompression=yes
UsePreviousTasks=yes
SetupLogging=yes

[Languages]
Name: french; MessagesFile: compiler:Languages\French.isl

[Tasks]
Name: desktopicon; Description: "Créer un raccourci sur le Bureau"; GroupDescription: "Raccourcis :"; Flags: unchecked

[Files]
Source: "{#ReleaseDir}\RadioIN.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#ReleaseDir}\RadioIN.exe.config"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#ReleaseDir}\libvlc.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#ReleaseDir}\libvlccore.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#ReleaseDir}\plugins\*"; DestDir: "{app}\plugins"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#ReleaseDir}\VLC-COPYING.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "LIRE-MOI-INSTALLATION.txt"; DestDir: "{app}"; DestName: "LIRE-MOI.txt"; Flags: ignoreversion

[Icons]
Name: "{group}\Radio IN"; Filename: "{app}\RadioIN.exe"; WorkingDir: "{app}"; AppUserModelID: "RadioIN.Player"
Name: "{autodesktop}\Radio IN"; Filename: "{app}\RadioIN.exe"; WorkingDir: "{app}"; AppUserModelID: "RadioIN.Player"; Tasks: desktopicon

[Run]
Filename: "{app}\RadioIN.exe"; Description: "Ouvrir Radio IN"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent

[Code]
function InstalledStartupCommand: String;
begin
  Result := '"' + ExpandConstant('{app}\RadioIN.exe') + '" --startup';
end;

function IsRadioStartupCommand(Command: String): Boolean;
var
  ClosingQuote: Integer;
  Executable: String;
begin
  Result := False;
  Command := Trim(Command);
  if Copy(Command, 1, 1) <> '"' then Exit;
  Delete(Command, 1, 1);
  ClosingQuote := Pos('"', Command);
  if ClosingQuote = 0 then Exit;
  Executable := Copy(Command, 1, ClosingQuote - 1);
  Result := (CompareText(ExtractFileName(Executable), 'RadioIN.exe') = 0) and
    (CompareText(Trim(Copy(Command, ClosingQuote + 1, Length(Command))), '--startup') = 0);
end;

function InitializeSetup: Boolean;
begin
  Result := IsDotNetInstalled(net48, 0);
  if not Result then
    SuppressibleMsgBox('Radio IN nécessite .NET Framework 4.8 ou supérieur.' + #13#10 +
      'Installez-le depuis Microsoft, puis relancez cet installateur.' + #13#10 +
      'https://dotnet.microsoft.com/download/dotnet-framework/net48', mbCriticalError, MB_OK, IDOK);
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ExistingCommand: String;
begin
  if CurStep = ssPostInstall then begin
    { Only migrate the startup option if the player already enabled it. }
    if RegQueryStringValue(HKCU, '{#RunKey}', 'RadioIN', ExistingCommand) then
      if IsRadioStartupCommand(ExistingCommand) then begin
        if not RegWriteStringValue(HKCU, '{#RunKey}', 'RadioIN', InstalledStartupCommand) then
          Log('Impossible de mettre à jour le démarrage Windows. Réactivez cette option dans le lecteur.');
      end;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ExistingCommand: String;
begin
  if CurUninstallStep = usPostUninstall then begin
    { Do not remove another portable copy's startup entry. Keep personal data. }
    if RegQueryStringValue(HKCU, '{#RunKey}', 'RadioIN', ExistingCommand) then
      if CompareText(ExistingCommand, InstalledStartupCommand) = 0 then
        RegDeleteValue(HKCU, '{#RunKey}', 'RadioIN');
  end;
end;
