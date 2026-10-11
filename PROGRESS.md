# PROGRESS.md — سجل تقدم إكمال e_prescriptions

> **ملاحظة تشغيل الاختبارات (مهمة):** بيئة العمل Linux بدون Visual Studio ولا NuGet، فمش ممكن تشغيل `dotnet test` الحقيقي (net48 + WPF + xunit).
> بدلاً منه بُني مشغّل بديل (خارج المشروع) يترجم ملفات `tests/ePrescriptions.Tests/*.cs` الحقيقية مع طبقات Core/Domain/Data الحقيقية
> فوق SQLite حقيقي (libsqlite3)، مع بدائل صغيرة لـ xunit وFluentAssertions. **مش بديل عن تشغيل Visual Studio/CI على Windows (خطوة 42 و45).**
> الملف `ReportingAndExportTests.cs` (يحتاج ClosedXML/PdfSharp) غير مشمول في المشغّل البديل.
> الأعداد المسجلة تحت = عدد الاختبارات اللي اشتغلت فعلاً في المشغّل البديل.

## عمل سابق قبل هذا البرومبت (إصدار v10) — موثّق بالتفصيل في FIXES.md
نُفّذ قبل كتابة البرومبت، ويغطي جزءاً من خطوات 4 و5 و6 و7 و8 و9. حالة كل خطوة مقابل معيار قبولها:
- **خطوة 4 (منع بيع المنتهي + تحذير 90 يوم):** منفّذة ✅ (فلتر الصلاحية في `GetActiveBatches` + رفض البيع في `SaveInvoice` + تحذير في `SalesViewModel`). اختبارات: `Sale_ShouldSkipExpiredBatches_...` و`ExpiredBatch_EvenIfItIsTheOnlyOne_ShouldNeverBeSold_AndSaleGoesNegative`.
- **خطوة 5 (ربط المرتجع بالفاتورة الأصلية):** منفّذة ✅ (`Invoices.OriginalInvoiceId` + `InvoiceItems.OriginalInvoiceItemId` + التحقق + تحميل الفاتورة في الواجهة). اختبار: `Return_ShouldRequireOriginalInvoice_AndRespectSoldQuantity`.
- **خطوة 6 (شطب/مرتجع للمورد):** منفّذة ✅ (الشاشة + الخصم + التدقيق + حقل السبب بـ Migration 009). اختبارات: `SupplierReturn_ShouldDeductStock_...` و`SupplierReturn_ShouldStoreReason`.
- **خطوة 7 (الرصيد السالب):** منفّذة بتصميم **مختلف عن معيار القبول** — راجع "قرارات معلّقة" تحت. اختبار: `NegativeBalance_AfterPurchase_ShouldKeepTotalEqualToSumOfBatches` (الإجمالي 15 والتشغيلة 15).
- **خطوة 8 (باركود التشغيلة):** منفّذة بصيغة مختلفة (رقمية `99`+رقم التشغيلة بدل `ITEM|id|batch|date`) + تحذير التشغيلة الأقدم — راجع "قرارات معلّقة". اختبار: `BatchLabel_ShouldResolveToSameItemBatchAndExpiry_AndOlderBatchShouldBeDetected`.
- **خطوة 9 (تتبع التشغيلات):** منفّذة ✅ (جدول `InvoiceItemBatches` بأعمدة `BatchId/Units` بدل `BatchNumber/QuantitySmallestUnits`). اختبار: `SaleAcrossTwoBatches_ShouldRecordTwoAllocationRowsWithCorrectQuantities`.

## قرارات معلّقة تحتاج موافقة صاحب المشروع
1. **خطوة 7:** معيار القبول بيقول إن فاتورة شراء 20 وحدة على رصيد -5 تنشئ تشغيلة فيها 20 وحدة والإجمالي 15. ده بيخلي مجموع التشغيلات (20) ≠ إجمالي الصنف (15) ويظهر 5 وحدات وهمية قابلة للبيع.
   المنفّذ حالياً: الإجمالي 15 **والتشغيلة 15** (الـ 5 المباعة بالسالب بتتخصم من التشغيلة الجديدة)، فمجموع التشغيلات = الإجمالي دايماً. لو عايز السلوك الحرفي للمعيار قول وأغيّره.
