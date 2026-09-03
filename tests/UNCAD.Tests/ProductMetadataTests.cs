using System;
using UNCAD.Infra;
using Xunit;

namespace UNCAD.Tests
{
    public sealed class ProductMetadataTests
    {
        [Fact]
        public void Brand_UsesApprovedCompanyAndWebsite()
        {
            Assert.Equal("UNCAD", ProductMetadata.ProductName);
            Assert.Equal("UNSIAO.Ltd", ProductMetadata.CompanyName);
            Assert.Equal("www.unsiao.com", ProductMetadata.Website);
            Assert.Equal("https://www.unsiao.com", ProductMetadata.WebsiteUrl);
            Assert.Contains("UNSIAO.Ltd", LegalTerms.FullText);
            Assert.Contains("www.unsiao.com", LegalTerms.FullText);
        }

        [Fact]
        public void UnifiedProBuild_FailsClosedUntilKeyIsLoaded()
        {
            LicenseSnapshot license = ProductMetadata.CurrentLicense();
            Assert.Equal("UNCAD Pro", ProductMetadata.DisplayName);
            Assert.True(ProductMetadata.RequiresOnlineLicense);
            Assert.True(license.IsExpired);
            Assert.Contains("在线", license.StatusText);
            Assert.Equal("—", ProductMetadata.CurrentCustomerCode);
        }

        [Fact]
        public void ProjectLicense_UsesExpiryProvidedByKey()
        {
            const string expiry = "2026-10-01T00:00:00+08:00";
            LicenseSnapshot active = ProductMetadata.EvaluateLicense(LicenseMode.Project,
                expiry, new DateTime(2026, 9, 30, 15, 59, 59, DateTimeKind.Utc));
            LicenseSnapshot expired = ProductMetadata.EvaluateLicense(LicenseMode.Project,
                expiry, new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc));

            Assert.False(active.IsExpired);
            Assert.True(expired.IsExpired);
            Assert.Equal("2026-10-01 00:00:00 UTC+8", expired.ExpiryText);
        }

        [Fact]
        public void PerpetualKey_DoesNotRequireAnExpiry()
        {
            LicenseSnapshot license = ProductMetadata.EvaluateLicense(LicenseMode.Perpetual,
                "", DateTime.UtcNow);

            Assert.False(license.IsExpired);
            Assert.Equal("无期限", license.ExpiryText);
        }
    }
}
