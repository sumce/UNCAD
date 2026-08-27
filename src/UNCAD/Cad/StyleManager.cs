using System;
using System.IO;
using Autodesk.AutoCAD.DatabaseServices;
using UNCAD.Infra;

namespace UNCAD.Cad
{
    /// <summary>
    /// 文字样式管理：所有生成文字使用统一的 UNC 专属文字样式（字体/宽高比可配置）。
    /// 样式不存在则自动创建；配置变更时自动同步字体（UNC 样式归插件管理）。
    /// 注意：字体必须用本机真实存在的 TTF/TTC 文件名；.ttc 会被 AutoCAD 保存成 .ttf，
    /// 若对应 .ttf 不存在会回退 simplex 导致中文变 "?"，故配置的字体找不到时自动回退 simhei.ttf。
    /// </summary>
    public static class StyleManager
    {
        /// <summary>
        /// 获取图纸的 Standard 文字样式；极少数没有 Standard 的图纸回退到当前文字样式。
        /// UNC_LINE / UNC_TRAY 用它保持与图纸默认文字外观一致。
        /// </summary>
        public static ObjectId GetDrawingStandardStyle(CadContext ctx, Transaction tr)
        {
            var styles = (TextStyleTable)tr.GetObject(ctx.Db.TextStyleTableId, OpenMode.ForRead);
            return styles.Has("Standard") ? styles["Standard"] : ctx.Db.Textstyle;
        }

        /// <summary>按配置（UNC_STYLE_*）获取/创建文字样式。</summary>
        public static ObjectId EnsureConfiguredStyle(CadContext ctx, Transaction tr)
        {
            return EnsureTextStyle(ctx, tr,
                Settings.Get(ConfigKeys.StyleName, "UNC-标注"),
                ResolveFont(Settings.Get(ConfigKeys.StyleFont, "msyh.ttf")),
                Settings.Get(ConfigKeys.StyleBigFont, ""),
                Settings.GetDouble(ConfigKeys.StyleWidth, 0.8));
        }

        /// <summary>查找或创建文字样式；返回样式 ObjectId。</summary>
        public static ObjectId EnsureTextStyle(CadContext ctx, Transaction tr,
            string styleName, string fontFile, string bigFontFile, double widthFactor)
        {
            fontFile = ResolveFont(fontFile);

            var tsTable = (TextStyleTable)tr.GetObject(ctx.Db.TextStyleTableId, OpenMode.ForRead);
            if (tsTable.Has(styleName))
            {
                var id = tsTable[styleName];
                var rec = (TextStyleTableRecord)tr.GetObject(id, OpenMode.ForRead);
                // 配置变更时同步更新（UNC 样式归插件管理）
                bool changed = rec.FileName != fontFile
                    || rec.BigFontFileName != (bigFontFile ?? "")
                    || Math.Abs(rec.XScale - widthFactor) > 1e-9;
                if (changed)
                {
                    rec.UpgradeOpen();
                    rec.FileName = fontFile;
                    rec.BigFontFileName = bigFontFile ?? "";
                    rec.XScale = widthFactor;
                }
                return id;
            }

            tsTable.UpgradeOpen();
            var rec2 = new TextStyleTableRecord
            {
                Name = styleName,
                FileName = fontFile,
                BigFontFileName = bigFontFile ?? "",
                XScale = widthFactor,
                TextSize = 0 // 高度不固定，由各命令的高度配置控制
            };
            tsTable.Add(rec2);
            tr.AddNewlyCreatedDBObject(rec2, true);
            return rec2.Id;
        }

        /// <summary>
        /// 字体可用性保护：配置的字体文件在本机不存在时，回退到确定可用的中文字体
        /// （simhei.ttf 黑体，其次 msyh.ttf 微软雅黑），避免 AutoCAD 找不到字体
        /// 回退 simplex 导致中文显示 "?"。
        /// </summary>
        public static string ResolveFont(string fontFile)
        {
            if (string.IsNullOrWhiteSpace(fontFile)) return fontFile;
            string f = fontFile.Trim();
            if (FontFileExists(f)) return f;

            foreach (var candidate in new[] { "simhei.ttf", "msyh.ttf", "simsun.ttf", "arial.ttf" })
            {
                if (FontFileExists(candidate)) return candidate;
            }
            return f; // 都找不到就保持原样（让 AutoCAD 自行处理）
        }

        private static bool FontFileExists(string file)
        {
            try
            {
                string win = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
                if (File.Exists(Path.Combine(win, file))) return true;
                string user = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Microsoft", "Windows", "Fonts");
                if (File.Exists(Path.Combine(user, file))) return true;
                return false;
            }
            catch
            {
                return false;
            }
        }
    }
}
