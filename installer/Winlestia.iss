; Winlestia — installateur Inno Setup 6
; Cible : Windows 11 x64, installation par utilisateur, sans élévation.
; Compilation : ISCC.exe installer\Winlestia.iss  (ou installer\build.ps1)
;
; Au lancement, le setup lit la clé de désinstallation existante :
;   - rien d'installé  → parcours d'installation classique ;
;   - déjà installé    → une page propose « Mettre à jour » ou « Désinstaller ».

#define AppName        "Winlestia"
#ifndef AppVersion
  #error AppVersion non defini : compiler via installer/build.ps1 (/DAppVersion lu dans Directory.Build.props).
#endif
#define AppPublisher   "dj"
#define AppExe         "Panneaux.Glass.exe"
#define AppId          "{{8E2B7C41-5F1A-4F7B-9C3D-6A2E0D5B7C18}"
#define UninstallKey   "Software\Microsoft\Windows\CurrentVersion\Uninstall\{8E2B7C41-5F1A-4F7B-9C3D-6A2E0D5B7C18}_is1"

[Setup]
AppId={#AppId}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
ChangesEnvironment=yes
AppPublisher={#AppPublisher}
VersionInfoVersion={#AppVersion}
VersionInfoProductName={#AppName}
VersionInfoCompany={#AppPublisher}
VersionInfoDescription={#AppName} Setup
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
; Pas d'AppMutex : il ferait échouer Setup et le désinstalleur avant PrepareToInstall / [UninstallRun].
; Le shell est fermé par Close-Winlestia.ps1 avant toute mise à jour ou désinstallation.
CloseApplications=yes
RestartApplications=no
UsePreviousAppDir=yes
UsePreviousTasks=yes
UsePreviousGroup=yes
AllowNoIcons=yes
LicenseFile=..\LICENSE
LanguageDetectionMethod=uilanguage
ShowLanguageDialog=auto

[Languages]
; Anglais en premier : c'est la langue de repli pour toute langue Windows non prise en charge.
; uilanguage : on suit la langue d'affichage de Windows (pas celle des formats régionaux).
; ShowLanguageDialog=auto : la boîte de choix n'apparaît que si la détection est ambiguë
; (aucune langue ne correspond à Windows) ; sinon la langue détectée est utilisée directement.
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "fr"; MessagesFile: "compiler:Languages\French.isl"
Name: "es"; MessagesFile: "compiler:Languages\Spanish.isl"

[CustomMessages]
en.TaskGroup=Options:
fr.TaskGroup=Options :
es.TaskGroup=Opciones:
en.TaskAutostart=Start {#AppName} at sign-in
fr.TaskAutostart=Démarrer {#AppName} à l'ouverture de session
es.TaskAutostart=Iniciar {#AppName} al iniciar sesión
en.TaskDesktopIcon=Create a desktop shortcut
fr.TaskDesktopIcon=Créer un raccourci sur le Bureau
es.TaskDesktopIcon=Crear un acceso directo en el Escritorio
en.TaskStartMenu=Create a Start menu entry
fr.TaskStartMenu=Créer une entrée dans le menu Démarrer
es.TaskStartMenu=Crear una entrada en el menú Inicio
en.TaskCli=Add the "winlestia" command to the user PATH (scripts and tests)
fr.TaskCli=Ajouter la commande « winlestia » au PATH de l'utilisateur (scripts et tests)
es.TaskCli=Añadir el comando «winlestia» al PATH del usuario (scripts y pruebas)
en.TaskTerminal=Integrate {#AppName} with the terminal (colours, clink, PowerShell profiles) - reversible
fr.TaskTerminal=Intégrer {#AppName} au terminal (couleurs, clink, profils PowerShell) — réversible
es.TaskTerminal=Integrar {#AppName} con el terminal (colores, clink, perfiles de PowerShell): reversible
en.IconRestoreTaskbar=Restore the Windows taskbar
fr.IconRestoreTaskbar=Restaurer la barre des tâches Windows
es.IconRestoreTaskbar=Restaurar la barra de tareas de Windows
en.IconUninstall=Uninstall {#AppName}
fr.IconUninstall=Désinstaller {#AppName}
es.IconUninstall=Desinstalar {#AppName}
en.RunLaunch=Launch {#AppName} now
fr.RunLaunch=Lancer {#AppName} maintenant
es.RunLaunch=Iniciar {#AppName} ahora
en.InstalledCaption={#AppName} is already installed
fr.InstalledCaption={#AppName} est déjà installé
es.InstalledCaption={#AppName} ya está instalado
en.InstalledDesc=Version %1 detected in %2.
fr.InstalledDesc=Version %1 détectée dans %2.
es.InstalledDesc=Versión %1 detectada en %2.
en.InstalledHint=Choose what you want to do. Nothing will be changed until you confirm.
fr.InstalledHint=Choisis ce que tu veux faire. Rien ne sera modifié avant que tu valides.
es.InstalledHint=Elige lo que quieres hacer. No se modificará nada hasta que confirmes.
en.ModeReinstall=Reinstall {#AppName} {#AppVersion}
fr.ModeReinstall=Réinstaller {#AppName} {#AppVersion}
es.ModeReinstall=Reinstalar {#AppName} {#AppVersion}
en.ModeUpdate=Update to {#AppName} {#AppVersion}
fr.ModeUpdate=Mettre à jour vers {#AppName} {#AppVersion}
es.ModeUpdate=Actualizar a {#AppName} {#AppVersion}
en.ModeUninstall=Uninstall {#AppName} (profile and settings are kept)
fr.ModeUninstall=Désinstaller {#AppName} (profil et réglages conservés)
es.ModeUninstall=Desinstalar {#AppName} (se conservan el perfil y los ajustes)
en.ConfirmUninstall=Uninstall {#AppName} %1?%n%nThe software will be removed. Your profile and settings in %2 are kept.%n%nThe uninstall window will open, then this setup will close.
fr.ConfirmUninstall=Désinstaller {#AppName} %1 ?%n%nLe logiciel sera retiré. Ton profil et tes réglages dans %2 sont conservés.%n%nLa fenêtre de désinstallation va s'ouvrir, ce setup se fermera ensuite.
es.ConfirmUninstall=¿Desinstalar {#AppName} %1?%n%nSe eliminará el software. Tu perfil y tus ajustes en %2 se conservan.%n%nSe abrirá la ventana de desinstalación y este instalador se cerrará después.
en.UninstallerMissing=The uninstaller could not be found:%n%1
fr.UninstallerMissing=Le désinstalleur est introuvable :%n%1
es.UninstallerMissing=No se encuentra el desinstalador:%n%1
en.ProfileCaption=Your profile
fr.ProfileCaption=Ton profil
es.ProfileCaption=Tu perfil
en.ProfileDesc=Which nickname should {#AppName} display?
fr.ProfileDesc=Quel pseudo afficher dans {#AppName} ?
es.ProfileDesc=¿Qué apodo mostrar en {#AppName}?
en.ProfileHint=This name stays on this computer. You can change it later by editing the profile file.
fr.ProfileHint=Ce nom reste sur cet ordinateur. Tu pourras le changer plus tard en éditant le fichier de profil.
es.ProfileHint=Este nombre se queda en este equipo. Podrás cambiarlo más tarde editando el archivo de perfil.
en.ProfileLabel=Nickname:
fr.ProfileLabel=Pseudo :
es.ProfileLabel=Apodo:
en.TerminalApplyFailed=The terminal theme could not be applied. See Winlestia-Terminal\install-result.json.
fr.TerminalApplyFailed=Le thème du terminal n'a pas pu être appliqué. Consulte Winlestia-Terminal\install-result.json.
es.TerminalApplyFailed=No se pudo aplicar el tema del terminal. Consulta Winlestia-Terminal\install-result.json.
en.TerminalRestoreFailed=The terminal integration could not be removed. Run the setup again or uninstall {#AppName} to remove it.
fr.TerminalRestoreFailed=L'intégration du terminal n'a pas pu être retirée. Relance l'installation ou désinstalle {#AppName} pour la retirer.
es.TerminalRestoreFailed=No se pudo quitar la integración del terminal. Vuelve a ejecutar el instalador o desinstala {#AppName} para quitarla.
en.CloseFailed=Could not close {#AppName} cleanly.
fr.CloseFailed=Impossible de fermer {#AppName} proprement.
es.CloseFailed=No se pudo cerrar {#AppName} correctamente.

[Tasks]
Name: "autostart";  Description: "{cm:TaskAutostart}"; GroupDescription: "{cm:TaskGroup}"; Flags: checkedonce
Name: "desktopicon"; Description: "{cm:TaskDesktopIcon}"; GroupDescription: "{cm:TaskGroup}"; Flags: checkedonce
Name: "startmenu";  Description: "{cm:TaskStartMenu}"; GroupDescription: "{cm:TaskGroup}"; Flags: checkedonce
Name: "cli";        Description: "{cm:TaskCli}"; GroupDescription: "{cm:TaskGroup}"; Flags: checkedonce
Name: "terminal";   Description: "{cm:TaskTerminal}"; GroupDescription: "{cm:TaskGroup}"; Flags: unchecked


[InstallDelete]
; Mise à jour : les deux dossiers sont remplacés en entier, aucun fichier de
; l'ancienne version ne survit à côté des nouveaux.
; Preserve existing preferences/catalogue during upgrade.
Type: filesandordirs; Name: "{app}\Caelestia-Windows"
; winlestia.exe est desormais un fichier unique : retirer les restes de l'ancien publish multi-fichiers.
Type: files; Name: "{app}\Glass-v5\winlestia.dll"
Type: files; Name: "{app}\Glass-v5\winlestia.deps.json"
Type: files; Name: "{app}\Glass-v5\winlestia.runtimeconfig.json"
; Les anciens fonds d'écran fournis sont remplacés par les nouveaux (le dossier utilisateur %LOCALAPPDATA%\Panneaux\Wallpapers n'est jamais touché).
Type: files; Name: "{app}\wallpapers\*"

[Files]
Source: "Close-Winlestia.ps1"; Flags: dontcopy
Source: "Close-Winlestia.ps1"; DestDir: "{app}"; Flags: ignoreversion
; Licence et notices tierces accessibles a l'utilisateur installe
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\THIRD_PARTY_NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\src\backend\LICENSE-CAELESTIA.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\src\backend\CAELESTIA-NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion

; Backend C# auto-suffisant (.NET 10 embarque)
Source: "..\build\payload\Glass-v5\*"; DestDir: "{app}\Glass-v5"; Flags: ignoreversion recursesubdirs createallsubdirs
; Frontend Qt Quick + Python 3.12 embarque + Qt 6.10
Source: "..\build\payload\Caelestia-Windows\*"; DestDir: "{app}\Caelestia-Windows"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "__pycache__,*.pyc,validation-*,tests"

; Collection de fonds d'écran par défaut (Unsplash, hors ligne), lue en lecture seule par le sélecteur Ctrl+Alt+W
Source: "..\assets\wallpapers\*"; DestDir: "{app}\wallpapers"; Flags: ignoreversion

Source: "..\build\payload\Winlestia-Terminal\*"; DestDir: "{app}\Winlestia-Terminal"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\Glass-v5\{#AppExe}"; Parameters: "--qt-shell --show"; WorkingDir: "{app}\Glass-v5"; IconFilename: "{app}\Glass-v5\{#AppExe}"; Tasks: startmenu
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\Glass-v5\{#AppExe}"; Parameters: "--qt-shell --show"; WorkingDir: "{app}\Glass-v5"; IconFilename: "{app}\Glass-v5\{#AppExe}"; Tasks: desktopicon
Name: "{autoprograms}\{cm:IconRestoreTaskbar}"; Filename: "{app}\Glass-v5\RETABLIR_BARRE_WINDOWS.exe"; Tasks: startmenu
Name: "{autoprograms}\{cm:IconUninstall}"; Filename: "{uninstallexe}"; Tasks: startmenu

[Registry]
Root: HKCU; Subkey: "Environment"; ValueType: expandsz; ValueName: "Path"; ValueData: "{olddata};{app}\Glass-v5"; Check: PathMissing() and not PathIsEmpty(); Tasks: cli
Root: HKCU; Subkey: "Environment"; ValueType: expandsz; ValueName: "Path"; ValueData: "{app}\Glass-v5"; Check: PathMissing() and PathIsEmpty(); Tasks: cli
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Panneaux"; ValueData: """{app}\Glass-v5\{#AppExe}"""; Flags: uninsdeletevalue; Tasks: autostart

[Run]

Filename: "{app}\Glass-v5\{#AppExe}"; Parameters: "--qt-shell --show"; WorkingDir: "{app}\Glass-v5"; Description: "{cm:RunLaunch}"; Flags: nowait postinstall skipifsilent runasoriginaluser

[UninstallRun]
; Tâche de session créée par le backend (migration de la clé Run) : retirée avant tout le reste.
Filename: "{sys}\schtasks.exe"; Parameters: "/delete /tn WinlestiaShell /f"; Flags: runhidden waituntilterminated; RunOnceId: "DeleteLogonTask"

Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoLogo -NoProfile -ExecutionPolicy Bypass -File ""{app}\Close-Winlestia.ps1"" -AppRoot ""{app}"""; Flags: runhidden waituntilterminated; RunOnceId: "CloseWinlestia"

Filename: "{app}\Glass-v5\Winlestia.Files.exe"; Parameters: "--restore"; Flags: runhidden waituntilterminated skipifdoesntexist; RunOnceId: "RestoreFolderHandler"

Filename: "{app}\Caelestia-Windows\.venv\Scripts\python.exe"; Parameters: """{app}\Winlestia-Terminal\install.py"" --restore"; Flags: runhidden waituntilterminated; RunOnceId: "RestoreTerminal"

; L'application est arrêtée avant que ses fichiers disparaissent…
; Graceful shutdown and path-scoped fallback handled above.
; …puis la barre des tâches Windows revient, même si le shell est déjà mort.
Filename: "{app}\Glass-v5\RETABLIR_BARRE_WINDOWS.exe"; Flags: runhidden waituntilterminated skipifdoesntexist; RunOnceId: "RestoreTaskbar"

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

function TerminalIntegrated(): Boolean;
begin
  Result := FileExists(ExpandConstant('{localappdata}\Winlestia\terminal-backup\state.json'));
end;

procedure InitializeWizard();
var
  Pseudo: String;
begin
  if Installed then
  begin
    ModePage := CreateInputOptionPage(wpWelcome,
      CustomMessage('InstalledCaption'),
      FmtMessage(CustomMessage('InstalledDesc'), [PrevVersion, PrevDir]),
      CustomMessage('InstalledHint'),
      True, False);
    if CompareText(PrevVersion, '{#AppVersion}') = 0 then
      ModePage.Add(CustomMessage('ModeReinstall'))
    else
      ModePage.Add(CustomMessage('ModeUpdate'));
    ModePage.Add(CustomMessage('ModeUninstall'));
    ModePage.SelectedValueIndex := 0;
  end;

  ProfilePage := CreateInputQueryPage(wpSelectTasks,
    CustomMessage('ProfileCaption'),
    CustomMessage('ProfileDesc'),
    CustomMessage('ProfileHint'));
  ProfilePage.Add(CustomMessage('ProfileLabel'), False);
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
    if MsgBox(FmtMessage(CustomMessage('ConfirmUninstall'), [PrevVersion,
              ExpandConstant('{localappdata}\Panneaux')]),
              mbConfirmation, MB_YESNO) <> IDYES then
    begin
      Result := False;
      Exit;
    end;
    LeavingToUninstall := True;
    if not Exec(UninstallerPath(), '/SILENT', '', SW_SHOWNORMAL, ewNoWait, Code) then
    begin
      LeavingToUninstall := False;
      MsgBox(FmtMessage(CustomMessage('UninstallerMissing'), [UninstallerPath()]), mbError, MB_OK);
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

{ Première installation uniquement : la langue n'est mémorisée pour l'app (mode explicite fr/en/es) que si elle
  diffère de celle que le mode 'auto' de l'app choisirait de toute façon (langue d'affichage de Windows, repli en),
  c'est-à-dire si l'utilisateur a choisi une autre langue à la main. Sinon on n'épingle rien : 'auto' continue de
  suivre Windows. Un fichier existant n'est jamais écrasé, la préférence de l'utilisateur est conservée. }
function AutoLanguage(): String;
begin
  case GetUILanguage and $3FF of
    $0C: Result := 'fr';
    $0A: Result := 'es';
  else
    Result := 'en';
  end;
end;

procedure SaveLanguage();
var
  Path: String;
begin
  if ActiveLanguage() = AutoLanguage() then
    Exit;
  Path := ExpandConstant('{localappdata}\Panneaux\Caelestia\language.json');
  if FileExists(Path) then
    Exit;
  ForceDirectories(ExtractFileDir(Path));
  SaveStringToFile(Path, '{"language": "' + ActiveLanguage() + '"}', False);
end;

procedure SaveProfile();
var
  Dir, Json, Pseudo, PseudoJson: String;
begin
  Pseudo := Trim(ProfilePage.Values[0]);
  if Pseudo = '' then
    Pseudo := GetUserNameString();

  Dir := ExtractFileDir(ProfilePath());
  ForceDirectories(Dir);

  PseudoJson := Pseudo;
  StringChangeEx(PseudoJson, '\', '\\', True);
  StringChangeEx(PseudoJson, '"', '\"', True);

  Json := '{' + #13#10 +
          '  "displayName": "' + PseudoJson + '",' + #13#10 +
          '  "version": "{#AppVersion}",' + #13#10 +
          '  "installedAt": "' + GetDateTimeString('yyyy-mm-dd hh:nn:ss', '-', ':') + '"' + #13#10 +
          '}' + #13#10;
  SaveStringToFile(ProfilePath(), Json, False);
  // Retenu pour la prochaine mise à jour : le champ pseudo arrive déjà rempli.
  RegWriteStringValue(HKCU, 'Software\Winlestia', 'DisplayName', Pseudo);
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  // Mise à jour interactive : la page des tâches s'affiche après la restauration des tâches précédentes (UsePreviousTasks),
  // on coche donc ici 'terminal' si l'intégration est en place. Personne n'est désintégré sans le vouloir.
  if (CurPageID = wpSelectTasks) and TerminalIntegrated() then
    WizardSelectTasks('terminal');
end;

procedure CurStepChanged(CurStep: TSetupStep);
var Code: Integer;
begin
  if CurStep = ssPostInstall then
  begin
    SaveProfile();
    SaveLanguage();
    // Comportement : intégrer si la tâche est cochée ; en mode silencieux (page non affichée) une intégration existante est
    // conservée même sans /TASKS. Le retrait n'a lieu qu'en mode interactif (case décochée), jamais en silencieux.
    if WizardIsTaskSelected('terminal') or (WizardSilent and TerminalIntegrated()) then
    begin
      if (not Exec(ExpandConstant('{app}\Caelestia-Windows\.venv\Scripts\python.exe'),
          '"' + ExpandConstant('{app}\Winlestia-Terminal\install.py') + '"', '', SW_HIDE, ewWaitUntilTerminated, Code)) or (Code <> 0) then
        MsgBox(CustomMessage('TerminalApplyFailed'), mbError, MB_OK);
    end
    else if (not WizardSilent) and TerminalIntegrated() then
    begin
      // Case décochée lors d'une mise à jour : on rend le terminal à son état d'origine.
      if (not Exec(ExpandConstant('{app}\Caelestia-Windows\.venv\Scripts\python.exe'),
          '"' + ExpandConstant('{app}\Winlestia-Terminal\install.py') + '" --restore', '', SW_HIDE, ewWaitUntilTerminated, Code)) or (Code <> 0) then
        MsgBox(CustomMessage('TerminalRestoreFailed'), mbError, MB_OK);
    end;
  end;
end;

function PathIsEmpty(): Boolean;
var Current: String;
begin
  if not RegQueryStringValue(HKCU, 'Environment', 'Path', Current) then Current := '';
  Result := Trim(Current) = '';
end;

function PathMissing(): Boolean;
var Current, Entry, Local, Alt: String;
begin
  if not RegQueryStringValue(HKCU, 'Environment', 'Path', Current) then Current := '';
  Current := ';' + Lowercase(Current) + ';';
  Entry := Lowercase(ExpandConstant('{app}\Glass-v5'));
  Result := Pos(';' + Entry + ';', Current) = 0;
  { Same folder written with %LOCALAPPDATA% }
  Local := Lowercase(ExpandConstant('{localappdata}'));
  if Result and (Copy(Entry, 1, Length(Local)) = Local) then begin
    Alt := '%localappdata%' + Copy(Entry, Length(Local) + 1, MaxInt);
    Result := Pos(';' + Alt + ';', Current) = 0;
  end;
end;

procedure RemoveFromPath();
var Current, Entry: String;
    At: Integer;
begin
  if not RegQueryStringValue(HKCU, 'Environment', 'Path', Current) then Exit;
  Entry := ExpandConstant('{app}\Glass-v5');
  At := Pos(';' + Lowercase(Entry) + ';', ';' + Lowercase(Current) + ';');
  if At = 0 then Exit;
  Current := ';' + Current + ';';
  Delete(Current, At, Length(Entry) + 1);
  Current := Copy(Current, 2, Length(Current) - 2);
  RegWriteExpandStringValue(HKCU, 'Environment', 'Path', Current);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  // Le profil et le cache restent : une réinstallation retrouve le même pseudo.
  if CurUninstallStep = usPostUninstall then
    begin
      RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'Panneaux');
      RemoveFromPath();
    end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var Code: Integer;
begin
  Result := '';
  ExtractTemporaryFile('Close-Winlestia.ps1');
  if (not Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
    '-NoLogo -NoProfile -ExecutionPolicy Bypass -File "' + ExpandConstant('{tmp}\Close-Winlestia.ps1') + '" -AppRoot "' + ExpandConstant('{app}') + '"', '', SW_HIDE, ewWaitUntilTerminated, Code)) or (Code <> 0) then
    Result := CustomMessage('CloseFailed');
end;