; =====================================================================
; سكريبت تثبيت برنامج e_prescriptions لإدارة الصيدليات والوصفات الطبية
; متوافق مع Inno Setup 6.x
; يدعم Windows 7 SP1، 8.1، 10، و 11 بنسختي 32 بت و 64 بت (AnyCPU)
; =====================================================================

#define MyAppName "e_prescriptions"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "ماجد القبانى"
#define MyAppExeName "ePrescriptions.exe"
#define MyAppAssocName "ePrescriptions Data"
#define MyAppAssocExt ".eprx"

[Setup]
; الهوية الأساسية
AppId={{9F82A41C-328E-45A7-A279-99E8F8B4C011}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\e_prescriptions
DefaultGroupName=e_prescriptions
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
OutputDir=..\dist\installer
OutputBaseFilename=e_prescriptions_Setup_v1.0.0

; متطلبات نظام التشغيل الصارمة: Windows 7 Service Pack 1 فما فوق
MinVersion=6.1sp1

; دعم معماريتي 32 بت و 64 بت
ArchitecturesInstallIn64BitMode=x64
ArchitecturesAllowed=x86 x64

; الواجهة واللغة العربية
WizardStyle=modern
ShowLanguageDialog=no

; صلاحيات المسؤول لتثبيت الخطوط والخدمات في Program Files
PrivilegesRequired=admin

[Languages]
Name: "arabic"; MessagesFile: "compiler:Languages\Arabic.isl"

[Messages]
arabic.WelcomeLabel1=مرحباً بك في مثبت برنامج [name]
arabic.WelcomeLabel2=سيقوم هذا المعالج بتثبيت نظام إدارة الصيدليات المتكامل [name] الإصدار [version] على جهازك.%n%nالبرنامج يعمل محلياً بالكامل بدون إنترنت، ويدعم الربط الإلكتروني وطباعة الإيصالات والباركود.

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: checkedonce

[Files]
; 1) الملف التنفيذي الرئيسي ومكتبات المشروع
Source: "..\src\ePrescriptions.App\bin\Release\net48\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb,*.xml"

; 2) ملفات SQLite الأصلية (Native Interop DLLs) - حاسمة للعمل في AnyCPU على 32 و 64 بت
; يتم نسخها بجانب البرنامج داخل مجلدي x86 و x64 حتى يتمكن محرك SQLite من تحميل المعمارية الصحيحة تلقائياً
Source: "..\src\ePrescriptions.App\bin\Release\net48\x86\SQLite.Interop.dll"; DestDir: "{app}\x86"; Flags: ignoreversion
Source: "..\src\ePrescriptions.App\bin\Release\net48\x64\SQLite.Interop.dll"; DestDir: "{app}\x64"; Flags: ignoreversion; Check: Is64BitInstallMode

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{group}\إلغاء تثبيت {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Dirs]
; مجلد البيانات المشترك (قاعدة البيانات والنسخ الاحتياطية والسجلات والتصدير) في ProgramData
; بصلاحيات تعديل لكل مستخدمي الجهاز؛ لا يُحذف عند إلغاء التثبيت حفاظاً على البيانات
Name: "{commonappdata}\ePrescriptions"; Permissions: users-modify; Flags: uninsneveruninstall
Name: "{commonappdata}\ePrescriptions\backups"; Permissions: users-modify; Flags: uninsneveruninstall
Name: "{commonappdata}\ePrescriptions\logs"; Permissions: users-modify; Flags: uninsneveruninstall
Name: "{commonappdata}\ePrescriptions\exports"; Permissions: users-modify; Flags: uninsneveruninstall

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Code]
// التحقق من تثبيت .NET Framework 4.8 على نظام التشغيل
function IsNet48Installed(): Boolean;
var
  InstallValue: Cardinal;
  ReleaseValue: Cardinal;
  Success: Boolean;
begin
  Result := False;
  // فحص سجل النظام لنظام .NET Framework 4.5+
  Success := RegQueryDWordValue(HKLM, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', ReleaseValue);
  if Success then
  begin
    // رقم الإصدار 528040 يمثل .NET Framework 4.8
    if ReleaseValue >= 528040 then
      Result := True;
  end;
end;

// منع التثبيت فوق نسخة تعمل حالياً عبر فحص الـ Mutex
function InitializeSetup(): Boolean;
begin
  Result := True;

  // التحقق من عدم تشغيل البرنامج حالياً
  if CheckForMutexes('e_prescriptions_SingleInstance_Mutex_9F82A41C') then
  begin
    MsgBox('برنامج e_prescriptions يعمل حالياً في الخلفية.' + #13#10 +
           'يرجى إغلاق البرنامج أولاً قبل متابعة التثبيت أو التحديث.', mbError, MB_OK);
    Result := False;
    Exit;
  end;

  // التحقق من توفر .NET Framework 4.8
  if not IsNet48Installed() then
  begin
    if MsgBox('يتطلب تشغيل نظام e_prescriptions توفر .NET Framework 4.8 على هذا الجهاز.' + #13#10 +
              'هل ترغب في تنزيله وتثبيته الآن من مايكروسوفت؟', mbConfirmation, MB_YESNO) = IDYES then
    begin
      // فتح رابط التحميل الرسمي لـ .NET Framework 4.8
      ShellExec('open', 'https://go.microsoft.com/fwlink/?linkid=2088631', '', '', SW_SHOWNORMAL, ewNoWait, InstallValue);
    end;
    Result := False;
    Exit;
  end;
end;

// إيقاف البرنامج عند الرغبة في إزالة التثبيت
function InitializeUninstall(): Boolean;
begin
  Result := True;
  if CheckForMutexes('e_prescriptions_SingleInstance_Mutex_9F82A41C') then
  begin
    MsgBox('برنامج e_prescriptions يعمل حالياً.' + #13#10 +
           'يرجى إغلاق البرنامج بالكامل قبل البدء في إزالة التثبيت.', mbError, MB_OK);
    Result := False;
  end;
end;
