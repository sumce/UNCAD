using System;
using System.Linq;
using System.Threading;
using UNCAD.Core.Excel;
using UNCAD.UI;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// 3.0 WPF 批量未匹配规格选择窗（D-038）：每行必须显式选择替代规格，
    /// 全部选择后才能确认；类别限定在请求构造时已收紧。
    /// </summary>
    [Collection("WpfUiSerial")]
    public sealed class BatchCatalogSelectionWindowTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Window_RequiresEveryExplicitChoiceAndSupportsCancellation(bool cancel)
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var catalog = new BoqCatalogIndex(ListItemReader.ReadEmbedded());
                    ListItem replacement = catalog.FindBusPlugBox("400A");
                    ListItem cable = catalog.Cables[0];
                    var requests = new[]
                    {
                        new BatchCatalogRequest("H1:busPlugBox", "MECTT03", "AC Rack 2-1",
                            "母线插接箱", "N208 3P4W 3P350A", catalog.Items),
                        new BatchCatalogRequest("H1:cable", "MECTT03", "AC Rack 2-1",
                            "电缆", "UNKNOWN", catalog.Items)
                    };
                    Assert.All(requests[0].Candidates, item =>
                        Assert.Equal("母线插接箱", item.Category));
                    var window = new BatchCatalogSelectionWindow(requests);
                    window.Show();
                    Pump(3);
                    var rows = window.CatalogGrid.Items
                        .Cast<BatchCatalogSelectionWindow.BatchCatalogRow>().ToList();
                    var confirm = window.ConfirmButton;

                    Assert.False(confirm.IsEnabled);
                    Assert.All(rows, row => Assert.Null(row.SelectedItem));
                    Assert.Equal("MECTT03 / AC Rack 2-1", rows[0].Frame);
                    Assert.Contains("350A", rows[0].Original);
                    Assert.Equal("母线插接箱", rows[0].Category);

                    rows[0].SelectedCode = replacement.Code;
                    Assert.Equal(replacement.Code, rows[0].SelectedCode);
                    Assert.False(confirm.IsEnabled, "仍有一行未选择");
                    rows[1].SelectedCode = cable.Code;
                    Assert.Equal(cable.Code, rows[1].SelectedCode);
                    Assert.True(confirm.IsEnabled,
                        "Selections were not accepted: " + rows[0].SelectedCode
                        + " / " + rows[1].SelectedCode);

                    if (cancel)
                    {
                        Click(window.CancelButton);
                        Pump(2);
                        Assert.False(window.Confirmed);
                        Assert.Null(window.Selections);
                    }
                    else
                    {
                        Click(confirm);
                        Pump(2);
                        Assert.True(window.Confirmed);
                        Assert.Same(replacement, window.Selections["H1:busPlugBox"]);
                        Assert.Same(cable, window.Selections["H1:cable"]);
                    }
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
        public void Window_WithNoRequests_CannotConfirm()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var window = new BatchCatalogSelectionWindow(
                        new BatchCatalogRequest[0]);
                    window.Show();
                    Pump(3);
                    Assert.False(window.ConfirmButton.IsEnabled);
                    Assert.Empty(window.CatalogGrid.Items);
                    window.Close();
                }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure != null) throw failure;
        }

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
