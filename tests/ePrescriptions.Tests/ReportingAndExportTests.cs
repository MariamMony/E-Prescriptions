using System;
using System.Collections.Generic;
using System.IO;
using ePrescriptions.App.Services;
using ePrescriptions.Data;
using ePrescriptions.Data.Repositories;
using ePrescriptions.Domain.Entities;
using ePrescriptions.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace ePrescriptions.Tests
{
    public class ReportingAndExportTests
    {
        [Fact]
        public void SalesPeriodSummary_Calculations_ShouldBeAccurateInEgyptianPounds()
        {
            // Arrange
            var summary = new SalesPeriodSummary
            {
                TotalInvoicesCount = 10,
                TotalSalesPiastres = 150000, // 1500.00 EGP
                TotalCostPiastres = 90000,   // 900.00 EGP
                TotalVatPiastres = 21000,    // 210.00 EGP
                CashSalesPiastres = 100000,  // 1000.00 EGP
                NetworkSalesPiastres = 50000 // 500.00 EGP
            };

            // Assert
            summary.TotalSalesPounds.Should().Be(1500.00m);
            summary.TotalCostPounds.Should().Be(900.00m);
            summary.NetProfitPiastres.Should().Be(60000);
            summary.NetProfitPounds.Should().Be(600.00m);
            summary.ProfitMarginPercent.Should().BeApproximately(40.0, 0.01);
            summary.CashSalesPounds.Should().Be(1000.00m);
            summary.NetworkSalesPounds.Should().Be(500.00m);
        }

        [Fact]
        public void LowStockReportRow_DeficitAndReorder_ShouldBeComputedCorrectly()
        {
            // Arrange
            var lowItem = new LowStockReportRow
            {
                ItemCode = "MED-001",
                NameAr = "بنادول إكسترا",
                CurrentStockSmallestUnits = 10,
                MinStockAlertSmallestUnits = 40,
                RetailPriceBoxPiastres = 1200
            };

            // Assert
            lowItem.DeficitUnits.Should().Be(30);
            lowItem.SuggestedOrderUnits.Should().Be(110); // 30 + 80
            lowItem.RetailPriceBoxPounds.Should().Be(12.00m);
        }

        [Fact]
        public void ExpiryReportRow_StatusAndDays_ShouldIdentifyExpiredBatches()
        {
            // Arrange
            var expiredBatch = new ExpiryReportRow
            {
                ItemCode = "MED-002",
                NameAr = "أوجمنتين",
                BatchNumber = "BATCH-EXP",
                ExpiryDate = DateTime.Today.AddDays(-5),
                RemainingSmallestUnits = 20,
                CostPricePerSmallestUnitPiastres = 50
            };

            var validBatch = new ExpiryReportRow
            {
                ItemCode = "MED-003",
                NameAr = "كونكور",
                BatchNumber = "BATCH-VAL",
                ExpiryDate = DateTime.Today.AddDays(45),
                RemainingSmallestUnits = 10,
                CostPricePerSmallestUnitPiastres = 100
            };

            // Assert
            expiredBatch.IsExpired.Should().BeTrue();
            expiredBatch.StatusDisplay.Should().Be("منتهي الصلاحية");
            expiredBatch.TotalCostPiastres.Should().Be(1000);
            expiredBatch.TotalCostPounds.Should().Be(10.00m);

            validBatch.IsExpired.Should().BeFalse();
            validBatch.DaysRemaining.Should().Be(45);
            validBatch.StatusDisplay.Should().Be("متبقي 45 يوم");
        }

        [Fact]
        public void ExcelExportService_ExportSalesReport_ShouldCreateValidExcelFile()
        {
            // Arrange
            var service = new ExcelExportService();
            var sales = new List<SalesReportRow>
            {
                new SalesReportRow
                {
                    InvoiceNumber = "INV-2026-00001",
                    Date = DateTime.Now,
                    CustomerName = "عميل نقدي",
                    PaymentMethod = PaymentMethod.Cash,
                    SubtotalPiastres = 10000,
                    VatPiastres = 1400,
                    TotalPiastres = 11400,
                    EstimatedCostPiastres = 7000
                }
            };

            var summary = new SalesPeriodSummary
            {
                TotalInvoicesCount = 1,
                TotalSalesPiastres = 11400,
                CashSalesPiastres = 11400,
                TotalCostPiastres = 7000
            };

            // Act
            string filePath = service.ExportSalesReport(sales, summary, DateTime.Today, DateTime.Today, "صيدلية ماجد القبانى");

            // Assert
            File.Exists(filePath).Should().BeTrue();
            new FileInfo(filePath).Length.Should().BeGreaterThan(1000);

            // Clean up
            try { File.Delete(filePath); } catch { }
        }

        [Fact]
        public void BackupService_CreateAndValidateBackup_ShouldSucceedWithIntegrityCheck()
        {
            // Arrange
            string tempDbPath = Path.Combine(Path.GetTempPath(), $"test_db_{Guid.NewGuid():N}.db");
            var dbContext = new DatabaseContext(tempDbPath);
            dbContext.InitializeAndMigrate(seedData: true);

            var backupService = new BackupService(dbContext);
            string tempBackupFolder = Path.Combine(Path.GetTempPath(), $"backups_{Guid.NewGuid():N}");

            try
            {
                // Act: إنشاء نسخة احتياطية
                bool created = backupService.CreateBackup(tempBackupFolder, out string backupPath, out string error);

                // Assert
                created.Should().BeTrue();
                error.Should().BeEmpty();
                File.Exists(backupPath).Should().BeTrue();

                // Act: فحص السلامة
                bool valid = backupService.ValidateBackupFile(backupPath, out string valError);
                valid.Should().BeTrue();
                valError.Should().BeEmpty();
            }
            finally
            {
                // Clean up
                try { if (Directory.Exists(tempBackupFolder)) Directory.Delete(tempBackupFolder, true); } catch { }
                try { if (File.Exists(tempDbPath)) File.Delete(tempDbPath); } catch { }
            }
        }
    }
}
