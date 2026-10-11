using System;
using System.IO;
using UNCAD.Core.Contracts;
using UNCAD.Core.Submission;
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

        [Fact]
        public void BoqOutputModule_IdentifiesTheWorkbookStageInLogsAndFailures()
        {
            // BOQ 工作簿输出以前没有模块标识：失败只能从命令入口整体猜。
            // 本测试钉住模块ID/名称与阶段名，并证明失败消息同时保留原始原因。
            Assert.Equal("BOQ-XLSX", BoqOutputModule.Descriptor.Id);
            Assert.Equal("清单输出", BoqOutputModule.Descriptor.Name);

            ModuleExecutionException error = Assert.Throws<ModuleExecutionException>(() =>
                ModuleRunner.Run(BoqOutputModule.Descriptor, "写入 BOQ 工作簿",
                    () => throw new InvalidDataException(
                        "BOQ 项目 1.1 的工程量不能为负数：-3")));

            Assert.Contains("BOQ-XLSX/清单输出", error.Message);
            Assert.Contains("写入 BOQ 工作簿", error.Message);
            Assert.Contains("不能为负数", error.Message);
            Assert.IsType<InvalidDataException>(error.InnerException);
        }
    }
}
