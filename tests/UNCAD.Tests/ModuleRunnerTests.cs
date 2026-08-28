using System;
using UNCAD.Core.Contracts;
using UNCAD.Infra;
using Xunit;

namespace UNCAD.Tests
{
    public class ModuleRunnerTests
    {
        [Fact]
        public void Run_WrapsFailureWithModuleAndStageContext()
        {
            var module = new ModuleDescriptor("BOQ-TABLE", "清单表格");

            ModuleExecutionException error = Assert.Throws<ModuleExecutionException>(() =>
                ModuleRunner.Run(module, "写入CAD表格", () =>
                    throw new InvalidOperationException("目标表格已损坏")));

            Assert.Same(module, error.Module);
            Assert.Equal("写入CAD表格", error.Stage);
            Assert.IsType<InvalidOperationException>(error.InnerException);
            Assert.Contains("BOQ-TABLE/清单表格", error.Message);
            Assert.Contains("写入CAD表格", error.Message);
            Assert.Contains("目标表格已损坏", error.Message);
        }
    }
}
