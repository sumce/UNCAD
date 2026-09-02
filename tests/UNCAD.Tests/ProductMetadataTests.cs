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
        public void ConfiguredBuild_HasConsistentLicenseState()
        {
            LicenseSnapshot license = ProductMetadata.CurrentLicense();
            Assert.Equal(ProductMetadata.BuildLicenseMode, license.Mode);
            if (license.Mode == LicenseMode.Perpetual)
            {
                Assert.False(license.IsExpired);
                Assert.Null(license.DaysRemaining);
                Assert.Equal("无期限", license.ExpiryText);
                ProductMetadata.EnsureCommandAllowed("U1F");
            }
            else
            {
                Assert.NotNull(license.ExpiresUtc);
                Assert.Equal(ProductMetadata.BuildLicenseMode == LicenseMode.Project
                    ? "2026-10-01 00:00:00 UTC+8"
                    : "2026-09-03 00:00:00 UTC+8", license.ExpiryText);
            }
        }

        [Fact]
        public void BuildConfiguration_SelectsMatchingLicenseFlavor()
        {
            if (ProductMetadata.BuildConfiguration == "JSWY")
            {
                Assert.Equal(LicenseMode.Project, ProductMetadata.BuildLicenseMode);
                Assert.Equal("2026-10-01T00:00:00+08:00",
                    ProductMetadata.BuildLicenseExpiresUtc);
                Assert.Equal("UNCAD-JSWY", ProductMetadata.BuildCustomerCode);
                Assert.Equal("江苏文炎建设工程有限公司", ProductMetadata.LicenseeCompany);
                Assert.Equal("李小亮", ProductMetadata.LicenseeName);
                Assert.Equal(10, ProductMetadata.ExpectedAuthorizationYears);
            }
            else if (ProductMetadata.BuildConfiguration == "Temporary")
            {
                Assert.Equal(LicenseMode.Trial, ProductMetadata.BuildLicenseMode);
                Assert.Equal("2026-09-03T00:00:00+08:00",
                    ProductMetadata.BuildLicenseExpiresUtc);
            }
            else
            {
                Assert.Equal(LicenseMode.Perpetual, ProductMetadata.BuildLicenseMode);
                Assert.Equal("", ProductMetadata.BuildLicenseExpiresUtc);
            }
        }

        [Fact]
        public void TemporaryLicense_ExpiresAtConfiguredUtcPlus8Deadline()
        {
            const string expiry = "2026-09-03T00:00:00+08:00";
            LicenseSnapshot active = ProductMetadata.EvaluateLicense(LicenseMode.Trial,
                expiry, new DateTime(2026, 9, 2, 15, 59, 59, DateTimeKind.Utc));
            LicenseSnapshot expired = ProductMetadata.EvaluateLicense(LicenseMode.Trial,
                expiry, new DateTime(2026, 9, 2, 16, 0, 0, DateTimeKind.Utc));

            Assert.False(active.IsExpired);
            Assert.Equal(1, active.DaysRemaining);
            Assert.True(expired.IsExpired);
            Assert.Equal("2026-09-03 00:00:00 UTC+8", expired.ExpiryText);
        }

        [Fact]
        public void JswyProjectLicense_UsesOctoberFirstDeadline()
        {
            if (ProductMetadata.BuildConfiguration != "JSWY") return;

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
        public void BuildLicenseValues_AreInternallyConsistent()
        {
            if (ProductMetadata.BuildLicenseMode == LicenseMode.Perpetual)
                Assert.True(string.IsNullOrWhiteSpace(ProductMetadata.BuildLicenseExpiresUtc));
            else
                Assert.False(string.IsNullOrWhiteSpace(ProductMetadata.BuildLicenseExpiresUtc));
        }
    }
}
