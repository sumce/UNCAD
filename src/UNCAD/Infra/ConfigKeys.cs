namespace UNCAD.Infra
{
    /// <summary>全部配置键的唯一来源（与旧 LISP 插件键名兼容）。</summary>
    public static class ConfigKeys
    {
        // 线段绘制（UNC_LINE）
        public const string UnlText = "UNL_TEXT";
        public const string UnlHeight = "UNL_HEIGHT";
        public const string UnlPos = "UNL_POS";
        public const string UnlOffset = "UNL_OFFSET";

        // 桥架标注（UNC_TRAY）
        public const string UnqText = "UNQ_TEXT";
        public const string UnqHeight = "UNQ_HEIGHT";
        public const string UnqLineOff = "UNQ_LINE_OFF";
        public const string UnqTextOff = "UNQ_TEXT_OFF";
        public const string UnqSide = "UNQ_SIDE"; // "1"=上方(默认) "0"=下方

        // 线管标注（UNC_CONDUIT）：管径独立，几何标注参数与桥架共用。
        public const string ConduitDiameter = "UNC_CONDUIT_DIAMETER";
        public const string ConduitHeight = UnqHeight;
        public const string ConduitLineOff = UnqLineOff;
        public const string ConduitTextOff = UnqTextOff;
        public const string ConduitSide = UnqSide;

        // 拱桥开洞（UNC_ARCH）
        public const string UnrDiameter = "UNR_DIAMETER";

        // 统计汇总（UNC_STAT）
        public const string UnaddHeight = "UNADD_HEIGHT";
        public const string UnaddMmPerGrid = "UNADD_MM_PER_GRID";
        public const string UnaddTextEnabled = "UNADD_TEXT_ENABLED";
        public const string UnaddMTextEnabled = "UNADD_MTEXT_ENABLED";
        public const string UnaddCableEnabled = "UNADD_CABLE_ENABLED";
        public const string UnaddBridgeEnabled = "UNADD_BRIDGE_ENABLED";
        public const string UnaddConduitEnabled = "UNADD_CONDUIT_ENABLED";

        // 文字样式（所有生成文字共用）
        public const string StyleName = "UNC_STYLE_NAME";
        public const string StyleFont = "UNC_STYLE_FONT";        // TTF 文件名，如 msyh.ttf（微软雅黑）
        public const string StyleBigFont = "UNC_STYLE_BIGFONT";  // SHX 大字体，空=纯 TTF
        public const string StyleWidth = "UNC_STYLE_WIDTH";      // 宽高比

        // Excel 填充（UNC_FILL）
        public const string FillExcelPath = "UNC_FILL_EXCEL";    // 机台数据 Excel（每次重读 Sheet1）
        public const string FillCatalogPath = "UNC_FILL_CATALOG_EXCEL"; // 固定 BOQ Excel（空=同文件 Sheet2）
        public const string FillTableRow = "UNC_FILL_TABLE_ROW"; // Table 起始行（1 基）
        public const string FillTextHeight = "UNC_FILL_TEXT_HEIGHT"; // 写入单元格文字高度，默认 500
        public const string FillBridge = "UNC_FILL_BRIDGE";      // 图框块 BRIDGE_FRAME_INFO 内容（空=不写）

        // 设备信息提交（UNC_SUBMIT）
        public const string SubmitFolder = "UNC_SUBMIT_FOLDER";  // 首次选择后记住输出文件夹
        // （旧单列模式遗留键，暂保留兼容）
        public const string FillCol = "UNC_FILL_COL";
        public const string FillStartRow = "UNC_FILL_START_ROW";
        public const string FillTableCol = "UNC_FILL_TABLE_COL";
    }
}
