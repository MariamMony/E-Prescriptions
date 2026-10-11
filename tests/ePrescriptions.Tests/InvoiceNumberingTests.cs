using System;
using ePrescriptions.Data;
using ePrescriptions.Data.Repositories;
using ePrescriptions.Domain.Entities;
using ePrescriptions.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace ePrescriptions.Tests
{
    public class InvoiceNumberingTests
    {
        private class TestContext
        {
            public DatabaseContext Db { get; }
            public ItemRepository Items { get; }
            public InvoiceRepository Invoices { get; }
            public PurchaseRepository Purchases { get; }
            public CustomerRepository Customers { get; }
            public FinanceRepository Finance { get; }
            public ShiftRepository Shifts { get; }
            public UserRepository Users { get; }
            public int ShiftId { get; }
            public Item TestItem { get; }

            public TestContext()
            {
                Db = new DatabaseContext(":memory:");
                Db.InitializeAndMigrate(seedData: true);

                Items = new ItemRepository(Db);
                Invoices = new InvoiceRepository(Db, Items);
                Purchases = new PurchaseRepository(Db);
                Customers = new CustomerRepository(Db);
                Finance = new FinanceRepository(Db);
                Shifts = new ShiftRepository(Db);
                Users = new UserRepository(Db);

                ShiftId = Shifts.OpenShift(1, "ماجد القبانى", 5000);

                var item = Items.GetByBarcodeOrCode("6281001000101");
                if (item == null)
                {
                    item = new Item
                    {
                        Code = "TEST-NUM",
                        NameAr = "صنف ترقيم",
                        TotalStockSmallestUnits = 100
                    };
                    Items.SaveItem(item);
                }
                TestItem = item;
            }

            public Invoice CreateSale(string invoiceNumber = "")
            {
                return new Invoice
                {
                    InvoiceNumber = invoiceNumber,
                    Type = InvoiceType.Sales,
                    ShiftId = ShiftId,
                    UserId = 1,
                    UserName = "ماجد القبانى",
                    CustomerId = 1,
                    CustomerName = "عميل نقدي",
                    PaymentMethod = PaymentMethod.Cash,
                    SubtotalPiastres = 1000,
                    TotalPiastres = 1000,
                    PaidPiastres = 1000,
                    RemainingPiastres = 0,
                    Items =
                    {
                        new InvoiceItem
                        {
                            ItemId = TestItem.Id,
                            ItemCode = TestItem.Code,
                            ItemNameAr = TestItem.NameAr,
                            UnitLevel = UnitLevel.Piece,
                            QuantitySold = 1,
                            SmallestUnitsCount = 1,
                            UnitPricePiastres = 1000,
                            TotalPiastres = 1000
                        }
                    }
                };
            }
        }

        [Fact]
        public void GenerateNextInvoiceNumber_ShouldStartAtOne_ForNewYear()
        {
            var ctx = new TestContext();
            int testYear = 2030;

            string num1 = ctx.Invoices.GenerateNextInvoiceNumber(InvoiceType.Sales, testYear);
            num1.Should().Be("INV-2030-00001");

            string num2 = ctx.Invoices.GenerateNextInvoiceNumber(InvoiceType.Sales, testYear);
            num2.Should().Be("INV-2030-00002");
        }

        [Fact]
        public void GenerateNextInvoiceNumber_ShouldSeparateSalesAndReturns()
        {
            var ctx = new TestContext();
            int testYear = 2031;

            string sale1 = ctx.Invoices.GenerateNextInvoiceNumber(InvoiceType.Sales, testYear);
            string ret1 = ctx.Invoices.GenerateNextInvoiceNumber(InvoiceType.Return, testYear);

            sale1.Should().Be("INV-2031-00001");
            ret1.Should().Be("RET-2031-00001");
        }

        [Fact]
        public void GenerateNextInvoiceNumber_ShouldResetPerYear_AndNotCountPreviousYears()
        {
            var ctx = new TestContext();

            // إدراج فواتير لسنة 2024
            string y2024_1 = ctx.Invoices.GenerateNextInvoiceNumber(InvoiceType.Sales, 2024);
            string y2024_2 = ctx.Invoices.GenerateNextInvoiceNumber(InvoiceType.Sales, 2024);
            y2024_1.Should().Be("INV-2024-00001");
            y2024_2.Should().Be("INV-2024-00002");

            // سنة 2025 يجب أن تبدأ من 1 ولا تتأثر بفواتير 2024
            string y2025_1 = ctx.Invoices.GenerateNextInvoiceNumber(InvoiceType.Sales, 2025);
            y2025_1.Should().Be("INV-2025-00001");
        }

        [Fact]
        public void GenerateNextInvoiceNumber_WithExistingInvoicesPriorToCounter_ShouldPickUpFromMax()
        {
            var ctx = new TestContext();
            int testYear = 2028;

            // محاكاة وجود فواتير سابقة في جدول Invoices قبل تهيئة العداد
            using (var conn = ctx.Db.CreateConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    INSERT INTO Invoices (InvoiceNumber, Type, Date, CustomerId, CustomerName, CustomerPhone, PriceCategory, PaymentMethod, UserId, UserName, ShiftId, SubtotalPiastres, DiscountPiastres, VatPiastres, TotalPiastres, PaidPiastres, RemainingPiastres, Notes, ZatcaQrBase64)
                    VALUES ('INV-2028-00007', 1, '2028-05-01 10:00:00', 1, 'عميل', '', 1, 1, 1, 'ماجد', @sId, 1000, 0, 0, 1000, 1000, 0, '', '');";
                cmd.Parameters.AddWithValue("@sId", ctx.ShiftId);
                cmd.ExecuteNonQuery();
            }

            // التوليد التالي يجب أن يأخذ في الاعتبار أعلى رقم موجود
            string next = ctx.Invoices.GenerateNextInvoiceNumber(InvoiceType.Sales, testYear);
            next.Should().Be("INV-2028-00008");
        }

        [Fact]
        public void SaveInvoice_WhenInvoiceNumberIsEmpty_ShouldAutoGenerateUniqueNumber()
        {
            var ctx = new TestContext();
            var sale = ctx.CreateSale(string.Empty);

            bool saved = ctx.Invoices.SaveInvoice(sale, out string err);
            saved.Should().BeTrue(err);

            sale.InvoiceNumber.Should().NotBeNullOrEmpty();
            sale.InvoiceNumber.StartsWith($"INV-{DateTime.Now.Year}-").Should().BeTrue();
        }

        [Fact]
        public void SaveInvoice_WhenInvoiceNumberAlreadyTaken_ShouldReallocateNextSequentialNumber()
        {
            var ctx = new TestContext();

            // الفاتورة الأولى بالرقم المتوقع
            string expectedFirst = ctx.Invoices.GenerateNextInvoiceNumber(InvoiceType.Sales);
            var sale1 = ctx.CreateSale(expectedFirst);
            ctx.Invoices.SaveInvoice(sale1, out _).Should().BeTrue();
            sale1.InvoiceNumber.Should().Be(expectedFirst);

            // الفاتورة الثانية أُعدت بنفس الرقم الأول (محاكاة تزامن كاشيرين أو تأخر الحفظ)
            var sale2 = ctx.CreateSale(expectedFirst);
            bool saved2 = ctx.Invoices.SaveInvoice(sale2, out string err2);
            saved2.Should().BeTrue(err2);

            // يجب أن يتم إعادة تخصيص رقم جديد فريد متسلسل تلقائياً دون خطأ
            sale2.InvoiceNumber.Should().NotBe(expectedFirst);
            sale2.InvoiceNumber.StartsWith($"INV-{DateTime.Now.Year}-").Should().BeTrue();
        }

        [Fact]
        public void PurchasesAndVouchers_ShouldNumberPerYearSequentially()
        {
            var ctx = new TestContext();
            int testYear = 2029;

            string pur1 = ctx.Purchases.GenerateNextPurchaseInvoiceNumber(testYear);
            pur1.Should().Be("PUR-2029-00001");

            string sv1 = ctx.Purchases.GenerateNextSupplierVoucherNumber(testYear);
            sv1.Should().Be("SV-2029-00001");

            string cv1 = ctx.Customers.GenerateNextCustomerVoucherNumber(testYear);
            cv1.Should().Be("CV-2029-00001");

            string exp1 = ctx.Finance.GenerateNextExpenseNumber(testYear);
            exp1.Should().Be("EXP-2029-00001");

            string jv1 = ctx.Finance.GenerateNextJournalEntryNumber(testYear);
            jv1.Should().Be("JV-2029-00001");
        }
    }
}
