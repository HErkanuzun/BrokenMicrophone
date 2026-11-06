; Broken Microphone - kurulum paketi (Inno Setup 6)
; Derleme: paketle.bat  (ya da: ISCC.exe /DAppVersion=1.0 installer\BrokenMicrophone.iss, önce build.bat)
;
; - Program normal kullanıcı olarak çalışır. Kurulum yönetici izni ister: Program Files'a kurar ve telefonun bağlanabilmesi için
;   Windows güvenlik duvarına sadece bu programa, sadece yerel ağdan gelen bağlantı izni ekler (kaldırınca silinir).
; - Program kuruluysa aynı yere üzerine kurar (güncelleme); çalışan kopya önce düzgünce kapatılır.
; - Windows "Uygulamalar" listesinde sürümüyle görünür. Kaldırırken ayarların da silinip silinmeyeceği sorulur.

#ifndef AppVersion
  #define AppVersion "1.0"
#endif
; Windows sürüm bilgisi sadece sayı kabul eder ("1.2-beta.1" -> "1.2")
#ifndef AppNumVersion
  #define AppNumVersion AppVersion + ".0.0"
#endif
#define AppName "Broken Microphone"
#define AppExe "BrokenMicrophone.exe"
#define AppUrl "https://github.com/HErkanuzun/BrokenMicrophone"

[Setup]
; AppId hiç değişmemeli: güncellemenin eski kurulumu bulması buna bağlı
AppId={{D6B999DB-A641-4367-B9DC-C99509B6AC90}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=HErkanuzun
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
AppCopyright=Copyright (c) 2026 HErkanuzun. MIT License.
VersionInfoVersion={#AppNumVersion}
VersionInfoProductName={#AppName}
VersionInfoDescription={#AppName} Setup
DefaultDirName={autopf}\{#AppName}
DisableProgramGroupPage=yes
DisableDirPage=auto
UsePreviousAppDir=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
LicenseFile=..\LICENSE
SetupIconFile=..\src\app.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
OutputDir=..\dist
OutputBaseFilename=BrokenMicrophone-{#AppVersion}-Setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
ShowLanguageDialog=auto

[Languages]
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
turkish.DeleteData=Ayarlar da silinsin mi?%n%n(%1)
english.DeleteData=Also delete the settings?%n%n(%1)
turkish.Firewall=Güvenlik duvarı izni ekleniyor…
english.Firewall=Adding the firewall rule…

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\bin\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion
Source: "..\packaging\README.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; "Windows ile başlat" programın içinden açılır; kaldırınca kayıt silinir
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "BrokenMicrophone"; Flags: uninsdeletevalue dontcreatekey

[Run]
; telefonun bağlanabilmesi için: sadece bu program, sadece yerel ağdaki cihazlar (ağ "Ortak" olsa da)
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""{#AppName}"""; Flags: runhidden; StatusMsg: "{cm:Firewall}"
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=""{#AppName}"" dir=in action=allow program=""{app}\{#AppExe}"" enable=yes profile=any remoteip=localsubnet protocol=TCP"; Flags: runhidden; StatusMsg: "{cm:Firewall}"
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent runasoriginaluser

[UninstallRun]
; Açık kopya varsa önce düzgünce kapatılır
Filename: "{app}\{#AppExe}"; Parameters: "--exit"; Flags: waituntilterminated runhidden; RunOnceId: "CloseApp"
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""{#AppName}"""; Flags: runhidden; RunOnceId: "Firewall"

[Code]
// Dosyalar değiştirilmeden önce çalışan kopya düzgünce kapatılır:
// yeni exe geçici klasörden "--exit" ile çalıştırılır.
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  ExtractTemporaryFile('{#AppExe}');
  Exec(ExpandConstant('{tmp}\{#AppExe}'), '--exit', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := '';
end;

// Kaldırma bitince kullanıcı verisi sorulur (sessiz kaldırmada dokunulmaz)
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    DataDir := ExpandConstant('{userappdata}\BrokenMicrophone');
    if DirExists(DataDir) and not UninstallSilent then
      if MsgBox(FmtMessage(CustomMessage('DeleteData'), [DataDir]), mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
        DelTree(DataDir, True, True, True);
  end;
end;
