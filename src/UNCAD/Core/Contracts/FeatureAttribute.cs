using System;

namespace UNCAD.Core.Contracts
{
    /// <summary>
    /// 功能元数据特性：FeatureRegistry 启动时扫描，用于诊断和命令清单。
    /// AutoCAD 命令由 CommandMethod 注册，任务化 Ribbon 由 RibbonCatalog 定义。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public sealed class FeatureAttribute : Attribute
    {
        public FeatureAttribute(string id, string displayName)
        {
            Id = id;
            DisplayName = displayName;
        }

        /// <summary>功能唯一标识（如 "unl"）。</summary>
        public string Id { get; }

        /// <summary>显示名称（用于 Ribbon/菜单）。</summary>
        public string DisplayName { get; }

        /// <summary>功能描述（Ribbon 按钮提示）。</summary>
        public string Description { get; set; }


        /// <summary>对外命令列表，分号分隔（如 "UNL;OPUNL"）。</summary>
        public string Commands { get; set; }
    }
}
