using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using UNCAD.Core.Excel;
using UNCAD.UI;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// 3.0 WPF 固定清单选择窗（D-038，P2 模式）：分词搜索、类别胶囊、
    /// 最近使用置顶、连续添加、替换模式锁定类别。
    /// </summary>
    [Collection("WpfUiSerial")]
    public sealed class ManualListItemWindowTests
    {
        private static ListItem[] Catalog() => new[]
        {
            new ListItem
            {
                Category = "插座", Code = "8.9", Name = "插座",
                Feature = "普通五孔插座", Unit = "个", Alias = "五孔"
            },
            new ListItem
            {
                Category = "桥架", Code = "2.6", Name = "桥架",
                Feature = "200*100桥架", Unit = "m", Alias = "200*100"
            },
            new ListItem
            {
                Category = "电缆", Code = "1.1", Name = "多芯电缆 XLPE",
                Feature = "电缆清单模板", Unit = "m", Alias = "3*2.5"
            }
        };

        [Fact]
        public void Select_ExplicitItem_And_CaptureOnlyQuantity()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var window = new ManualListItemWindow(Catalog());
                    window.Show();
                    Pump(3);

                    window.SearchBox.Text = "五孔";
                    Pump(2);
                    Assert.Single(window.CatalogGrid.Items);
                    window.QuantityBox.Value = 2;
                    Click(window.ConfirmButton);
                    Pump(2);
                    Assert.True(window.Confirmed);
                    Assert.Equal("8.9", window.SelectedItem.Code);
                    Assert.Equal("插座", window.SelectedItem.Name);
                    Assert.Equal("普通五孔插座", window.SelectedItem.Feature);
                    Assert.Equal("个", window.SelectedItem.Unit);
                    Assert.Equal("2", window.Quantity);
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
        public void ContinuousAdd_FiresPickAndKeepsWindowOpen()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var picked = new List<ListItem>();
                    var window = new ManualListItemWindow(Catalog());
                    window.PickRequested += (item, quantity) =>
                    {
                        picked.Add(item);
                        Assert.Equal("2", quantity);
                    };
                    window.Show();
                    Pump(3);

                    window.SearchBox.Text = "五孔";
                    Pump(2);
                    window.QuantityBox.Value = 2;
                    // 添加并继续：不关窗，触发一次 PickRequested。
                    Click(window.ContinueButton);
                    Pump(2);
                    Assert.Single(picked);
                    Assert.False(window.Confirmed);

                    // 窗口仍开着：还能“添加所选清单”正常关闭。
                    Click(window.ConfirmButton);
                    Pump(2);
                    Assert.True(window.Confirmed);
                    Assert.Single(picked);
                    Assert.Equal("8.9", picked[0].Code);
                    Assert.Equal("8.9", window.SelectedItem.Code);
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
        public void TokenizedSearch_RequiresEveryKeyword()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var window = new ManualListItemWindow(Catalog());
                    window.Show();
                    Pump(3);

                    window.SearchBox.Text = "插座 五孔";
                    Pump(2);
                    var socketOnly = Rows(window);
                    Assert.Single(socketOnly);
                    Assert.Equal("8.9", socketOnly[0].Item.Code);

                    window.SearchBox.Text = "200 桥架";
                    Pump(2);
                    var bridgeOnly = Rows(window);
                    Assert.Single(bridgeOnly);
                    Assert.Equal("2.6", bridgeOnly[0].Item.Code);

                    // 两个关键词不能同时在两类上命中 → 空结果。
                    window.SearchBox.Text = "五孔 桥架";
                    Pump(2);
                    Assert.Empty(Rows(window));
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
        public void RecentCategory_ShowsOnlyPickedItems()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var window = new ManualListItemWindow(Catalog());
                    window.Show();
                    Pump(3);

                    window.SearchBox.Text = "五孔";
                    Pump(2);
                    window.CatalogGrid.SelectedIndex = 0;
                    Click(window.ContinueButton);
                    Pump(2);

                    window.CategoryList.SelectedItem = "最近使用";
                    Pump(2);
                    var recent = Rows(window).Select(row => row.Item.Code).ToList();
                    Assert.Contains("8.9", recent);
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
        public void ReplacementMode_OpensRequestedCableTab_AndFiltersRows()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var window = new ManualListItemWindow(Catalog(), true, "电缆");
                    window.Show();
                    Pump(3);

                    Assert.DoesNotContain(window.CategoryNames, name => name == "桥架");
                    Assert.Contains(window.CategoryNames, name => name == "电缆");
                    Assert.Equal("电缆", window.SelectedCategory);
                    var rows = Rows(window);
                    Assert.Single(rows);
                    Assert.Equal("1.1", rows[0].Item.Code);
                    // 替换模式：没有“添加并继续”和数量行，确认文案不同。
                    Assert.Equal(Visibility.Collapsed, window.ContinueButton.Visibility);
                    Assert.Equal(Visibility.Collapsed, window.QuantityBar.Visibility);
                    Assert.Equal("使用所选清单",
                        Convert.ToString(window.ConfirmButton.Content));
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
        public void Confirm_WithoutSelection_IsDisabled()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var window = new ManualListItemWindow(Catalog());
                    window.Show();
                    Pump(3);

                    window.SearchBox.Text = "不存在的关键词";
                    Pump(2);
                    Assert.Empty(Rows(window));
                    Assert.False(window.ConfirmButton.IsEnabled);
                    window.Close();
                }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure != null) throw failure;
        }

        [Theory]
        [InlineData("2.6", "10.1", -1)] // 数字段按数值比较：2 < 10
        [InlineData("10.1", "2.6", 1)]
        [InlineData("1.1", "1.1", 0)]
        [InlineData("a2", "a10", -1)]
        [InlineData("A", "a", 0)] // 大小写不敏感
        [InlineData("B", "a", 1)]
        public void NaturalCompare_SortsNumbersByValue(string x, string y, int sign)
            => Assert.Equal(sign, Math.Sign(ManualListItemWindow.NaturalCompare(x, y)));

        private static List<ManualListItemWindow.CatalogRow> Rows(
            ManualListItemWindow window)
            => window.CatalogGrid.Items.Cast<ManualListItemWindow.CatalogRow>().ToList();

        private static void Click(System.Windows.Controls.Primitives.ButtonBase button)
        {
            Assert.NotNull(button);
            button.RaiseEvent(new RoutedEventArgs(
                System.Windows.Controls.Primitives.ButtonBase.ClickEvent, button));
        }

        private static void Pump(int rounds)
        {
            for (var i = 0; i < rounds; i++)
            {
                var frame = new DispatcherFrame();
                Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Loaded,
                    new Action(() => frame.Continue = false));
                Dispatcher.PushFrame(frame);
                Thread.Sleep(30);
            }
        }
    }
}
