; Winlestia — installateur Inno Setup 6
; Cible : Windows 11 x64, installation par utilisateur, sans élévation.
; Compilation : ISCC.exe installer\Winlestia.iss  (ou installer\build.ps1)
;
; Au lancement, le setup lit la clé de désinstallation existante :
;   - rien d'installé  → parcours d'installation classique ;
;   - déjà installé    → une page propose « Mettre à jour » ou « Désinstaller ».

#define AppName        "Winlestia"
#define AppVersion     "0.7.6"
#define AppPublisher   "dj"
#define AppExe         "Panneaux.Glass.exe"
#define AppId          "{{8E2B7C41-5F1A-4F7B-9C3D-6A2E0D5B7C18}"
#define UninstallKey   "Software\Microsoft\Windows\CurrentVersion\Uninstall\{8E2B7C41-5F1A-4F7B-9C3D-6A2E0D5B7C18}_is1"

[Setup]
AppId={#AppId}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
VersionInfoVersion={#AppVersion}
VersionInfoProductName={#AppName}
VersionInfoCompany={#AppPublisher}
VersionInfoDescription=Installateur {#AppName}
DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=no
DisableWelcomePage=no
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern
SetupIconFile=..\assets\winlestia.ico
UninstallDisplayIcon={app}\Glass-v5\{#AppExe}
UninstallDisplayName={#AppName}
Uninstallable=yes
OutputDir=..\build\out
OutputBaseFilename={#AppName}-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
LZMANumBlockThreads=4
MinVersion=10.0.22000
; Le shell de l'utilisateur est fermé proprement avant toute mise à jour de fichiers.
AppMutex=Local\PanneauxGlassV5
CloseApplications=yes
RestartApplications=no
UsePreviousAppDir=yes
UsePreviousTasks=yes
UsePreviousGroup=yes
AllowNoIcons=yes
LicenseFile=..\LICENSE

[Languages]
Name: "fr"; MessagesFile: "compiler:Languages\French.isl"

[Tasks]
Name: "autostart";  Description: "Démarrer {#AppName} à l'ouverture de session"; GroupDescription: "Options :"; Flags: checkedonce
Name: "desktopicon"; Description: "Créer un raccourci sur le Bureau"; GroupDescription: "Options :"; Flags: checkedonce
Name: "startmenu";  Description: "Créer une entrée dans le menu Démarrer"; GroupDescription: "Options :"; Flags: checkedonce


[InstallDelete]
; Mise à jour : les deux dossiers sont remplacés en entier, aucun fichier de
; l'ancienne version ne survit à côté des nouveaux.
; Preserve existing preferences/catalogue during upgrade.
Type: filesandordirs; Name: "{app}\Caelestia-Windows"

[Files]
Source: "Close-Winlestia.ps1"; Flags: dontcopy
Source: "Close-Winlestia.ps1"; DestDir: "{app}"; Flags: ignoreversion

; Backend C# auto-suffisant (.NET 8 embarque)
Source: "..\build\payload\Glass-v5\*"; DestDir: "{app}\Glass-v5"; Flags: ignoreversion recursesubdirs createallsubdirs
; Frontend Qt Quick + Python 3.12 embarque + Qt 6.10
Source: "..\build\payload\Caelestia-Windows\*"; DestDir: "{app}\Caelestia-Windows"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "__pycache__,*.pyc,validation-*,tests"

Source: "..\build\payload\Winlestia-Terminal\*"; DestDir: "{app}\Winlestia-Terminal"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\Glass-v5\{#AppExe}"; Parameters: "--qt-shell --show"; WorkingDir: "{app}\Glass-v5"; IconFilename: "{app}\Glass-v5\{#AppExe}"; Tasks: startmenu
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\Glass-v5\{#AppExe}"; Parameters: "--qt-shell --show"; WorkingDir: "{app}\Glass-v5"; IconFilename: "{app}\Glass-v5\{#AppExe}"; Tasks: desktopicon
Name: "{autoprograms}\Restaurer la barre des tâches Windows"; Filename: "{app}\Glass-v5\RETABLIR_BARRE_WINDOWS.exe"; Tasks: startmenu
Name: "{autoprograms}\Désinstaller {#AppName}"; Filename: "{uninstallexe}"; Tasks: startmenu

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Panneaux"; ValueData: """{app}\Glass-v5\{#AppExe}"""; Flags: uninsdeletevalue; Tasks: autostart

[Run]

Filename: "{app}\Glass-v5\{#AppExe}"; Parameters: "--qt-shell --show"; WorkingDir: "{app}\Glass-v5"; Description: "Lancer {#AppName} maintenant"; Flags: nowait postinstall skipifsilent runasoriginaluser

[UninstallRun]
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoLogo -NoProfile -ExecutionPolicy Bypass -File ""{app}\Close-Winlestia.ps1"" -AppRoot ""{app}"""; Flags: runhidden waituntilterminated; RunOnceId: "CloseWinlestia"

Filename: "{app}\Caelestia-Windows\.venv\Scripts\python.exe"; Parameters: """{app}\Winlestia-Terminal\install.py"" --restore"; Flags: runhidden waituntilterminated; RunOnceId: "RestoreTerminal"

; L'application est arrêtée avant que ses fichiers disparaissent…
; Graceful shutdown and path-scoped fallback handled above.
; …puis la barre des tâches Windows revient, même si le shell est déjà mort.
Filename: "{app}\Glass-v5\RETABLIR_BARRE_WINDOWS.exe"; Flags: runhidden waituntilterminated; RunOnceId: "RestoreTaskbar"

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Code]
var
  ModePage: TInputOptionWizardPage;
  ProfilePage: TInputQueryWizardPage;
  Installed: Boolean;
  PrevVersion, PrevDir: String;
  LeavingToUninstall: Boolean;

function InstalledVersion(): String;
var
  Value: String;
begin
  Value := '';
  if not RegQueryStringValue(HKCU, '{#UninstallKey}', 'DisplayVersion', Value) then
    Value := '';
  Result := Value;
end;

function InstalledDir(): String;
var
  Value: String;
begin
  Value := '';
  if not RegQueryStringValue(HKCU, '{#UninstallKey}', 'Inno Setup: App Path', Value) then
    Value := '';
  Result := Value;
end;

function UninstallerPath(): String;
begin
  Result := AddBackslash(PrevDir) + 'unins000.exe';
end;

function PosFrom(const Sub, Text: String; From: Integer): Integer;
begin
  Result := Pos(Sub, Copy(Text, From, Length(Text)));
  if Result > 0 then
    Result := Result + From - 1;
end;

{ Pseudo déjà choisi : une mise à jour ne doit pas le redemander. }
function ExistingPseudo(): String;
var
  Lines: TStringList;
  Json: String;
  KeyPos, StartPos, EndPos: Integer;
begin
  Result := '';
  if RegQueryStringValue(HKCU, 'Software\Winlestia', 'DisplayName', Result) then
    Exit;
  Result := '';
  if not FileExists(ExpandConstant('{localappdata}\Panneaux\profil.json')) then
    Exit;
  Lines := TStringList.Create;
  try
    Lines.LoadFromFile(ExpandConstant('{localappdata}\Panneaux\profil.json'));
    Json := Lines.Text;
  finally
    Lines.Free;
  end;
  KeyPos := Pos('"displayName"', Json);
  if KeyPos = 0 then
    Exit;
  StartPos := PosFrom('"', Json, KeyPos + Length('"displayName"'));
  if StartPos = 0 then
    Exit;
  EndPos := PosFrom('"', Json, StartPos + 1);
  if EndPos = 0 then
    Exit;
  Result := Copy(Json, StartPos + 1, EndPos - StartPos - 1);
end;

function InitializeSetup(): Boolean;
begin
  PrevVersion := InstalledVersion();
  PrevDir := InstalledDir();
  Installed := (PrevVersion <> '') and (PrevDir <> '') and FileExists(UninstallerPath());
  LeavingToUninstall := False;
  Result := True;
end;

procedure InitializeWizard();
var
  Pseudo: String;
begin
  if Installed then
  begin
    ModePage := CreateInputOptionPage(wpWelcome,
      'Winlestia est déjà installé',
      'Version ' + PrevVersion + ' détectée dans ' + PrevDir + '.',
      'Choisis ce que tu veux faire. Rien ne sera modifié avant que tu valides.',
      True, False);
    if CompareText(PrevVersion, '{#AppVersion}') = 0 then
      ModePage.Add('Réinstaller Winlestia {#AppVersion}')
    else
      ModePage.Add('Mettre à jour vers Winlestia {#AppVersion}');
    ModePage.Add('Désinstaller Winlestia (profil et réglages conservés)');
    ModePage.SelectedValueIndex := 0;
  end;

  ProfilePage := CreateInputQueryPage(wpSelectTasks,
    'Ton profil',
    'Quel pseudo afficher dans Winlestia ?',
    'Ce nom reste sur cet ordinateur. Tu pourras le changer plus tard en éditant le fichier de profil.');
  ProfilePage.Add('Pseudo :', False);
  Pseudo := ExistingPseudo();
  if Pseudo = '' then
    Pseudo := GetUserNameString();
  ProfilePage.Values[0] := Pseudo;
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := False;
  if (ModePage <> nil) and (PageID = ModePage.ID) and (not Installed) then
    Result := True;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  Code: Integer;
begin
  Result := True;
  if Installed and (CurPageID = ModePage.ID) and (ModePage.SelectedValueIndex = 1) then
  begin
    if MsgBox('Désinstaller Winlestia ' + PrevVersion + ' ?' + #13#10 + #13#10 +
              'Le logiciel sera retiré. Ton profil et tes réglages dans ' +
              ExpandConstant('{localappdata}\Panneaux') + ' sont conservés.' + #13#10 + #13#10 +
              'La fenêtre de désinstallation va s''ouvrir, ce setup se fermera ensuite.',
              mbConfirmation, MB_YESNO) <> IDYES then
    begin
      Result := False;
      Exit;
    end;
    LeavingToUninstall := True;
    if not Exec(UninstallerPath(), '/SILENT', '', SW_SHOWNORMAL, ewNoWait, Code) then
    begin
      LeavingToUninstall := False;
      MsgBox('Le désinstalleur est introuvable :' + #13#10 + UninstallerPath(), mbError, MB_OK);
      Result := False;
      Exit;
    end;
    Result := False;
    // L'assistant se retire sans demander confirmation : le désinstalleur prend la main.
    try
      WizardForm.Close;
    except
    end;
  end;
end;

procedure CancelButtonClick(CurPageID: Integer; var Cancel, Confirm: Boolean);
begin
  if LeavingToUninstall then
    Confirm := False;
end;

function ProfilePath(): String;
begin
  Result := ExpandConstant('{localappdata}\Panneaux\profil.json');
end;

procedure SaveProfile();
var
  Dir, Json, Pseudo: String;
begin
  Pseudo := Trim(ProfilePage.Values[0]);
  if Pseudo = '' then
    Pseudo := GetUserNameString();

  Dir := ExtractFileDir(ProfilePath());
  ForceDirectories(Dir);

  Json := '{' + #13#10 +
          '  "displayName": "' + Pseudo + '",' + #13#10 +
          '  "version": "{#AppVersion}",' + #13#10 +
          '  "installedAt": "' + GetDateTimeString('yyyy-mm-dd hh:nn:ss', '-', ':') + '"' + #13#10 +
          '}' + #13#10;
  SaveStringToFile(ProfilePath(), Json, False);
  // Retenu pour la prochaine mise à jour : le champ pseudo arrive déjà rempli.
  RegWriteStringValue(HKCU, 'Software\Winlestia', 'DisplayName', Pseudo);
end;

procedure CurStepChanged(CurStep: TSetupStep);
var Code: Integer;
begin
  if CurStep = ssPostInstall then
  begin
    SaveProfile();
      if (not Exec(ExpandConstant('{app}\Caelestia-Windows\.venv\Scripts\python.exe'),
          '"' + ExpandConstant('{app}\Winlestia-Terminal\install.py') + '"', '', SW_HIDE, ewWaitUntilTerminated, Code)) or (Code <> 0) then
        MsgBox('Le theme du terminal n''a pas pu etre applique. Consulte Winlestia-Terminal\install-result.json.', mbError, MB_OK);
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  // Le profil et le cache restent : une réinstallation retrouve le même pseudo.
  if CurUninstallStep = usPostUninstall then
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'Panneaux');
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var Code: Integer;
begin
  Result := '';
  ExtractTemporaryFile('Close-Winlestia.ps1');
  if (not Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
    '-NoLogo -NoProfile -ExecutionPolicy Bypass -File "' + ExpandConstant('{tmp}\Close-Winlestia.ps1') + '" -AppRoot "' + ExpandConstant('{app}') + '"', '', SW_HIDE, ewWaitUntilTerminated, Code)) or (Code <> 0) then
    Result := 'Impossible de fermer Winlestia proprement.';
end;