2. **خطوة 8:** الباركود الرقمي القصير اتختار عشان قارئ الباركود بيكتب كأنه كيبورد وحروف `ITEM|` بتتلخبط لو لغة الكيبورد عربي. التاريخ والصنف بيتجابوا من القاعدة بالتشغيلة.

---

## خطوة 1 — إصلاح حسابات التشغيل الافتراضية الباظة
✅ تمت | تاريخ: 2026-10-11
الملفات المعدّلة: src/ePrescriptions.Data/Migrations/002_SeedData.sql، tests/ePrescriptions.Tests/SessionAndLabelTests.cs، tests/ePrescriptions.Tests/DatabaseMigrationsTests.cs، tests/ePrescriptions.Tests/SetupAndSecurityTests.cs (جديد)
عدد الاختبارات قبل/بعد: 44 (43 ناجح + 1 فاشل = اختبار دخول admin/admin123 اللي كان بيكشف المشكلة) → 45 (كلها ناجحة)
ملخص سطرين: حُذف إدخالا `admin` و`pharmacist` من 002_SeedData.sql (الـ hash المخزّن ما كانش بيطابق أي كلمة مرور — اتأكد بحساب PBKDF2-SHA1 فعلي). أُضيف اختبار `SeedData_ShouldNotInsertAnyUserRows` يتأكد إن الملف مفيهوش أي INSERT في Users.
ملاحظات/قرارات اتخذتها: (1) اختبار `UserSessionService_LoginAndLock` كان بيعتمد على حساب admin من البذرة، فبقى بينشئ المدير بنفسه عبر `SaveStaffUser` قبل الدخول (الاختبار بيتحقق من نفس السلوك). (2) اختبار `DatabaseMigrationsTests` كان بيشترط ≥2 مستخدم؛ بقى يشترط مستخدم واحد بالظبط (Owner) لأن الحذف هو الهدف من الخطوة. (3) حساب Owner لسه بيتنشأ بكلمة مرور ثابتة لحد خطوة 2.

## خطوة 2 — شاشة إعداد أول تشغيل (First-Run Setup Wizard)
✅ تمت | تاريخ: 2026-10-11
الملفات المعدّلة: Data/DatabaseContext.cs، Data/Setup/FirstRunSetupService.cs (جديد)، Data/Migrations/002_SeedData.sql (أصبح فاضياً)، Data/Migrations/DemoData.sql (جديد، مصري على الهيكل النهائي)، App/App.xaml.cs، App/ViewModels/FirstRunSetupViewModel.cs (جديد)، App/Views/FirstRunSetupWindow.xaml(.cs) (جديد)، tests/.../SetupAndSecurityTests.cs
عدد الاختبارات قبل/بعد: 45 → 50 (كلها ناجحة)
ملخص سطرين: التطبيق بقى يستدعي `InitializeAndMigrate(seedData:false)` فمفيش بيانات تجريبية ولا حساب Owner افتراضي. لو مفيش Owner تظهر شاشة تطلب اسم الصيدلية/الرقم الضريبي (اختياريين) وكلمة مرور Owner (8+ وغير الافتراضية وتتأكد مرتين) وخيار "بيانات تجريبية" غير مفعّل افتراضياً. العميل النقدي (رقم 1) بيتنشأ دايماً لأنه سجل أساسي.
ملاحظات/قرارات اتخذتها: (1) نُفّذت معها **خطوة 22**: البيانات التجريبية بقت في `DemoData.sql` مصرية ومكتوبة على الهيكل النهائي (الـ 002 القديم كان بأعمدة Halalas ومينفعش يتحمّل بعد الـ migrations). (2) `InitializeAndMigrate(seedData:true)` فضلت للاختبارات فقط (بيانات تجريبية + Owner بكلمة افتراضية). (3) التثبيتات القديمة اللي فيها Owner بكلمة `ChangeMe@Owner1` لسه محتاجة خطوة 46. (4) في التثبيت النظيف بدون بيانات تجريبية، إعدادات الصيدلية بتتكتب فاضية (بدل القيم الافتراضية الشخصية اللي كانت هتظهر على الفواتير). (5) الواجهة (XAML) ما اتعرضتش على Windows، اتأكدت من سلامتها نحوياً فقط.

