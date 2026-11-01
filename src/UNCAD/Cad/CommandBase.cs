using System;
using System.Diagnostics;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;
using UNCAD.Core.Contracts;
using UNCAD.Infra;

namespace UNCAD.Cad
{
    /// <summary>
    /// 命令基类：统一错误处理（ESC 静默、其余打印+日志）与文档上下文。
    /// 子类只需实现 Execute(CadContext) 并用 [CommandMethod] 包装 Run()。
    /// </summary>
    public abstract class CommandBase
    {
        /// <summary>
        /// 命令入口：由 [CommandMethod] 方法调用。
        /// state：向 Execute 传参（如导出模式、预设规格），不需要时直接 Run()。
        /// </summary>
        public void Run(object state = null)
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var ctx = new CadContext(doc);
            string command = ResolveCommandId();
            string summary = ResolveFeatureSummary();
            var stopwatch = Stopwatch.StartNew();
            // 2.10.0：命令标签进文件日志；操作记录写本地 JSONL（失败不影响命令）。
            using (Log.Command(command))
            using (OperationLogScope operation = OperationLog.Begin(command, summary))
            {
                ctx.Operation = operation;
                try
                {
                    Execute(ctx, state);
                    operation.Complete();
                }
                catch (Autodesk.AutoCAD.Runtime.Exception ex) when (ex.ErrorStatus == ErrorStatus.UserBreak)
                {
                    // 用户按 ESC，静默退出
                    Log.Info(command + " cancelled");
                    operation.Cancel("用户取消（ESC）");
                }
                catch (Autodesk.AutoCAD.Runtime.Exception ex)
                {
                    ctx.Write("\n错误: " + ex.Message);
                    Log.Error(command + " failed", ex);
                    operation.Fail("命令失败", ex);
                }
                catch (System.Exception ex)
                {
                    ctx.Write("\n错误: " + ex.Message);
                    Log.Error(command + " failed", ex);
                    operation.Fail("命令失败", ex);
                }
                finally
                {
                    // AutoCAD keeps implied selection after ESC or an exception. Clear it for
                    // every command so the next UsePickSet command cannot silently reuse stale IDs.
                    try { SelectionService.ClearPickFirst(ctx); } catch { }
                    stopwatch.Stop();
                    Log.Info(command + " finished in " + stopwatch.ElapsedMilliseconds + "ms");
                }
            }
        }

        /// <summary>
        /// 当前命令名：优先取 AutoCAD 正在执行的命令（一次类里可能注册多个命令，
        /// 例如 U1F/U1U），退回 [Feature] 的第一个命令，再退回类型名。
        /// </summary>
        private string ResolveCommandId()
        {
            try
            {
                string names = Convert.ToString(
                    Application.GetSystemVariable("CMDNAMES")) ?? "";
                foreach (string name in names.Split(';'))
                {
                    string candidate = name.Trim().ToUpperInvariant();
                    if (candidate.Length > 0) return candidate;
                }
            }
            catch { /* 系统变量不可用时走特性回退 */ }
            FeatureAttribute attribute = GetFeatureAttribute(GetType());
            if (attribute != null && !string.IsNullOrWhiteSpace(attribute.Commands))
            {
                string first = attribute.Commands.Split(';')[0].Trim();
                if (first.Length > 0) return first;
            }
            return "UNKNOWN";
        }

        private string ResolveFeatureSummary()
        {
            FeatureAttribute attribute = GetFeatureAttribute(GetType());
            string display = attribute == null ? "" : (attribute.DisplayName ?? "").Trim();
            return display.Length == 0 ? GetType().Name : display;
        }

        private static FeatureAttribute GetFeatureAttribute(Type type)
            => (FeatureAttribute)Attribute.GetCustomAttribute(type,
                typeof(FeatureAttribute), false);

        /// <summary>给非 Execute 流程（如设置对话框）复用的错误保护。</summary>
        protected static void Guard(Action body)
        {
            try
            {
                body();
            }
            catch (Autodesk.AutoCAD.Runtime.Exception ex) when (ex.ErrorStatus == ErrorStatus.UserBreak)
            {
            }
            catch (Autodesk.AutoCAD.Runtime.Exception ex)
            {
                WriteError(ex.Message);
                Log.Error("Guarded command failed", ex);
            }
            catch (System.Exception ex)
            {
                WriteError(ex.Message);
                Log.Error("Guarded command failed", ex);
            }
        }

        private static void WriteError(string msg)
        {
            var ed = Application.DocumentManager.MdiActiveDocument?.Editor;
            ed?.WriteMessage("\n错误: " + msg);
        }

        /// <summary>业务逻辑实现（无参数命令）。</summary>
        protected abstract void Execute(CadContext ctx);

        /// <summary>带参数业务逻辑；默认转发到 Execute(ctx)。需要参数的命令重写此方法。</summary>
        protected virtual void Execute(CadContext ctx, object state) => Execute(ctx);
    }
}
