using System;
using System.Reflection;
using System.Windows.Forms;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;
using UNCAD.Infra;
using UNCAD.UI;

[assembly: ExtensionApplication(typeof(UNCAD.Bootstrap))]

namespace UNCAD
{
    /// <summary>插件启动：预热 WinForms IME 表、扫描 Feature 注册表、构建 Ribbon、输出横幅。</summary>
    public class Bootstrap : IExtensionApplication
    {
        private static bool _licenseIdleAttached;
        private static bool _licenseDialogOpen;

        public void Initialize()
        {
            WarmUpWinFormsImeConversion();
            var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager
                .MdiActiveDocument;
            doc?.Editor.WriteMessage(
                "\n[UNCAD] " + Branding.Nameplate + " | 已加载：" + FeatureRegistry.Summary);
            RibbonBuilder.Build();
            if (ProductMetadata.RequiresOnlineLicense)
            {
                Autodesk.AutoCAD.ApplicationServices.Application.Idle += OnLicenseIdle;
                _licenseIdleAttached = true;
                OnlineLicenseMonitor.Start();
            }
            Log.Info("UNCAD initialized; Ribbon registration requested");
        }

        /// <summary>
        /// .NET Framework 的 ImeModeConversion 惰性初始化不是线程安全的：两个线程同时创建
        /// WinForms 控件（例如本插件对话框与 AutoCAD 自身 UI）时，同一字典被插入两次，
        /// 抛出“已添加了具有相同键的项”。加载时在主线程提前初始化一次即可消除竞争窗口。
        /// </summary>
        private static void WarmUpWinFormsImeConversion()
        {
            try
            {
                Type ime = typeof(Control).Assembly.GetType(
                    "System.Windows.Forms.ImeModeConversion");
                if (ime == null) return;
                const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
                object table = ime.GetProperty("ImeModeConversion", flags)
                        ?.GetValue(null, null)
                    ?? ime.GetField("ImeModeConversion", flags)?.GetValue(null);
                Log.Info("WinForms IME conversion warmup "
                    + (table != null ? "ok" : "skipped"));
            }
            catch (System.Exception ex)
            {
                Log.Warn("WinForms IME warmup failed: " + ex.Message);
            }
        }

        private static void OnLicenseIdle(object sender, EventArgs args)
        {
            if (_licenseDialogOpen) return;
            if (OnlineLicenseMonitor.TryTakeActivationRequest(out string reason))
            {
                try
                {
                    _licenseDialogOpen = true;
                    using (var form = new OnlineLicenseForm(reason))
                        Autodesk.AutoCAD.ApplicationServices.Application.ShowModalDialog(form);
                }
                finally { _licenseDialogOpen = false; }
                return;
            }
            if (!OnlineLicenseMonitor.TryTakeNotice(out OnlineLicenseNotice notice)) return;
            MessageBoxIcon icon = notice.Level == "error" ? MessageBoxIcon.Error
                : notice.Level == "info" ? MessageBoxIcon.Information
                : MessageBoxIcon.Warning;
            IntPtr handle = Autodesk.AutoCAD.ApplicationServices.Application.MainWindow?.Handle
                ?? IntPtr.Zero;
            MessageBox.Show(new WindowWrapper(handle), notice.Message, notice.Title,
                MessageBoxButtons.OK, icon);
        }

        public void Terminate()
        {
            if (_licenseIdleAttached)
            {
                Autodesk.AutoCAD.ApplicationServices.Application.Idle -= OnLicenseIdle;
                _licenseIdleAttached = false;
            }
            OnlineLicenseMonitor.Stop();
        }
    }
}