## خطوة 3 — فرض وجود وردية مفتوحة قبل أي بيع
✅ تمت | تاريخ: 2026-10-11
الملفات المعدّلة: Data/Repositories/InvoiceRepository.cs، App/ViewModels/SalesViewModel.cs، App/Views/SalesView.xaml، App/ViewModels/StaffViewModel.cs، App/ViewModels/MainViewModel.cs، App/Services/UserSessionService.cs، tests/.../SalesShiftTests.cs (جديد)
عدد الاختبارات قبل/بعد: 50 → 52 (كلها ناجحة)
ملخص سطرين: شلت `?? 1`؛ `SaveInvoice` بيرفض أي بيع/مرتجع لو الوردية مش موجودة أو مقفولة (فمفيش فاتورة ولا حركة مخزون)، وشاشة البيع بتعرض شريط "افتح وردية" برصيد افتتاح. 
ملاحظات/قرارات اتخذتها: (1) اكتشفت إن الكاشير مالوش صلاحية شاشة الموظفين، فكان هيبقى مش قادر يفتح وردية بعد المنع؛ فأضفت زر فتح الوردية في شاشة البيع نفسها. (2) `StaffViewModel` كان بيفتح/يقرأ الوردية للمستخدم رقم 1 وباسم "ماجد القبانى" ثابت؛ بقى يستخدم المستخدم المسجّل دخوله ويحدّث وردية الجلسة (`SetCurrentShift(Shift?)` بقت تقبل null عند الإغلاق). (3) شلت الاسم الشخصي الثابت من `MainViewModel` وبدائل `?? 1` للمستخدم في البيع. (4) المرتجع كمان بيحتاج وردية مفتوحة.

## إغلاق فجوات خطوات 4 و6 و7 و8 و9 (بعد الخطوة 3)
✅ تمت | تاريخ: 2026-10-11
الملفات المعدّلة: Data/Migrations/009_SupplierReturnReason.sql (جديد، إصدار القاعدة = 9)، DatabaseContext.cs، SupplierReturnRepository.cs، ExpiryAndReturnEntities.cs، SupplierReturnsViewModel.cs، SupplierReturnsView.xaml، tests/.../ExpiryReturnsAndLabelsTests.cs
عدد الاختبارات قبل/بعد: 52 → 57 (كلها ناجحة)
ملخص سطرين: إضافة حقل السبب لمستند المرتجع/الشطب + اختبارات المعايير الناقصة لخطوات 4 و7 و8 و9.

## خطوة 10 — فرض الوصفة الطبية
✅ تمت | تاريخ: 2026-10-11
الملفات المعدّلة: Data/Migrations/010_ItemPrescriptionDetails.sql (جديد، إصدار القاعدة = 10)، DatabaseContext.cs، InvoiceAndSalesEntities.cs (حقلا PrescriptionNumber/DoctorName على InvoiceItem)، InvoiceRepository.cs، SalesItemRow.cs، SalesViewModel.cs، InventoryViewModel.cs، InventoryView.xaml، tests/.../PrescriptionTests.cs (جديد)
عدد الاختبارات قبل/بعد: 57 → 60 (كلها ناجحة)
ملخص سطرين: الصنف اللي `IsPrescriptionRequired` بيتمنع بيعه من غير رقم وصفة واسم طبيب (الفحص في المستودع + رسالة واضحة في الواجهة + تنبيه على السطر)، والقيمتان بتتخزنا على بند الفاتورة.
ملاحظات/قرارات اتخذتها: (1) اكتشفت إن مفيش أي مكان في الواجهة يخلّي الصنف "يتطلب وصفة"، فأضفت خانة اختيار في نموذج تعديل الصنف في المخزون (وتتسجل في سجل التدقيق). (2) الرقم والطبيب بيتاخدوا من حقول الوصفة اللي في رأس الفاتورة وبيتنسخوا على كل بند يتطلب وصفة. (3) المرتجع مش بيتطلب وصفة.

