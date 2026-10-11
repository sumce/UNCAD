using UNCAD.Core.Contracts;

namespace UNCAD.Core.Submission
{
    /// <summary>
    /// BOQ 工作簿输出阶段（读取图框记录 → 写入机台 xlsx）的模块标识。
    ///
    /// 为什么单独成一个模块：这是"有外部副作用、可能失败"的阶段，按
    /// docs/DEVELOPMENT.md 必须经 ModuleRunner 执行，日志与用户可见失败都要能按
    /// 模块ID定位。此前该阶段没有模块标识，出问题只能从命令入口整体猜。
    /// </summary>
    public static class BoqOutputModule
    {
        public static readonly ModuleDescriptor Descriptor =
            new ModuleDescriptor("BOQ-XLSX", "清单输出");
    }
}
