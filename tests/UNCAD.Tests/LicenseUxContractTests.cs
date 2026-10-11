using System;
using UNCAD.Infra;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// 授权不可用时的**用户体验契约**回归锁。
    ///
    /// 背景（真实用户反馈）：用户只因为没有登录，就被弹窗挡住、并且再也找不到重新登录的入口，
    /// 体验上等同于"连 AutoCAD 都不能用了"。而正确行为只有两条：
    ///   1) **不登录不能用 UNCAD 命令，但绝不影响 AutoCAD 本身**；
    ///   2) 任何时候都要给得出**可操作的登录出口**（运行 UNC_LOGIN）。
    ///
    /// 这三条一旦被改回去，用户的 CAD 会再次被挡住，因此必须有测试守着。
    /// </summary>
    public class LicenseUxContractTests
    {
        /// <summary>
        /// 周期性心跳**不得**自行弹出登录窗口。
        ///
        /// 旧实现会在每次心跳探测到授权不可用时排入"激活请求"（Bootstrap 在 Idle 里
        /// 用 ShowModalDialog 弹出登录窗）。ShowModalDialog 是**应用程序模态**的，
        /// 弹出期间用户无法使用 AutoCAD——这正是用户反馈"不让我用 CAD"的直接原因。
        /// 现在只有用户主动操作才会请求登录，因此未登录/心跳失败后队列里必须是空的。
        /// </summary>
        [Fact]
        public void Periodic_heartbeat_does_not_queue_a_modal_login_request()
        {
            // 清掉可能残留的状态
            OnlineLicenseMonitor.TryTakeActivationRequest(out _);

            // 未登录时客户端状态是 missing，且**不应**产生激活请求
            OnlineLicenseMonitor.RequestUserLogin(string.Empty);
            Assert.True(OnlineLicenseMonitor.TryTakeActivationRequest(out _),
                "前置条件：RequestUserLogin 应能排入请求");

            // 取走之后，没有新的用户操作就不应再有请求
            Assert.False(OnlineLicenseMonitor.TryTakeActivationRequest(out _),
                "心跳本身不应排入激活请求（否则会弹模态窗口挡住 CAD）");
        }

        /// <summary>
        /// 用户主动操作必须**每次都能**唤出登录入口。
        ///
        /// 旧实现按 (账号, 原因) 去重，弹过一次后登录窗口再也不出现，
        /// 用户被永久卡在"只有一句错误、没地方登录"的状态（用户原话：
        /// "也不让我重新登录啥的"）。因此这里刻意断言**重复请求仍然有效**。
        /// </summary>
        [Fact]
        public void User_initiated_login_request_is_never_permanently_deduplicated()
        {
            const string reason = "账号不存在或已被删除。";

            for (int attempt = 1; attempt <= 3; attempt++)
            {
                OnlineLicenseMonitor.RequestUserLogin(reason);
                Assert.True(OnlineLicenseMonitor.TryTakeActivationRequest(out string taken),
                    $"第 {attempt} 次用户操作都应能唤出登录窗口");
                Assert.Equal(reason, taken);
            }
        }

        /// <summary>提示文案必须给出可操作出口，并说明不影响 CAD 本身。</summary>
        [Fact]
        public void Blocked_command_message_tells_the_user_what_to_do()
        {
            string message = ProductMetadata.BuildBlockedCommandMessage(
                "账号不存在或已被删除，请联系UNSIAO.Ltd处理。", "U1F");

            // 1) 必须告诉用户运行哪个命令
            Assert.Contains("UNC_LOGIN", message);
            // 2) 必须说明只是 UNCAD 命令被停用，CAD 本身不受影响
            Assert.Contains("不影响 AutoCAD", message);
            // 3) 必须带上原始原因，不能吞掉服务端给的信息
            Assert.Contains("账号不存在或已被删除", message);
            // 4) 必须指明是哪个命令被停用
            Assert.Contains("U1F", message);
        }

        /// <summary>服务端没给原因时也要有可读文案（不能出现空提示）。</summary>
        [Fact]
        public void Blocked_command_message_has_a_fallback_reason()
        {
            string message = ProductMetadata.BuildBlockedCommandMessage(null, "U1U");
            Assert.Contains("授权不可用", message);
            Assert.Contains("UNC_LOGIN", message);
        }

        /// <summary>
        /// UNC_LOGIN 命令本身**绝不能**被授权检查拦住——否则用户没有任何自救途径。
        /// （ClientLoginFeature 明确不调用 EnsureCommandAllowed；这里锁住该前提。）
        /// </summary>
        [Fact]
        public void Login_command_is_not_gated_by_license()
        {
            string source = System.IO.File.ReadAllText(
                FindSource("src/UNCAD/Features/About/ClientLoginFeature.cs"));
            // 断言"没有调用"（带括号），而不是"没提到"：
            // 该文件的文档注释里**故意**写了 "<see cref=...EnsureCommandAllowed/>" 说明缘由，
            // 那是对的，不该被判失败。
            Assert.DoesNotContain("EnsureCommandAllowed(", source);
        }

        /// <summary>
        /// **通知取走即清空**——否则命令行会无限刷屏。
        ///
        /// 背景（真实用户反馈）："怎么输入框一直刷屏这个"。
        /// `TryTakeNotice` 由 `Bootstrap.OnLicenseIdle` 在**每个 AutoCAD Idle 事件**里调用，
        /// 而 Idle 会持续触发。旧实现忘了清空 `_pendingNotice`，于是同一条通知
        /// （例如一条"维护通知"公告）被反复取出、反复写进命令行，用户看到的是一屏又一屏的重复文字。
        ///
        /// 正确行为：取走一次之后就为空，第二次取必须返回 false。
        /// 这条锁死的是**取走侧**；入队侧的去重（`QueueNotice`）另有一套。
        /// </summary>
        [Fact]
        public void Notice_is_consumed_once_so_the_command_line_cannot_flood()
        {
            string source = System.IO.File.ReadAllText(
                FindSource("src/UNCAD/Infra/OnlineLicenseMonitor.cs"));

            int start = source.IndexOf("internal static bool TryTakeNotice",
                StringComparison.Ordinal);
            Assert.True(start >= 0, "找不到 TryTakeNotice 的实现");

            // 取方法体（到下一个方法或文件末尾）
            int end = source.IndexOf("/// <summary>", start + 1, StringComparison.Ordinal);
            string body = end > start ? source.Substring(start, end - start)
                                      : source.Substring(start);

            // 必须把 _pendingNotice 置空，否则同一个通知会被无限取出
            Assert.Contains("_pendingNotice = null", body);

            // 与另一条同样"取走即清空"的路径保持一致（防止只改一处又漏一处）
            int activationStart = source.IndexOf("internal static bool TryTakeActivationRequest",
                StringComparison.Ordinal);
            Assert.True(activationStart >= 0, "找不到 TryTakeActivationRequest 的实现");
            int activationEnd = source.IndexOf("/// <summary>", activationStart + 1,
                StringComparison.Ordinal);
            string activationBody = activationEnd > activationStart
                ? source.Substring(activationStart, activationEnd - activationStart)
                : source.Substring(activationStart);
            Assert.Contains("_pendingActivationReason = null", activationBody);
        }

        /// <summary>
        /// 用户看到的那句提示必须点名 **UNC_LOGIN**（而不是别的拼写），
        /// 否则用户照着敲会得到"未知命令"。
        /// </summary>
        [Fact]
        public void Notice_text_names_the_real_login_command()
        {
            string source = System.IO.File.ReadAllText(
                FindSource("src/UNCAD/Bootstrap.cs"));
            int index = source.IndexOf("此提示不影响 AutoCAD 使用", StringComparison.Ordinal);
            Assert.True(index >= 0, "找不到命令行提示文案");
            string tail = source.Substring(index, Math.Min(120, source.Length - index));
            Assert.Contains("UNC_LOGIN", tail);
            // 常见误写：把字母 O 写成数字 0（用户实际报障里就出现了这种字样）
            Assert.DoesNotContain("UNC_L0GIN", tail);
        }

        private static string FindSource(string relativePath)
        {
            var directory = new System.IO.DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (directory != null)
            {
                string candidate = System.IO.Path.Combine(directory.FullName, relativePath);
                if (System.IO.File.Exists(candidate)) return candidate;
                directory = directory.Parent;
            }
            throw new InvalidOperationException("找不到源文件：" + relativePath);
        }
    }
}
