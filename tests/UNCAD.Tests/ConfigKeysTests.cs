using UNCAD.Infra;
using Xunit;

namespace UNCAD.Tests
{
    public class ConfigKeysTests
    {
        [Fact]
        public void Nameplate_UsesApprovedBrandText()
        {
            Assert.Equal("UNCAD · AutoCAD Engineering Tools", Branding.Nameplate);
            Assert.Equal("UNSIAO.Ltd", Branding.Developer);
        }

        [Fact]
        public void AboutInfo_ExposesVersionBuildAndOwnershipMetadata()
        {
            AboutInfo info = AboutInfo.Current();
            Assert.Equal("2.2.1", info.Version);
            Assert.NotEqual("未知", info.BuildTime);
            Assert.Equal("2026-09-02", info.UpdatedOn);
            if (ProductMetadata.BuildLicenseMode == LicenseMode.Project)
            {
                Assert.Contains("项目授权版", info.Authorization);
                Assert.Equal("UNCAD-JSWY", info.CustomerCode);
                Assert.Equal("江苏文炎建设工程有限公司", info.LicenseeCompany);
                Assert.Equal("李小亮", info.LicenseeName);
                Assert.Equal(10, info.ExpectedAuthorizationYears);
                return;
            }
            Assert.Contains(ProductMetadata.BuildLicenseMode == LicenseMode.Perpetual
                ? "正式版"
                : ProductMetadata.CurrentLicense().IsExpired ? "已过期" : "试用版", info.Authorization);
            Assert.Equal("UNSIAO.Ltd", Branding.Developer);
            if (ProductMetadata.BuildConfiguration == "JSWY")
            {
                Assert.Equal("UNCAD-JSWY", info.CustomerCode);
                Assert.Equal("江苏文炎建设工程有限公司", info.LicenseeCompany);
                Assert.Equal("李小亮", info.LicenseeName);
                Assert.Equal(10, info.ExpectedAuthorizationYears);
            }
        }

        [Fact]
        public void SubmitFolder_HasIndependentPersistentKey()
        {
            Assert.Equal("UNC_SUBMIT_FOLDER", ConfigKeys.SubmitFolder);
            Assert.NotEqual(ConfigKeys.FillExcelPath, ConfigKeys.SubmitFolder);
        }

        [Fact]
        public void EmbeddedCatalog_LeavesOnlyTheMachineWorkbookAsUserInput()
        {
            // 固定清单已内嵌进插件；外部清单配置键必须保持不存在，防止再次暴露。
            Assert.Equal("UNC_FILL_EXCEL", ConfigKeys.FillExcelPath);
            Assert.Null(typeof(ConfigKeys).GetField("FillCatalogPath"));
        }

        [Fact]
        public void FillClearRange_HasIndependentPersistentKey()
        {
            Assert.Equal("UNC_FILL_CLEAR_ROWS", ConfigKeys.FillClearRows);
            Assert.NotEqual(ConfigKeys.FillTableRow, ConfigKeys.FillClearRows);
        }

        [Fact]
        public void FillBehavior_HasIndependentPersistentKeys()
        {
            Assert.Equal("UNC_FILL_FLEXIBLE_CONDUIT_METERS",
                ConfigKeys.FillFlexibleConduitMeters);
            Assert.Equal("UNC_FILL_INCLUDE_UNMATCHED_CONDUITS",
                ConfigKeys.FillIncludeUnmatchedConduits);
            Assert.NotEqual(ConfigKeys.FillFlexibleConduitMeters,
                ConfigKeys.FillIncludeUnmatchedConduits);
            Assert.NotEqual(ConfigKeys.FillClearRows,
                ConfigKeys.FillFlexibleConduitMeters);
            Assert.Equal("UNC_FILL_DEVICE_COLOR_INDEX", ConfigKeys.FillDeviceColorIndex);
            Assert.Equal("UNC_FILL_UPSTREAM_COLOR_INDEX", ConfigKeys.FillUpstreamColorIndex);
            Assert.NotEqual(ConfigKeys.FillDeviceColorIndex,
                ConfigKeys.FillUpstreamColorIndex);
        }

        [Fact]
        public void StatisticsToggles_HaveIndependentPersistentKeys()
        {
            Assert.Equal("UNADD_TEXT_ENABLED", ConfigKeys.UnaddTextEnabled);
            Assert.Equal("UNADD_MTEXT_ENABLED", ConfigKeys.UnaddMTextEnabled);
            Assert.Equal("UNADD_CABLE_ENABLED", ConfigKeys.UnaddCableEnabled);
            Assert.Equal("UNADD_BRIDGE_ENABLED", ConfigKeys.UnaddBridgeEnabled);
            Assert.Equal("UNADD_CONDUIT_ENABLED", ConfigKeys.UnaddConduitEnabled);
        }

        [Fact]
        public void ConduitAnnotationGeometry_UsesBridgeSettingKeys()
        {
            Assert.Equal(ConfigKeys.UnqHeight, ConfigKeys.ConduitHeight);
            Assert.Equal(ConfigKeys.UnqLineOff, ConfigKeys.ConduitLineOff);
            Assert.Equal(ConfigKeys.UnqTextOff, ConfigKeys.ConduitTextOff);
            Assert.Equal(ConfigKeys.UnqSide, ConfigKeys.ConduitSide);
        }
    }
}