## خطوة 11 — مراقبة حد الائتمان في البيع الآجل
✅ تمت | تاريخ: 2026-10-11
الملفات المعدّلة: InvoiceRepository.cs، InvoiceAndSalesEntities.cs (Invoice.CreditLimitOverridden غير مخزّن)، SalesViewModel.cs، tests/.../CreditLimitTests.cs (جديد)
عدد الاختبارات قبل/بعد: 60 → 64 (كلها ناجحة)
ملخص سطرين: (رصيد العميل + المتبقي من الفاتورة الآجلة) لو أكبر من الحد المسموح، البيع بيترفض لأي دور. Admin/Owner بس بيظهرلهم سؤال تأكيد وبعده بيتسجل البيع. حد الائتمان 0 معناه "لا ائتمان".
ملاحظات/قرارات اتخذتها: (1) الفحص في المستودع (داخل الـ Transaction وبرصيد حديث من القاعدة) مش على قائمة العملاء في الشاشة. (2) تجاوز الحد بيتسجّل في التدقيق — راجع الإضافة تحت. (3) الاختبارات بتحقن دالة التأكيد `ConfirmCreditLimitOverride` بدل نافذة MessageBox.

### إضافة على خطوة 11 — تسجيل تجاوز حد الائتمان في LogAudit (بطلب صاحب المشروع)
✅ تمت | تاريخ: 2026-10-11
الملفات المعدّلة: App/ViewModels/SalesViewModel.cs (مستودعا التدقيق كمعاملين اختياريين + `LogCreditLimitOverride`)، tests/.../CreditLimitTests.cs
عدد الاختبارات قبل/بعد: 64 → 66 (كلها ناجحة)
ملخص سطرين: عند موافقة Admin/Owner على تجاوز الحد وحفظ الفاتورة بنجاح، بيتسجل في التدقيق: اسم العميل، حد الائتمان، رصيده قبل وبعد، المبلغ الآجل ورقم الفاتورة، وعلى اسم اللي وافق. Owner في `OwnerAuditLog` وAdmin في `StaffAuditLog` (من غير خلط بين السجلين).
ملاحظات/قرارات اتخذتها: التسجيل بعد نجاح الحفظ فقط (لو فشل ما يتسجلش موافقة وهمية)، والمبالغ بصيغة ثابتة الثقافة (F2) عشان تطلع نفس الشكل على أي إعداد لغة. فاتورة اترفضت من كاشير/صيدلي ما بتتسجلش (مفيش موافقة).

