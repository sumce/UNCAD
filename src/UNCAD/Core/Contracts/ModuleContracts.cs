using System;

namespace UNCAD.Core.Contracts
{
    public sealed class ModuleDescriptor
    {
        public ModuleDescriptor(string id, string name)
        {
            Id = string.IsNullOrWhiteSpace(id)
                ? throw new ArgumentException("Module ID is required.", nameof(id))
                : id.Trim();
            Name = string.IsNullOrWhiteSpace(name)
                ? throw new ArgumentException("Module name is required.", nameof(name))
                : name.Trim();
        }

        public string Id { get; }
        public string Name { get; }
        public string Label => Id + "/" + Name;
    }

    public sealed class ModuleExecutionException : Exception
    {
        public ModuleExecutionException(ModuleDescriptor module, string stage,
            Exception innerException)
            : base(BuildMessage(module, stage, innerException), innerException)
        {
            Module = module ?? throw new ArgumentNullException(nameof(module));
            Stage = string.IsNullOrWhiteSpace(stage) ? "执行" : stage.Trim();
        }

        public ModuleDescriptor Module { get; }
        public string Stage { get; }

        private static string BuildMessage(ModuleDescriptor module, string stage,
            Exception innerException)
        {
            if (module == null) throw new ArgumentNullException(nameof(module));
            string operation = string.IsNullOrWhiteSpace(stage) ? "执行" : stage.Trim();
            string detail = innerException?.Message ?? "未知错误";
            return "模块[" + module.Label + "]在[" + operation + "]阶段失败: " + detail;
        }
    }
}
