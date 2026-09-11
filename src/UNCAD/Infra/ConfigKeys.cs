namespace UNCAD.Infra
{
    /// <summary>全部配置键的唯一来源（与旧 LISP 插件键名兼容）。</summary>
    public static class ConfigKeys
    {
        // 线段绘制（U1L；注册表键名保持不变以兼容已有设置）
        public const string UnlText = "UNL_TEXT";
        public const string UnlHeight = "UNL_HEIGHT";
        public const string UnlPos = "UNL_POS";
        public const string UnlOffset = "UNL_OFFSET";

        // 桥架标注（U1Q1/U1Q2/U1Q4；注册表键名保持不变）
        public const string UnqText = "UNQ_TEXT";
        public const string UnqHeight = "UNQ_HEIGHT";
        public const string UnqLineOff = "UNQ_LINE_OFF";
        public const string UnqTextOff = "UNQ_TEXT_OFF";
        public const string UnqSide = "UNQ_SIDE"; // "1"=上方(默认) "0"=下方

        // 软管绘制（U1C）：管径独立，几何标注参数与桥架共用。
        public const string ConduitDiameter = "UNC_CONDUIT_DIAMETER";
        public const string ConduitHeight = UnqHeight;
        public const string ConduitLineOff = UnqLineOff;
        public const string ConduitTextOff = UnqTextOff;
        public const string ConduitSide = UnqSide;

        // 拱桥开洞（U1R；注册表键名保持不变）
        public const string UnrDiameter = "UNR_DIAMETER";

        // 统计汇总（传统 UNADD；注册表键名保持不变）
        public const string UnaddHeight = "UNADD_HEIGHT";
        public const string UnaddMmPerGrid = "UNADD_MM_PER_GRID";
        public const string UnaddTextEnabled = "UNADD_TEXT_ENABLED";
        public const string UnaddMTextEnabled = "UNADD_MTEXT_ENABLED";
        public const string UnaddDimensionEnabled = "UNADD_DIMENSION_ENABLED";
        public const string UnaddCableEnabled = "UNADD_CABLE_ENABLED";
        public const string UnaddBridgeEnabled = "UNADD_BRIDGE_ENABLED";
        public const string UnaddConduitEnabled = "UNADD_CONDUIT_ENABLED";

        // 文字样式（所有生成文字共用）
        public const string StyleName = "UNC_STYLE_NAME";
        public const string StyleFont = "UNC_STYLE_FONT";        // TTF 文件名，如 msyh.ttf（微软雅黑）
        public const string StyleBigFont = "UNC_STYLE_BIGFONT";  // SHX 大字体，空=纯 TTF
        public const string StyleWidth = "UNC_STYLE_WIDTH";      // 宽高比

        // 版本更新提示（Bootstrap 已展示过的版本号；空=全新安装）
        public const string UpdateNotesSeenVersion = "UNC_UPDATE_NOTES_SEEN_VERSION";
        // 启动全屏品牌动画（"1"=开启,默认）
        public const string StartupSplashEnabled = "UNC_STARTUP_SPLASH_ENABLED";

        // Excel 生成与更新（U1F / U1U；注册表键名保持不变）
        public const string FillExcelPath = "UNC_FILL_EXCEL";    // 机台数据 Excel（用户唯一需要提供的文件）
        public const string FillTableRow = "UNC_FILL_TABLE_ROW"; // Table 起始行（1 基）
        public const string FillClearRows = "UNC_FILL_CLEAR_ROWS"; // 每次先清空的模板数据行数，默认 11
        public const string FillTextHeight = "UNC_FILL_TEXT_HEIGHT"; // 写入单元格文字高度，默认 500
        public const string FillBridge = "UNC_FILL_BRIDGE";      // 图框块 BRIDGE_FRAME_INFO 内容（空=不写）
        public const string FillFlexibleConduitMeters =
            "UNC_FILL_FLEXIBLE_CONDUIT_METERS"; // 软管默认长度，单位米
        public const string FillDeviceColorIndex = "UNC_FILL_DEVICE_COLOR_INDEX";
        public const string FillUpstreamColorIndex = "UNC_FILL_UPSTREAM_COLOR_INDEX";
        public const string FillIncludeUnmatchedConduits =
            "UNC_FILL_INCLUDE_UNMATCHED_CONDUITS"; // 仅兼容旧配置；运行时固定忽略
        public const string FillRecentCatalogItems =
            "UNC_FILL_RECENT_CATALOG_ITEMS"; // 清单选择对话框“最近使用”记忆（仅UI，逗号分隔编码）

        // BOQ 自动填充类别开关（U1F/U1U 规划清单行时跳过关闭的类别）
        public const string FillAutofillCable = "UNC_FILL_AUTOFILL_CABLE";       // 电缆
        public const string FillAutofillBreaker = "UNC_FILL_AUTOFILL_BREAKER";   // 电盘（断路器）
        public const string FillAutofillFlexibleConduit =
            "UNC_FILL_AUTOFILL_FLEXIBLE_CONDUIT";                                 // 软管
        public const string FillAutofillRigidConduit =
            "UNC_FILL_AUTOFILL_RIGID_CONDUIT";                                    // 线管
        public const string FillAutofillBridge = "UNC_FILL_AUTOFILL_BRIDGE";     // 桥架
        public const string FillAutofillOutletPanel =
            "UNC_FILL_AUTOFILL_OUTLET_PANEL";                                     // 插座盘
        public const string FillAutofillBusPlugBox =
            "UNC_FILL_AUTOFILL_BUS_PLUG_BOX";                                     // 插接箱


        // BOQ 与 DWG 自动输出目录；沿用旧键名以迁移已有用户设置。
        public const string SubmitFolder = "UNC_SUBMIT_FOLDER";  // 自动输出文件夹
        // 试用授权防回拨:系统时间水位线(max-seen UTC,yyyyMMddHHmmss)。
        public const string ClockWatermarkUtc = "UNC_CLOCK_WATERMARK";
        // 专业版在线授权码：由用户首次使用时输入，升级后继续沿用。
        public const string OnlineAuthorizationCode = "UNC_ONLINE_AUTHORIZATION_CODE";
        // 上次在线授权验证成功的 UTC 时间戳（宽限期判定，防回拨由 TrustedClock 背书）。
        public const string LicenseLastValidatedUtc = "UNC_LICENSE_LAST_OK";
    }
}