## خطوة 12 — نقل قاعدة البيانات إلى ProgramData
✅ تمت | تاريخ: 2026-10-11
الملفات المعدّلة: Core/Helpers/AppPaths.cs (جديد: مسارات + `LegacyDataMigrator`)، Data/DatabaseContext.cs، Core/Helpers/Logger.cs، App/Services/BackupService.cs، ExcelExportService.cs، PdfReportService.cs، App/App.xaml.cs، installer/e_prescriptions_installer.iss، README.md، tests/.../DataPathsTests.cs (جديد)
عدد الاختبارات قبل/بعد: 66 → 69 (كلها ناجحة)
ملخص سطرين: القاعدة والنسخ الاحتياطية والسجلات والتصدير كلها في `%ProgramData%\ePrescriptions` (`DefaultDatabasePath` بيشاور عليه). عند أول تشغيل بعد التحديث بيتم نسخ `%AppData%\e_prescriptions` (القاعدة + ملفا WAL/SHM + النسخ الاحتياطية) للمسار الجديد من غير حذف القديم ومن غير الكتابة فوق قاعدة جديدة موجودة، وبيتكتب ملف `MIGRATED_TO_PROGRAMDATA.txt` في القديم.
ملاحظات/قرارات اتخذتها: (1) المثبّت بقى بينشئ المجلد بصلاحية `users-modify` وبـ `uninsneveruninstall` (البيانات متتحذفش مع إلغاء التثبيت). (2) لو البرنامج اتشغّل من غير المثبّت، `AppPaths.EnsureDirectory` بيحاول يديّ مجموعة Users صلاحية تعديل وقت إنشاء المجلد (محاولة best-effort، لازم تتجرّب على Windows). (3) **ترحيل بيانات لأكتر من مستخدم ويندوز:** لو كل مستخدم كان عنده قاعدة منفصلة في AppData الخاص بيه، بيتم ترحيل قاعدة أول مستخدم يفتح البرنامج فقط؛ الباقي لازم يتدمج يدوياً (مفيش دمج تلقائي). (4) نسخة الاسترداد من القاعدة القديمة يدوية: انسخ الملف من `%AppData%\e_prescriptions` لو احتجت.

## خطوة 13 — تقوية النسخ الاحتياطي (تسليم على أجزاء)

### 13-أ ✅ الخدمة: VACUUM INTO + قرص خارجي + حد احتفاظ + استعادة مع migrations | تاريخ: 2026-10-11
الملفات المعدّلة: App/Services/BackupService.cs (إعادة كتابة)، Core/Helpers/BackupSettings.cs (جديد)، tests/.../BackupTests.cs (جديد)، tools/linux-test-runner/ (جديد)
عدد الاختبارات قبل/بعد: 69 → 81 (كلها ناجحة)
ملخص سطرين: `CreateBackup` بقى بـ `VACUUM INTO` (متّسق مع القاعدة المفتوحة وWAL) لملف مؤقت + فحص سلامة ثم تسمية؛ و`RunConfiguredBackup` بيكتب نسخة في المجلد الأساسي ونسخة مطابقة على قرص/USB منفصل (ويرفض إن الخارجي = نفس الأساسي)، و`PruneOldBackups` بيحتفظ بعدد محدد من الأحدث بنمط `e_prescriptions_backup_*.db` فقط. `RestoreBackup` بيرفض النسخة الأحدث من البرنامج، بياخد نسخة وقائية، بيستبدل الملف بعد تفريغ الاتصالات وحذف WAL/SHM، ثم `InitializeAndMigrate(seedData:false)` يطبّق الـ migrations الناقصة ويتحقق إن `user_version == LatestSchemaVersion`، وبيرجّع القاعدة السابقة تلقائياً لو فشل.
ملاحظات/قرارات اتخذتها: (1) إعدادات النسخ (مجلد أساسي/خارجي/عدد الاحتفاظ/تلقائي بعد الوردية) في ملف مستقل `backup_settings.ini` وليس جدول الإعدادات، فالاستعادة ما ترجّعهاش لقيم قديمة (هذا تفسيري لبند "عزل ملف الإعدادات"). (2) الحذف بالاسم (التاريخ في الاسم) مش بتاريخ الملف. (3) أقل عدد احتفاظ = 1، والافتراضي 14. (4) اسم النسخة لو اتكرر في نفس الثانية بياخد لاحقة `_01`. (5) بيئة هذه الجلسة بُني فيها مشغّل اختبارات جديد على .NET 8 فوق libsqlite3 (محفوظ في `tools/linux-test-runner`، شوف README فيه) لأن المشغّل القديم ضاع مع الجلسة السابقة؛ ما زال بديلاً عن Visual Studio/CI (خطوة 42 و45).

