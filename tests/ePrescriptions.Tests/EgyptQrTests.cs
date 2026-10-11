using System;
using ePrescriptions.Core.Helpers;
using FluentAssertions;
using Xunit;

namespace ePrescriptions.Tests
{
    public class EgyptQrTests
    {
        [Fact]
        public void BuildSimpleQrText_ShouldContainInvoiceNumberTotalAndTaxNumber()
        {
            string text = EgyptQrHelper.BuildSimpleQrText("INV-2026-00001", new DateTime(2026, 10, 5, 14, 30, 0), 11550, "123456789", "صيدلية");

            text.Should().Contain("INV-2026-00001");
            text.Should().Contain("115.50");
            text.Should().Contain("123456789");
        }

        [Theory]
        [InlineData("123456789", 9, true)]
        [InlineData("12345678", 9, false)]
        [InlineData("12345678A", 9, false)]
        [InlineData("", 9, false)]
        [InlineData("300987654300003", 15, true)]
        public void ValidateTaxRegistrationNumber_ShouldMatchDigitsAndLength(string number, int length, bool expected)
        {
            EgyptQrHelper.ValidateTaxRegistrationNumber(number, length).Should().Be(expected);
        }
    }
}
