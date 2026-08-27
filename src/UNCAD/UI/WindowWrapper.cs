using System;
using System.Windows.Forms;

namespace UNCAD.UI
{
    /// <summary>把 AutoCAD 主窗口句柄包装为 IWin32Window，供 WinForms 对话框作为所有者。</summary>
    internal sealed class WindowWrapper : IWin32Window
    {
        private readonly IntPtr _handle;

        public WindowWrapper(IntPtr handle) { _handle = handle; }

        public IntPtr Handle => _handle;
    }
}