### 13-ب ✅ الواجهة + النسخ التلقائي بعد إغلاق الوردية + عزل ملف الإعدادات | تاريخ: 2026-10-11
الملفات المعدّلة: App/ViewModels/StaffViewModel.cs، App/ViewModels/SettingsViewModel.cs، App/Views/SettingsView.xaml، tools/linux-test-runner/runner.csproj، tests/.../AutoBackupAfterShiftTests.cs (جديد)
عدد الاختبارات قبل/بعد: 81 → 84 (كلها ناجحة)
ملخص سطرين: `StaffViewModel.CloseShift` بياخد نسخة تلقائية (`RunAfterShiftBackup`) بعد إغلاق الوردية؛ فشل النسخ ما بيلغيش الإغلاق بس بيظهر تنبيه ⚠. شاشة الإعدادات فيها: مجلد النسخ، مجلد القرص الخارجي، عدد الاحتفاظ، خانة النسخ التلقائي، وزر حفظ (بيكتب `backup_settings.ini`)، وزر "نسخة فورية" بقى بيستخدم نفس الإعدادات.
ملاحظات/قرارات اتخذتها: (1) `SettingsViewModel.cs` وملف الـ XAML **ما اتترجموش في المشغّل البديل** (بيعتمدوا على WPF/طابعات)، فتعديلاتهم متراجعة بالقراءة فقط وتحتاج بناء فعلي على Windows (خطوة 42). (2) مفيش زر "استعراض" للمجلد الخارجي (يحتاج WinForms)؛ المسار بيتكتب يدوياً. (3) تنبيه فشل النسخ بيعدي عبر `BackupWarningNotifier` عشان الاختبارات ما تفتحش MessageBox. (4) المتبقي بنص الخطوة 41 (جدولة يومية فعلية + اختبار استعادة دوري + تنبيه) مؤجل لخطوته.

## خطوة 13 — ✅ تمت بالكامل | تاريخ: 2026-10-11
معيار القبول (اختبار نسخ ثم استعادة يتأكد من سلامة البيانات وأن LatestSchemaVersion صحيح بعد الاستعادة): `BackupThenRestore_ShouldBringBackOriginalData_AndLatestSchemaVersion` و`Restore_OfOlderSchemaBackup_ShouldApplyMissingMigrations`.
عدد الاختبارات قبل/بعد الخطوة كلها: 69 → 84 (كلها ناجحة)

