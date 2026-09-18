using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace UNCAD.UI
{
    /// <summary>
    /// 为插件弹窗提供局部 Per-Monitor DPI 上下文，不改变 AutoCAD 进程的全局 DPI 状态。
    /// </summary>
    public abstract class DpiAwareForm : Form
    {
        protected override void CreateHandle()
        {
            using (DpiAwarenessScope.Enter())
                base.CreateHandle();
        }

        protected override void SetVisibleCore(bool value)
        {
            using (DpiAwarenessScope.Enter())
                base.SetVisibleCore(value);
        }

        private sealed class DpiAwarenessScope : IDisposable
        {
            private static readonly IntPtr PerMonitorAwareV2 = new IntPtr(-4);
            private readonly IntPtr _previous;

            private DpiAwarenessScope(IntPtr previous)
            {
                _previous = previous;
            }

            public static DpiAwarenessScope Enter()
            {
                try
                {
                    return new DpiAwarenessScope(SetThreadDpiAwarenessContext(
                        PerMonitorAwareV2));
                }
                catch (EntryPointNotFoundException)
                {
                    return new DpiAwarenessScope(IntPtr.Zero);
                }
                catch (DllNotFoundException)
                {
                    return new DpiAwarenessScope(IntPtr.Zero);
                }
                catch (SEHException)
                {
                    return new DpiAwarenessScope(IntPtr.Zero);
                }
            }

            public void Dispose()
            {
                if (_previous == IntPtr.Zero) return;
                try { SetThreadDpiAwarenessContext(_previous); }
                catch (EntryPointNotFoundException) { }
                catch (DllNotFoundException) { }
                catch (SEHException) { }
            }

            [DllImport("user32.dll", EntryPoint = "SetThreadDpiAwarenessContext")]
            private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr value);
        }
    }
}
