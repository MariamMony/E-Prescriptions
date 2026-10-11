using System;
using System.Linq;
using ePrescriptions.App.Services;
using ePrescriptions.App.ViewModels;
using ePrescriptions.Data;
using ePrescriptions.Data.Repositories;
using ePrescriptions.Domain.Entities;
using ePrescriptions.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace ePrescriptions.Tests
{
    /// <summary>
    /// خطوة 16: إمكانية إلغاء فاتورة (Active / Cancelled) مع إرجاع المخزون وتسجيل العملية في سجل التدقيق
    /// </summary>
    public class InvoiceCancellationTests
    {
        private class TestContext
        {
            public DatabaseContext Db = new DatabaseContext(":memory:");
            public ItemRepository Items = null!;
            public InvoiceRepository Invoices = null!;
            public CustomerRepository Customers = null!;
            public ShiftRepository Shifts = null!;
            public UserRepository Users = null!;
            public OwnerAuditLogRepository OwnerAudit = null!;
            public StaffAuditLogRepository StaffAudit = null!;
            public int ShiftId;
            public int ItemId;
            public User AdminUser = null!;
            public User OwnerUser = null!;
            public User CashierUser = null!;

            public TestContext()
            {
                Db.InitializeAndMigrate(seedData: true);
                Items = new ItemRepository(Db);
                Invoices = new InvoiceRepository(Db, Items);
                Customers = new CustomerRepository(Db);
                Shifts = new ShiftRepository(Db);
                Users = new UserRepository(Db);
                OwnerAudit = new OwnerAuditLogRepository(Db);
                StaffAudit = new StaffAuditLogRepository(Db);

                // إنشاء مستخدمين للاختبار
                int adminId = Users.SaveStaffUser(0, "admin_t", "مدير الاختبار", "Pass@1234", UserRole.Admin, true);
                AdminUser = Users.GetAll(includeOwner: true).First(u => u.Id == adminId);

                int cashierId = Users.SaveStaffUser(0, "cashier_t", "كاشير الاختبار", "Pass@1234", UserRole.Cashier, true);
                CashierUser = Users.GetAll(includeOwner: true).First(u => u.Id == cashierId);

                OwnerUser = Users.GetAll(includeOwner: true).First(u => u.Role == UserRole.Owner);

                ShiftId = Shifts.OpenShift(AdminUser.Id, AdminUser.FullName, 10000);

                ItemId = Items.SaveItem(new Item
                {
                    Code = "CNL-ITEM",
                    Barcode = "6221112223334",
                    NameAr = "دواء تجربة الإلغاء",
                    StripsPerBox = 2,
                    PiecesPerStrip = 10,
                    RetailPriceBoxPiastres = 4000,
                    CostPriceBoxPiastres = 2500,
                    IsActive = true
                });

                Items.AddBatch(new ItemBatch
                {
                    ItemId = ItemId,
                    BatchNumber = "B-CNL-01",
                    ExpiryDate = DateTime.Today.AddDays(180),
                    RemainingSmallestUnits = 50,
                    CostPricePerSmallestUnitPiastres = 125
                });
            }

            public Invoice CreateSale(int units, PaymentMethod method = PaymentMethod.Cash, int? customerId = null, long pricePerUnit = 200)
            {
                long total = units * pricePerUnit;
                var inv = new Invoice
                {
                    InvoiceNumber = Invoices.GenerateNextInvoiceNumber(InvoiceType.Sales),
                    Type = InvoiceType.Sales,
                    Status = InvoiceStatus.Active,
                    CustomerId = customerId,
                    CustomerName = customerId.HasValue ? Customers.GetById(customerId.Value)?.Name ?? "عميل" : "عميل نقدي",
                    PaymentMethod = method,
                    UserId = AdminUser.Id,
                    UserName = AdminUser.FullName,
                    ShiftId = ShiftId,
                    SubtotalPiastres = total,
                    TotalPiastres = total,
                    PaidPiastres = method == PaymentMethod.OnAccount ? 0 : total,
                    RemainingPiastres = method == PaymentMethod.OnAccount ? total : 0,
                    Items =
                    {
                        new InvoiceItem
                        {
                            ItemId = ItemId,
                            ItemCode = "CNL-ITEM",
                            ItemNameAr = "دواء تجربة الإلغاء",
                            UnitLevel = UnitLevel.Piece,
                            QuantitySold = units,
                            SmallestUnitsCount = units,
                            UnitPricePiastres = pricePerUnit,
                            TotalPiastres = total
                        }
                    }
                };
                return inv;
            }
        }

        [Fact]
        public void CancelInvoice_ByAdmin_ShouldRestoreStock_AndRecordStaffAuditLog()
        {
            var ctx = new TestContext();
            var sale = ctx.CreateSale(15); // بيع 15 وحدة نقدي
            ctx.Invoices.SaveInvoice(sale, out string err).Should().BeTrue();

            // التحقق من خصم المخزون بعد البيع
            ctx.Items.GetById(ctx.ItemId)!.TotalStockSmallestUnits.Should().Be(35);

            // إلغاء الفاتورة بواسطة Admin
            bool cancelled = ctx.Invoices.CancelInvoice(
                sale.Id,
                ctx.AdminUser.Id,
                ctx.AdminUser.FullName,
                ctx.AdminUser.Role,
                out string cancelErr,
                ownerAuditRepo: ctx.OwnerAudit,
                staffAuditRepo: ctx.StaffAudit);

            cancelled.Should().BeTrue(cancelErr);

            // 1) التحقق من عودة المخزون بالكامل
            ctx.Items.GetById(ctx.ItemId)!.TotalStockSmallestUnits.Should().Be(50);
            var batches = ctx.Items.GetActiveBatches(ctx.ItemId);
            batches.Sum(b => b.RemainingSmallestUnits).Should().Be(50);

            // 2) التحقق من حالة الفاتورة
            var reloaded = ctx.Invoices.GetInvoiceById(sale.Id);
            reloaded.Should().NotBeNull();
            reloaded!.Status.Should().Be(InvoiceStatus.Cancelled);

            // 3) التحقق من تسجيل العملية في StaffAuditLog
            var staffEntries = ctx.StaffAudit.GetRecent(10);
            var log = staffEntries.FirstOrDefault(e => e.EntityType == "Invoices" && e.EntityId == sale.Id.ToString());
            log.Should().NotBeNull();
            log!.ActorUserId.Should().Be(ctx.AdminUser.Id);
            log.ActorUserName.Should().Be(ctx.AdminUser.FullName);
            log.OldValue.Should().Contain("Active");
            log.NewValue.Should().Contain("ملغاة");

            // 4) التحقق من عدم تسجيل عملية المدير في سجل Owner الخاص
            var ownerEntries = ctx.OwnerAudit.GetRecent(10);
            ownerEntries.Any(e => e.EntityType == "Invoices" && e.EntityId == sale.Id.ToString()).Should().BeFalse();
        }

        [Fact]
        public void CancelInvoice_ByOwner_ShouldRecordInOwnerAuditLog_AndNotStaffAuditLog()
        {
            var ctx = new TestContext();
            var sale = ctx.CreateSale(10);
            ctx.Invoices.SaveInvoice(sale, out _).Should().BeTrue();
            ctx.Items.GetById(ctx.ItemId)!.TotalStockSmallestUnits.Should().Be(40);

            // إلغاء بواسطة Owner
            bool cancelled = ctx.Invoices.CancelInvoice(
                sale.Id,
                ctx.OwnerUser.Id,
                ctx.OwnerUser.FullName,
                ctx.OwnerUser.Role,
                out string cancelErr,
                ownerAuditRepo: ctx.OwnerAudit,
                staffAuditRepo: ctx.StaffAudit);

            cancelled.Should().BeTrue(cancelErr);

            // عودة المخزون
            ctx.Items.GetById(ctx.ItemId)!.TotalStockSmallestUnits.Should().Be(50);

            // مسجل في OwnerAuditLog
            var ownerEntries = ctx.OwnerAudit.GetRecent(10);
            var log = ownerEntries.FirstOrDefault(e => e.EntityType == "Invoices" && e.EntityId == sale.Id.ToString());
            log.Should().NotBeNull();
            log!.OwnerUserId.Should().Be(ctx.OwnerUser.Id);

            // غير مسجل في StaffAuditLog
            var staffEntries = ctx.StaffAudit.GetRecent(10);
            staffEntries.Any(e => e.EntityType == "Invoices" && e.EntityId == sale.Id.ToString()).Should().BeFalse();
        }

        [Fact]
        public void CancelInvoice_OnAccountSale_ShouldRevertCustomerBalance()
        {
            var ctx = new TestContext();
            int custId = 2; // عميل آجل
            long startBalance = ctx.Customers.GetById(custId)!.CurrentBalancePiastres;

            var sale = ctx.CreateSale(10, PaymentMethod.OnAccount, custId, pricePerUnit: 300); // 3000 قرش
            ctx.Invoices.SaveInvoice(sale, out _).Should().BeTrue();

            ctx.Customers.GetById(custId)!.CurrentBalancePiastres.Should().Be(startBalance + 3000);

            // إلغاء الفاتورة
            ctx.Invoices.CancelInvoice(sale.Id, ctx.AdminUser.Id, ctx.AdminUser.FullName, ctx.AdminUser.Role, out _,
                staffAuditRepo: ctx.StaffAudit).Should().BeTrue();

            // رجوع رصيد العميل لما كان عليه
            ctx.Customers.GetById(custId)!.CurrentBalancePiastres.Should().Be(startBalance);
            ctx.Items.GetById(ctx.ItemId)!.TotalStockSmallestUnits.Should().Be(50);
        }

        [Fact]
        public void CancelInvoice_ByCashier_ShouldBeRejected()
        {
            var ctx = new TestContext();
            var sale = ctx.CreateSale(5);
            ctx.Invoices.SaveInvoice(sale, out _).Should().BeTrue();

            bool cancelled = ctx.Invoices.CancelInvoice(
                sale.Id,
                ctx.CashierUser.Id,
                ctx.CashierUser.FullName,
                ctx.CashierUser.Role,
                out string err);

            cancelled.Should().BeFalse();
            err.Should().Contain("مدير النظام");

            // المخزون لم يتغير
            ctx.Items.GetById(ctx.ItemId)!.TotalStockSmallestUnits.Should().Be(45);
            ctx.Invoices.GetInvoiceById(sale.Id)!.Status.Should().Be(InvoiceStatus.Active);
        }

        [Fact]
        public void CancelInvoice_WhenReturnsExist_ShouldBeRejected()
        {
            var ctx = new TestContext();
            var sale = ctx.CreateSale(10);
            ctx.Invoices.SaveInvoice(sale, out _).Should().BeTrue();

            // عمل مرتجع لـ 3 وحدات
            var ret = new Invoice
            {
                InvoiceNumber = ctx.Invoices.GenerateNextInvoiceNumber(InvoiceType.Return),
                Type = InvoiceType.Return,
                OriginalInvoiceId = sale.Id,
                CustomerName = "عميل نقدي",
                PaymentMethod = PaymentMethod.Cash,
                UserId = ctx.AdminUser.Id,
                UserName = ctx.AdminUser.FullName,
                ShiftId = ctx.ShiftId,
                TotalPiastres = 600,
                PaidPiastres = 600,
                Items =
                {
                    new InvoiceItem
                    {
                        ItemId = ctx.ItemId,
                        ItemCode = "CNL-ITEM",
                        ItemNameAr = "دواء تجربة الإلغاء",
                        UnitLevel = UnitLevel.Piece,
                        QuantitySold = 3,
                        SmallestUnitsCount = 3,
                        UnitPricePiastres = 200,
                        TotalPiastres = 600,
                        OriginalInvoiceItemId = sale.Items[0].Id
                    }
                }
            };
            ctx.Invoices.SaveInvoice(ret, out _).Should().BeTrue();

            // محاولة إلغاء الفاتورة الأصلية يجب أن تفشل لوجود مرتجع
            bool cancelled = ctx.Invoices.CancelInvoice(
                sale.Id,
                ctx.AdminUser.Id,
                ctx.AdminUser.FullName,
                ctx.AdminUser.Role,
                out string err);

            cancelled.Should().BeFalse();
            err.Should().Contain("مرتجع");
        }

        [Fact]
        public void CancelInvoice_AlreadyCancelled_ShouldFail()
        {
            var ctx = new TestContext();
            var sale = ctx.CreateSale(5);
            ctx.Invoices.SaveInvoice(sale, out _).Should().BeTrue();

            ctx.Invoices.CancelInvoice(sale.Id, ctx.AdminUser.Id, ctx.AdminUser.FullName, ctx.AdminUser.Role, out _).Should().BeTrue();

            // محاولة ثانية
            bool cancelledAgain = ctx.Invoices.CancelInvoice(
                sale.Id,
                ctx.AdminUser.Id,
                ctx.AdminUser.FullName,
                ctx.AdminUser.Role,
                out string err);

            cancelledAgain.Should().BeFalse();
            err.Should().Contain("ملغاة بالفعل");
        }

        [Fact]
        public void SalesViewModel_CancelInvoiceByNumber_ShouldSucceedForAdmin()
        {
            var ctx = new TestContext();
            var sale = ctx.CreateSale(8);
            ctx.Invoices.SaveInvoice(sale, out _).Should().BeTrue();

            var session = new UserSessionService(ctx.Users, ctx.Shifts);
            session.Login(ctx.AdminUser.Username, "Pass@1234", out _).Should().BeTrue();
            session.SetCurrentShift(ctx.Shifts.GetCurrentOpenShift(ctx.AdminUser.Id));

            var vm = new SalesViewModel(
                ctx.Items,
                ctx.Invoices,
                ctx.Customers,
                new SettingsRepository(ctx.Db),
                sessionService: session,
                shiftRepository: ctx.Shifts,
                ownerAuditLogRepository: ctx.OwnerAudit,
                staffAuditLogRepository: ctx.StaffAudit);

            vm.CanCancelInvoice.Should().BeTrue();
            vm.ConfirmCancellation = _ => true; // تأكيد تلقائي بدون MessageBox

            bool result = vm.CancelInvoiceByNumber(sale.InvoiceNumber, out string err);
            result.Should().BeTrue(err);

            ctx.Items.GetById(ctx.ItemId)!.TotalStockSmallestUnits.Should().Be(50);
            ctx.Invoices.GetInvoiceById(sale.Id)!.Status.Should().Be(InvoiceStatus.Cancelled);
            vm.StatusMessage.Should().Contain("تم إلغاء الفاتورة");
        }
    }
}