## خطوة 14 — توحيد اسم الـ Mutex مع المثبّت
✅ تمت | تاريخ: 2026-10-11
الملفات المعدّلة: Core/Helpers/SingleInstanceMutex.cs، tests/.../MutexNameTests.cs (جديد)
عدد الاختبارات قبل/بعد: 84 → 85 (كلها ناجحة)
ملخص سطرين: الاسم الموحّد `e_prescriptions_SingleInstance_Mutex_9F82A41C` بقى ثابت `MutexBaseName` في الكود، والمثبّت (`CheckForMutexes`) ما اتغيّرش لأنه كان بالاسم ده. الاختبار بيقرأ ملف `.iss` ويتأكد إن كل استدعاءات `CheckForMutexes` مطابقة حرفياً للثابت، وإن الاسم القديم اختفى.
ملاحظات/قرارات اتخذتها: التطبيق بيفتح الـ Mutex بالاسم `Global\` + الاسم الأساسي (عشان يغطي كل جلسات ويندوز)، والمثبّت بيفحص الاسم الأساسي بدون البادئة. **لازم يتأكد على Windows (خطوة 42)** إن Inno Setup فعلاً بيكتشف الـ Mutex ذي البادئة `Global\`؛ لو لا، يتغيّر اسم المثبّت لـ `Global\...`.

## خطوة 15 — توليد القيود المحاسبية فعلياً
✅ تمت | تاريخ: 2026-10-11
الملفات المعدّلة: Data/Repositories/JournalPosting.cs (جديد)، InvoiceRepository.cs، PurchaseAndSupplierRepositories.cs، tests/.../JournalPostingTests.cs (جديد)
عدد الاختبارات قبل/بعد: 85 → 93 (كلها ناجحة)
ملخص سطرين: `SaveInvoice` و`SavePurchaseInvoice` بيولّدوا قيود اليومية داخل نفس الـ Transaction (فشل القيد = إلغاء المستند كله). بيع: مدين نقدية/شبكة/تأمين (المدفوع) + عملاء (المتبقي)، دائن إيرادات المبيعات (الصافي) + ضريبة القيمة المضافة؛ مرتجع: العكس بحساب مردودات المبيعات؛ شراء: مدين مخزون (الإجمالي)، دائن نقدية (المدفوع) + موردين (المتبقي). المصروف كان بيولّد قيده أصلاً (اختبار تأكيد أضيف).
ملاحظات/قرارات اتخذتها: (1) جدول القيود صف واحد = مدين واحد/دائن واحد، فالقيد المتعدد الأطراف بيتفكّك لصفوف بأرقام `JV-<رقم المستند>-01`، `-02`..، والمجموعان لازم يتساويا وإلا استثناء. (2) المتبقي/الآجل بيُؤخذ من `RemainingPiastres`، والباقي من الإجمالي نقدي (فالباقي الزائد/الفكة ما بيدخلش في القيد). (3) **غير مشمول:** تكلفة البضاعة المباعة، وسندات القبض/الصرف للعملاء والموردين، وإلغاء الفاتورة (خطوة 16 لازم تعكس قيدها)، والقيود ما بتتولّدش بأثر رجعي للفواتير القديمة. (4) تقارير الأرباح/الزكاة لم تتغيّر ولا تقرأ من القيود حالياً (راجع خطوة 47).

---
## خطوة 16 — إمكانية إلغاء فاتورة
✅ تمت | تاريخ: 2026-10-11
الملفات المعدّلة: Data/Migrations/011_InvoiceStatusAndCancellation.sql (جديد، إصدار القاعدة = 11)، Domain/Enums/DomainEnums.cs، Domain/Entities/InvoiceAndSalesEntities.cs، Data/DatabaseContext.cs، Data/Repositories/InvoiceRepository.cs، Data/Repositories/JournalPosting.cs، Data/Repositories/ReportRepository.cs، App/ViewModels/SalesViewModel.cs، App/Views/SalesView.xaml، tests/ePrescriptions.Tests/InvoiceCancellationTests.cs (جديد)
عدد الاختبارات قبل/بعد: 93 → 100 (كلها ناجحة)
ملخص سطرين: إضافة عمود Status لجدول Invoices (1=Active، 2=Cancelled) وإمكانية إلغاء الفاتورة بصلاحية Admin/Owner يرجّع المخزون ويعكس رصيد العميل ومجاميع الوردية وقيد اليومية المحاسبي. زِر وأمر "إلغاء فاتورة" في شاشة المبيعات يظهر للمدير وصاحب الصيدلية فقط مع تسجيل العملية في سجل التدقيق المناسب (Owner في سجله وAdmin في سجل الموظفين).
ملاحظات/قرارات اتخذتها: (1) رُفض إلغاء أي فاتورة مسجل عليها مرتجعات سارية لمنع تضارب الكميات المحاسبية. (2) تم عكس قيد اليومية تلقائياً برقم CNL-رقم الفاتورة داخل نفس الـ Transaction. (3) تقارير المبيعات والأداء في ReportRepository أصبحت تستبعد الفواتير الملغاة تلقائياً. (4) تم فحص وجود عمود Status قبل تشغيل Migration 011 لتجنب أخطاء تكرار الأعمدة في حال استعادة نسخ تجريبية قديمة.

---
## الخطوة التالية: خطوة 17 — تصحيح ترقيم الفواتير
