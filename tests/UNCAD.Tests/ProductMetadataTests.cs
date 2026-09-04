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

        [Fact]
        public void OnlineLicense_AllowsCommandsDuringOfflineGraceWindow()
        {
            var online = new OnlineLicenseState
            {
                HasResponse = false,
                IsActive = false,
                Status = "unavailable",
                FailureReason = "暂时无法连接授权服务器。"
            };

            LicenseSnapshot license = ProductMetadata.EvaluateOnlineLicense(online,
                DateTime.UtcNow, false, true);

            Assert.False(license.IsExpired);
            Assert.Contains("离线宽限中", license.StatusText);
        }

        [Fact]
        public void OnlineLicense_UsesActiveServerVerdictInsteadOfFutureLocalClock()
        {
            var online = new OnlineLicenseState
            {
                HasResponse = true,
                IsActive = true,
                Status = "active",
                LicenseMode = LicenseMode.Project,
                ExpiresAt = new DateTimeOffset(2026, 10, 1, 0, 0, 0,
                    TimeSpan.FromHours(8)),
                ServerTime = new DateTimeOffset(2026, 9, 5, 0, 0, 0,
                    TimeSpan.FromHours(8))
            };

            LicenseSnapshot license = ProductMetadata.EvaluateOnlineLicense(online,
                new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc), true, false);

            Assert.False(license.IsExpired);
            Assert.Contains("在线有效", license.StatusText);
        }
    }
}
