using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// WPF 窗体测试统一串行集合：Wpf.Ui 的主题管理器在进程内有共享状态，
    /// 多个 STA 线程并行创建/关闭窗口时偶发竞态（重跑即过的 flaky）。
    /// 所有 WPF 窗体测试类必须标注 [Collection("WpfUiSerial")]。
    /// </summary>
    [CollectionDefinition("WpfUiSerial", DisableParallelization = true)]
    public sealed class WpfUiSerialCollection
    {
    }
}
