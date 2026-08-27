using System;
using System.Collections.Generic;
using System.Linq;
using UNCAD.Core.Contracts;

namespace UNCAD.Infra
{
    /// <summary>功能描述信息。</summary>
    public sealed class FeatureInfo
    {
        public FeatureInfo(FeatureAttribute attr)
        {
            Attr = attr;
        }

        public FeatureAttribute Attr { get; }
        public string Id => Attr.Id;
        public string DisplayName => Attr.DisplayName;
        public string Description => Attr.Description;
        public string[] Commands => (Attr.Commands ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>
    /// 功能目录：启动时扫描程序集中带 [Feature] 特性的类。
    /// Bootstrap 使用它输出诊断命令清单；Ribbon 任务布局由 RibbonCatalog 管理。
    /// </summary>
    public static class FeatureRegistry
    {
        public static IReadOnlyList<FeatureInfo> Features { get; }

        static FeatureRegistry()
        {
            var list = new List<FeatureInfo>();
            foreach (var type in typeof(FeatureRegistry).Assembly.GetTypes())
            {
                var attr = type.GetCustomAttributes(typeof(FeatureAttribute), false)
                    .Cast<FeatureAttribute>().FirstOrDefault();
                if (attr != null)
                    list.Add(new FeatureInfo(attr));
            }
            Features = list.OrderBy(f => f.Id, StringComparer.Ordinal).ToList().AsReadOnly();
        }

        /// <summary>命令汇总，如 "UNADD / UNL / UNQ1 / UNR ..."。</summary>
        public static string Summary => string.Join(" / ", Features.SelectMany(f => f.Commands));
    }
}
