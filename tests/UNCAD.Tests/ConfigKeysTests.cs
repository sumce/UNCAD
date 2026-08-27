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
            Assert.Equal("1.2.2.0", info.Version);
            Assert.NotEqual("未知", info.BuildTime);
            Assert.Equal("2026-08-27", info.UpdatedOn);
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
        public void FillCatalogPath_IsIndependentFromMutableMachineSource()
        {
            Assert.NotEqual(ConfigKeys.FillExcelPath, ConfigKeys.FillCatalogPath);
            Assert.Equal("UNC_FILL_CATALOG_EXCEL", ConfigKeys.FillCatalogPath);
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
