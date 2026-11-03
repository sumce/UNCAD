using System;
using System.Linq;
using System.Threading;
using UNCAD.Core.Excel;
using UNCAD.UI;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// 3.0 WPF 电缆替代型号选择窗（D-038）：单关键词过滤、首行自动选中、
    /// 双击/按钮确认，取消语义为“保持原型号”。
    /// </summary>
    [Collection("WpfUiSerial")]
    public sealed class CableCatalogSelectionWindowTests
    {
        [Fact]
        public void Window_FiltersAndReturnsExplicitCatalogCable()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var candidates = new[]
                    {
                        Item("1.1", "3*2.5"),
                        Item("1.2", "5*6")
                    };
                    var window = new CableCatalogSelectionWindow(candidates, "UNKNOWN");
                    window.Show();
                    Pump(3);

                    Assert.Contains("UNKNOWN", window.CurrentModelText.Text);
                    // 空关键词显示全部并自动选中第一行。
                    Assert.Equal(2, window.CableGrid.Items.Count);
                    Assert.True(window.ConfirmButton.IsEnabled);

                    window.SearchBox.Text = "5*6";
                    Pump(2);
                    Assert.Single(window.CableGrid.Items);
                    var row = (CableCatalogSelectionWindow.CableRow)
                        window.CableGrid.Items[0];
                    Assert.Equal("1.2", row.Item.Code);
                    Click(window.ConfirmButton);
                    Pump(2);
                    Assert.True(window.Confirmed);
                    Assert.Equal("1.2", window.SelectedItem.Code);
                    Assert.Equal("5*6", window.SelectedItem.Spec);
                    window.Close();
                }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure != null) throw failure;
        }

        [Fact]
        public void Cancel_KeepsOriginalModel()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var window = new CableCatalogSelectionWindow(
                        new[] { Item("1.1", "3*2.5") }, "ZB-YJVR-5*6");
                    window.Show();
                    Pump(3);

                    Click(window.CancelButton);
                    Pump(2);
                    Assert.False(window.Confirmed, "取消=保持原型号，不产生确认结果");
                    window.Close();
                }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure != null) throw failure;
        }

        private static ListItem Item(string code, string spec)
            => new ListItem
            {
                Code = code,
                Spec = spec,
                Name = "电缆" + spec,
                Feature = "模板" + spec,
                Unit = "m"
            };

        private static void Click(System.Windows.Controls.Primitives.ButtonBase button)
        {
            Assert.NotNull(button);
            button.RaiseEvent(new System.Windows.RoutedEventArgs(
                System.Windows.Controls.Primitives.ButtonBase.ClickEvent, button));
        }

        private static void Pump(int rounds)
        {
            for (var i = 0; i < rounds; i++)
            {
                var frame = new System.Windows.Threading.DispatcherFrame();
                System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
                    System.Windows.Threading.DispatcherPriority.Loaded,
                    new Action(() => frame.Continue = false));
                System.Windows.Threading.Dispatcher.PushFrame(frame);
                System.Threading.Thread.Sleep(30);
            }
        }
    }
}
