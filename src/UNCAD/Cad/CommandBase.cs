using System;
using System.Diagnostics;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;
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
            string command = GetType().Name;
            var stopwatch = Stopwatch.StartNew();
            try
            {
                Execute(ctx, state);
            }
            catch (Autodesk.AutoCAD.Runtime.Exception ex) when (ex.ErrorStatus == ErrorStatus.UserBreak)
            {
                // 用户按 ESC，静默退出
                Log.Info(command + " cancelled");
            }
            catch (Autodesk.AutoCAD.Runtime.Exception ex)
            {
                ctx.Write("\n错误: " + ex.Message);
                Log.Error(command + " failed", ex);
            }
            catch (System.Exception ex)
            {
                ctx.Write("\n错误: " + ex.Message);
                Log.Error(command + " failed", ex);
            }
            finally
            {
                stopwatch.Stop();
                Log.Info(command + " finished in " + stopwatch.ElapsedMilliseconds + "ms");
            }
        }

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
