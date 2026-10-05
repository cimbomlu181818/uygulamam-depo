; Depo Takip - Inno Setup kurulum betiği
; Bu dosyayı çözüm klasörüne (DEPO DURUMU.slnx dosyasının yanına) DepoTakip.iss adıyla kaydet.

#define MyAppName "Depo Takip"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Çorum Bilgi Teknolojileri ve Haberleşme Şb. Md."
#define MyAppExeName "Depo Takip.exe"
#define BuildFolder "DEPO DURUMU\bin\Release"

[Setup]
AppId={{DB7CA56F-C45B-44C5-864F-59EB2F9A96F9}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=Cikti
OutputBaseFilename=DepoTakip_Kurulum_{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
SetupIconFile=DEPO DURUMU\DepoTakip.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}

[Languages]
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"

[Tasks]
Name: "desktopicon"; Description: "Masaüstüne kısayol oluştur"; GroupDescription: "Ek kısayollar:"

[Files]
; Release klasöründeki her şey (exe, dll'ler, x86 ve x64 klasörleri) kurulum paketine girer.
Source: "{#BuildFolder}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Depo Takip'i şimdi çalıştır"; Flags: nowait postinstall skipifsilent

[Code]
function IsDotNet48Installed: Boolean;
var
  ReleaseValue: Cardinal;
begin
  Result := RegQueryDWordValue(HKLM, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', ReleaseValue)
            and (ReleaseValue >= 528040);
end;

function InitializeSetup: Boolean;
begin
  Result := True;
  if not IsDotNet48Installed then
  begin
    MsgBox('Depo Takip için .NET Framework 4.8 gerekir. Bu bilgisayarda yüklü görünmüyor.' + #13#10 +
           'Önce Microsoft''un sitesinden .NET Framework 4.8 çalışma zamanını kurup sonra bu kurulumu yeniden çalıştır.',
           mbError, MB_OK);
    Result := False;
  end;
end;
