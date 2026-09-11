using System.Diagnostics;
using UNCAD.Infra;
using Xunit;

namespace UNCAD.Tests
{
    public class ConfigKeysTests
    {
        /// <summary>更新日期跟随程序集构建时间戳,与 AboutInfo 同源。</summary>
        private static string AboutInfoTestBuildDate()
        {
            string productVersion = FileVersionInfo.GetVersionInfo(
                typeof(AboutInfo).Assembly.Location).ProductVersion ?? "";
            const string marker = "+build.";
            int index = productVersion.IndexOf(marker, System.StringComparison.Ordinal);
            if (index >= 0
                && System.DateTime.TryParseExact(
                    productVersion.Substring(index + marker.Length), "yyyyMMddHHmmss",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeUniversal
                    | System.Globalization.DateTimeStyles.AdjustToUniversal,
                    out System.DateTime utc))
                return utc.AddHours(8).ToString("yyyy-MM-dd");
            return ProductMetadata.ReleaseDateUtc;
        }

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
            Assert.Equal("2.4.7", info.Version);
            Assert.NotEqual("未知", info.BuildTime);
            Assert.Equal(AboutInfoTestBuildDate(), info.UpdatedOn);
            Assert.Equal("UNCAD Pro", info.ProductName);
            Assert.Equal("—", info.CustomerCode);
            Assert.Contains("在线", info.Authorization);
            Assert.Equal("UNSIAO.Ltd", Branding.Developer);
        }

        [Fact]
        public void AboutInfo_DisplaysBuildTimestampInUtcPlusEight()
        {
            const string version = "2.4.7+build.20260910180000";

            Assert.Equal("2026-09-11 02:00:00 UTC+8",
                AboutInfo.ParseBuildTime(version));
            Assert.Equal("2026-09-11", AboutInfo.BuildDateOrFallback(version));
        }

        [Fact]
        public void SubmitFolder_HasIndependentPersistentKey()
        {
            Assert.Equal("UNC_SUBMIT_FOLDER", ConfigKeys.SubmitFolder);
            Assert.NotEqual(ConfigKeys.FillExcelPath, ConfigKeys.SubmitFolder);
        }

        [Fact]
        public void OnlineAuthorizationCode_HasPersistentRuntimeKey()
            => Assert.Equal("UNC_ONLINE_AUTHORIZATION_CODE",
                ConfigKeys.OnlineAuthorizationCode);

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
            Assert.Equal("UNC_FILL_AUTOFILL_CABLE", ConfigKeys.FillAutofillCable);
            Assert.Equal("UNC_FILL_AUTOFILL_BREAKER", ConfigKeys.FillAutofillBreaker);
            Assert.Equal("UNC_FILL_AUTOFILL_PANEL", ConfigKeys.FillAutofillPanel);
            Assert.Equal("UNC_FILL_AUTOFILL_FLEXIBLE_CONDUIT",
                ConfigKeys.FillAutofillFlexibleConduit);
            Assert.Equal("UNC_FILL_AUTOFILL_RIGID_CONDUIT",
                ConfigKeys.FillAutofillRigidConduit);
            Assert.Equal("UNC_FILL_AUTOFILL_BRIDGE", ConfigKeys.FillAutofillBridge);
            Assert.Equal("UNC_FILL_AUTOFILL_OUTLET_PANEL",
                ConfigKeys.FillAutofillOutletPanel);
            Assert.Equal("UNC_FILL_AUTOFILL_BUS_PLUG_BOX",
                ConfigKeys.FillAutofillBusPlugBox);
        }

        [Fact]
        public void StatisticsToggles_HaveIndependentPersistentKeys()
        {
            Assert.Equal("UNADD_TEXT_ENABLED", ConfigKeys.UnaddTextEnabled);
            Assert.Equal("UNADD_MTEXT_ENABLED", ConfigKeys.UnaddMTextEnabled);
            Assert.Equal("UNADD_DIMENSION_ENABLED", ConfigKeys.UnaddDimensionEnabled);
            Assert.Equal("UNADD_CABLE_ENABLED", ConfigKeys.UnaddCableEnabled);
            Assert.Equal("UNADD_BRIDGE_ENABLED", ConfigKeys.UnaddBridgeEnabled);
            Assert.Equal("UNADD_CONDUIT_ENABLED", ConfigKeys.UnaddConduitEnabled);
            Assert.True(StatisticsSettings.DefaultIncludeMText);
            Assert.True(StatisticsSettings.DefaultIncludeDimension);
        }

        [Fact]
        public void StatisticsTextSource_IncludesDimensionOverridesWithText()
        {
            Assert.Equal("TEXT,DIMENSION", new StatisticsSettingsSnapshot
            {
                IncludeText = true,
                IncludeDimension = true
            }.SelectionFilter);
            Assert.Equal("TEXT,MTEXT,DIMENSION", new StatisticsSettingsSnapshot
            {
                IncludeText = true,
                IncludeMText = true,
                IncludeDimension = true
            }.SelectionFilter);
            Assert.Equal("MTEXT", new StatisticsSettingsSnapshot
            {
                IncludeMText = true
            }.SelectionFilter);
            // 标注识别是独立开关：关闭后过滤器不得再选中 DIMENSION。
            Assert.Equal("TEXT", new StatisticsSettingsSnapshot
            {
                IncludeText = true,
                IncludeDimension = false
            }.SelectionFilter);
            Assert.Equal("DIMENSION", new StatisticsSettingsSnapshot
            {
                IncludeDimension = true
            }.SelectionFilter);
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
