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
