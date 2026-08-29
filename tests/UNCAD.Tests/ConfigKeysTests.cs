using UNCAD.Infra;
using Xunit;

namespace UNCAD.Tests
{
    public class ConfigKeysTests
    {
        [Fact]
        public void Nameplate_UsesApprovedBrandText()
        {
            Assert.Equal("UNSIAO Work™ | 云邵出品", Branding.Nameplate);
        }

        [Fact]
        public void AboutInfo_ExposesVersionBuildAndOwnershipMetadata()
        {
            AboutInfo info = AboutInfo.Current();
            Assert.Equal("1.9.7.5", info.Version);
            Assert.NotEqual("未知", info.BuildTime);
            Assert.Equal("2026-08-29", info.UpdatedOn);
            Assert.Contains("正式版", info.Authorization);
            Assert.Equal("UNSIAO.Ltd", Branding.Developer);
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
