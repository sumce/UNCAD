using System;

namespace UNCAD.Core.Contracts
{
    /// <summary>
    /// 功能注册特性：标记一个功能类，FeatureRegistry 启动时扫描，
    /// 自动注册命令、Ribbon 按钮与元数据。
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

        /// <summary>Ribbon 面板分组名（相同分组的命令进同一个面板）。</summary>
        public string RibbonPanel { get; set; }

        /// <summary>对外命令列表，分号分隔（如 "UNL;OPUNL"）。</summary>
        public string Commands { get; set; }
    }
}
