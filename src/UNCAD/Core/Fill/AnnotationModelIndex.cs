using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UNCAD.Core.Excel;
using UNCAD.Core.Text;

namespace UNCAD.Core.Fill
{
    /// <summary>
    /// 固定清单「1.名称」→ 清单行的反查表，服务于两行标注的还原。
    ///
    /// 只收桥架与线管：这两类是图上会写成两行标注、且统计引擎有对应类别的。
    /// 软管标注走 Ruanguan 块属性，不参与文字统计，所以不在这里建映射——
    /// 没有映射就不会被误配成线管。
    /// </summary>
    public sealed class AnnotationModelIndex
    {
        private const string BridgeCategory = "桥架";
        private const string ConduitCategory = "线管";
        private const string BridgeSpecPrefix = "桥架";
        private const string ConduitPrefix = "⌀";
        private const string ConduitSuffix = "线管";

        private readonly Dictionary<string, ListItem> _bridges;
        private readonly Dictionary<string, ListItem> _conduits;

        private AnnotationModelIndex(Dictionary<string, ListItem> bridges,
            Dictionary<string, ListItem> conduits)
        {
            _bridges = bridges;
            _conduits = conduits;
        }

        public static AnnotationModelIndex Build(IEnumerable<ListItem> items)
        {
            var bridges = new Dictionary<string, ListItem>(StringComparer.Ordinal);
            var conduits = new Dictionary<string, ListItem>(StringComparer.Ordinal);
            foreach (ListItem item in items ?? Enumerable.Empty<ListItem>())
            {
                if (item == null) continue;
                string model = BoqFeatureName.Extract(item.Feature);
                if (model.Length == 0) continue;
                string category = (item.Category ?? "").Trim();
                Dictionary<string, ListItem> target;
                if (string.Equals(category, BridgeCategory, StringComparison.Ordinal))
                    target = bridges;
                else if (string.Equals(category, ConduitCategory, StringComparison.Ordinal))
                    target = conduits;
                else
                    continue;
                if (target.TryGetValue(model, out ListItem existing))
                    throw new InvalidDataException("固定清单中“" + model
                        + "”重复，无法唯一识别图上标注（" + existing.Code + " 与 "
                        + item.Code + "）。");
                target[model] = item;
            }
            return new AnnotationModelIndex(bridges, conduits);
        }

        /// <summary>
        /// 把「型号行 + 长度行」折算回统计引擎认识的单行写法
        /// （桥架200*100 2500mm / ⌀20线管 2000mm）。
        /// 型号不在清单里、或长度不是纯长度写法时返回 false——调用方必须原样保留两行，
        /// 绝不能凭猜测合并。
        /// </summary>
        public bool TryCollapse(string model, string length, out string collapsed)
        {
            collapsed = "";
            string modelText = (model ?? "").Trim();
            string lengthText = (length ?? "").Trim();
            if (modelText.Length == 0 || !TextParser.IsBareLengthToken(lengthText))
                return false;

            // 还原出来的行必须与图上单行写法逐字一致，否则统计引擎认不出来：
            // 桥架 “桥架200*100 2500mm”（BridgeLabelFormatter 的毫米写法）、
            // 线管 “⌀20线管 2000mm”（ConduitLabelFormatter）。
            // 这两个格式在这里重现而不是调用那两个格式化器：ConduitLabelFormatter
            // 刻意不暴露“带实测长度”的重载（防止再次接入曲线实测距离），
            // 这里要的却是把已有的长度文字拼回去，语义不同。
            // AnnotationLabelPairTests 用内嵌清单把两处格式锁在一起。
            if (_bridges.TryGetValue(modelText, out ListItem bridge))
            {
                string spec = BoqCatalogIndex.NormalizeSpec(bridge.Alias);
                if (spec.Length == 0) return false;
                collapsed = BridgeSpecPrefix + spec + " " + lengthText;
                return true;
            }
            if (_conduits.TryGetValue(modelText, out ListItem conduit))
            {
                string diameter = ConduitDiameter.NormalizeOrEmpty(conduit.Alias);
                if (diameter.Length == 0) return false;
                collapsed = ConduitPrefix + diameter + ConduitSuffix + " " + lengthText;
                return true;
            }
            return false;
        }
    }
}
