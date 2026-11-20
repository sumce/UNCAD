using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using UNCAD.Core.Excel;
using UNCAD.Core.Fill;
using UNCAD.UI;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// 3.0 WPF 清单确认窗（D-038，P2 模式）：勾选/数量即时写回、
    /// 设备内预留联动重算电缆长度、未匹配行禁用、权威校验在“确认填充”。
    /// </summary>
    [Collection("WpfUiSerial")]
    public sealed class FillReviewWindowTests
    {
        [Fact]
        public void Window_ShowsEditableBasicsAndEveryMatchedItemChecked()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var rows = new List<TableFillRow>
                    {
                        new TableFillRow { Category = TableFillCategory.Cable, Name = "电缆", Quantity = "5", Code = "1.1", CatalogMatched = true },
                        new TableFillRow { Category = TableFillCategory.Bridge, Name = "桥架", Quantity = "2", Code = "2.1", CatalogMatched = true },
                        new TableFillRow { Category = TableFillCategory.Outlet, Name = "插座", Quantity = "1", Code = "8.1", CatalogMatched = true }
                    };
                    var window = new FillReviewWindow(FillReviewData.Create(
                        new MachineRow { MachineId = "M1", CircuitName = "设备A", Cable = "C1" }, rows));
                    window.Show();
                    Pump(3);

                    Assert.Equal(new[] { "基础信息", "清单选择" },
                        new[] { ReviewTabHeader(window, 0), ReviewTabHeader(window, 1) });
                    Assert.Equal("M1", window.MachineBox.Text);
                    Assert.Equal("设备A", window.CircuitBox.Text);
                    Assert.Equal("C1", window.OriginalCableBox.Text);
                    Assert.Equal("C1", window.CableBox.Text);
                    Assert.Equal(3, window.ReviewGrid.Items.Count);
                    // 名称/特征/单位/编码只读，仅“生成”和“数量/长度”可编辑。
                    Assert.Equal(5, window.ReviewGrid.Columns.Count(column => column.IsReadOnly));
                    Assert.Equal(2, window.ReviewGrid.Columns.Count(column => !column.IsReadOnly));
                    Assert.All(window.ReviewGrid.Items.Cast<FillReviewRow>(), row =>
                        Assert.True(row.Included));

                    // 勾选即时写回 Data（绑定 PropertyChanged，无悬挂编辑）。
                    var outlet = (FillReviewRow)window.ReviewGrid.Items[2];
                    outlet.Included = false;
                    Assert.False(window.Data.Items[2].Included);
                    Assert.Contains("将生成 2 / 3", window.CountText.Text);

                    // 删除选中行。
                    window.ReviewGrid.SelectedItem = outlet;
                    Click(window.RemoveItemButton);
                    Pump(2);
                    Assert.Equal(2, window.ReviewGrid.Items.Count);
                    Assert.Equal(2, window.Data.Items.Count);
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
        public void UncheckedOutlet_ImmediatelyConfirmed_IsNotSelected()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var rows = new List<TableFillRow>
                    {
                        new TableFillRow { Category = TableFillCategory.Cable, Name = "电缆", Quantity = "5", Code = "1.1", CatalogMatched = true },
                        new TableFillRow { Category = TableFillCategory.Outlet, Name = "插座", Quantity = "1", Code = "8.1", CatalogMatched = true }
                    };
                    var window = new FillReviewWindow(FillReviewData.Create(
                        new MachineRow { MachineId = "M1", CircuitName = "设备A", Cable = "C1" }, rows));
                    window.Show();
                    Pump(3);
                    var outlet = (FillReviewRow)window.ReviewGrid.Items[1];
                    outlet.Included = false;

                    Click(window.ConfirmButton);
                    Pump(2);
                    Assert.True(window.Confirmed);
                    Assert.DoesNotContain(window.Data.SelectedRows(), row =>
                        row.Category == TableFillCategory.Outlet || row.Name == "插座");
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
        public void SoftConduitDiameterEdit_RefreshesCatalogRow()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var rows = new List<TableFillRow>
                    {
                        new TableFillRow
                        {
                            Category = TableFillCategory.FlexibleConduit,
                            Name = "38mm软管", Description = "38mm旧模板",
                            Unit = "M", Quantity = "1.5", Code = "3.7"
                        }
                    };
                    var catalog = new List<ListItem>
                    {
                        new ListItem
                        {
                            Category = "软管", Code = "3.6", Name = "25mm软管",
                            Feature = "25mm新模板", Unit = "m", Spec = "25mm"
                        }
                    };
                    var window = new FillReviewWindow(FillReviewData.Create(
                        new MachineRow
                        {
                            MachineId = "M1",
                            CircuitName = "设备A",
                            Dia = "38"
                        }, rows), catalog);
                    window.Show();
                    Pump(3);

                    window.DiameterBox.Text = "25";
                    Pump(2);
                    FillReviewItem flexible = window.Data.FlexibleConduitItem();
                    Assert.Equal("25", window.Data.Machine.Dia);
                    Assert.Equal("3.6", flexible.Code);
                    Assert.Equal("25mm新模板", flexible.Description);
                    Assert.Equal("1.5", flexible.Quantity);
                    var row = (FillReviewRow)window.ReviewGrid.Items[0];
                    Assert.Equal("3.6", row.Code);
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
        public void DeviceReserveEdit_SyncsCableMetersAndCableRow()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var rows = new List<TableFillRow>
                    {
                        new TableFillRow { Category = TableFillCategory.Cable, Name = "电缆", Quantity = "100", Code = "1.1", CatalogMatched = true }
                    };
                    FillReviewData data = FillReviewData.Create(
                        new MachineRow { MachineId = "M1", CircuitName = "设备A", Cable = "C1" }, rows);
                    data.CableReserveEditable = true;
                    data.MeasuredCableMeters = "100";
                    data.PanelReserveMm = "1000";
                    data.OwnerReserveMm = "0";
                    data.DeviceReserveMm = "";
                    var window = new FillReviewWindow(data);
                    window.Show();
                    Pump(3);

                    // 设备内预留 500mm → 电缆长度 = 100 + (1000+500)/1000 = 101.5。
                    window.DeviceReserveBox.Text = "500";
                    Pump(2);
                    Assert.True(window.DeviceReserveBox.IsEnabled);
                    Assert.Equal("101.5", window.CableMetersBox.Text);
                    Assert.Equal("101.5", window.Data.CableMeters);
                    Assert.Equal("101.5", window.Data.CableItem().Quantity);
                    var cableRow = (FillReviewRow)window.ReviewGrid.Items[0];
                    Assert.Equal("101.5", cableRow.Quantity);
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
        public void CableQuantityEditInGrid_SyncsCableMetersField()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var rows = new List<TableFillRow>
                    {
                        new TableFillRow { Category = TableFillCategory.Cable, Name = "电缆", Quantity = "100", Code = "1.1", CatalogMatched = true }
                    };
                    var window = new FillReviewWindow(FillReviewData.Create(
                        new MachineRow { MachineId = "M1", CircuitName = "设备A", Cable = "C1" }, rows));
                    window.Show();
                    Pump(3);

                    ((FillReviewRow)window.ReviewGrid.Items[0]).Quantity = "88.5";
                    Pump(2);
                    Assert.Equal("88.5", window.CableMetersBox.Text);
                    Assert.Equal("88.5", window.Data.CableMeters);
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
        public void UnmatchedConduits_AreDisabledUntilCatalogReplacement()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var rows = new List<TableFillRow>
                    {
                        new TableFillRow
                        {
                            Category = TableFillCategory.RigidConduit, Name = "⌀32线管",
                            Description = "1.名称:⌀32线管", Quantity = "3",
                            CatalogMatched = false
                        },
                        new TableFillRow
                        {
                            Category = TableFillCategory.FlexibleConduit,
                            Name = "包塑金属软管",
                            Description = "1.名称:包塑金属软管\\P2.规格:32mm",
                            Quantity = "1.5", CatalogMatched = false
                        }
                    };
                    var window = new FillReviewWindow(
                        FillReviewData.Create(new MachineRow { Dia = "32" }, rows));
                    window.Show();
                    Pump(3);

                    // 存在未匹配行时自动切到清单页签，并显示警告横幅。
                    Assert.Equal(1, window.ReviewTabs.SelectedIndex);
                    Assert.True(window.CatalogWarningBar.IsOpen);
                    Assert.Contains("32", window.CatalogWarningBar.Message);
                    Assert.All(window.ReviewGrid.Items.Cast<FillReviewRow>(), row =>
                    {
                        Assert.False(row.Included);
                        Assert.False(row.CanToggleIncluded, "未匹配行勾选必须禁用");
                        Assert.True(row.IsUnmatched);
                    });

                    // “全部勾选”也不得让未匹配行进入 SelectedRows。
                    Click(window.FindButton("全部勾选"));
                    Pump(2);
                    Assert.Empty(window.Data.SelectedRows());

                    window.ReviewGrid.SelectedItem = window.ReviewGrid.Items[0];
                    Pump(1);
                    Assert.True(window.ReplaceItemButton.IsEnabled);
                    Assert.True(window.ResolveMissingButton.IsEnabled);
                    Assert.True(window.RemoveItemButton.IsEnabled);
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
        public void Confirm_WithUnmatchedItem_BlocksAndOpensCatalogPicker()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var rows = new List<TableFillRow>
                    {
                        new TableFillRow
                        {
                            Category = TableFillCategory.Cable, Name = "电缆",
                            Description = "1.名称:44*2+21", Quantity = "10",
                            CatalogMatched = false
                        }
                    };
                    var catalog = new List<ListItem>
                    {
                        new ListItem
                        {
                            Category = "电缆", Code = "1.1", Name = "多芯电缆",
                            Feature = "电缆模板", Unit = "m", Alias = "3*2.5"
                        }
                    };
                    var window = new FillReviewWindow(
                        FillReviewData.Create(new MachineRow
                        {
                            MachineId = "M1", CircuitName = "设备A", Cable = "44*2+21"
                        }, rows), catalog);
                    ManualListItemWindow opened = null;
                    // 接缝替身：记录嵌套选择器并模拟用户取消（不依赖 AutoCAD 宿主）。
                    window.ShowNestedPicker = picker =>
                    {
                        opened = (ManualListItemWindow)picker;
                        picker.Close();
                    };
                    window.Show();
                    Pump(3);

                    Click(window.ConfirmButton);
                    Pump(2);
                    Assert.False(window.Confirmed, "未匹配行未处理不得确认");
                    Assert.True(window.ErrorBar.IsOpen);
                    Assert.Contains("找不到对应型号", window.ErrorBar.Message);
                    // 自动打开的待选页锁定原类别且为替换模式。
                    Assert.NotNull(opened);
                    Assert.Equal("电缆", opened.SelectedCategory);
                    Assert.Equal("使用所选清单",
                        Convert.ToString(opened.ConfirmButton.Content));
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
        public void Confirm_WithInvalidReserveOrQuantity_BlocksConfirm()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var rows = new List<TableFillRow>
                    {
                        new TableFillRow { Category = TableFillCategory.Cable, Name = "电缆", Quantity = "5", Code = "1.1", CatalogMatched = true },
                        new TableFillRow { Category = TableFillCategory.Outlet, Name = "插座", Quantity = "1", Code = "8.1", CatalogMatched = true }
                    };
                    FillReviewData data = FillReviewData.Create(
                        new MachineRow { MachineId = "M1", CircuitName = "设备A", Cable = "C1" }, rows);
                    data.CableReserveEditable = true;
                    data.MeasuredCableMeters = "10";
                    data.PanelReserveMm = "0";
                    data.OwnerReserveMm = "0";
                    var window = new FillReviewWindow(data);
                    window.Show();
                    Pump(3);

                    window.DeviceReserveBox.Text = "200000";
                    Pump(2);
                    Click(window.ConfirmButton);
                    Pump(2);
                    Assert.False(window.Confirmed);
                    Assert.Contains("设备内预留", window.ErrorBar.Message);

                    window.DeviceReserveBox.Text = "100";
                    ((FillReviewRow)window.ReviewGrid.Items[1]).Quantity = "abc";
                    Click(window.ConfirmButton);
                    Pump(2);
                    Assert.False(window.Confirmed);
                    Assert.Contains("数量/长度", window.ErrorBar.Message);

                    ((FillReviewRow)window.ReviewGrid.Items[1]).Quantity = "2";
                    Click(window.ConfirmButton);
                    Pump(2);
                    Assert.True(window.Confirmed);
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
        public void ApplyCatalogReplacement_LocksOriginalCategory()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var rows = new List<TableFillRow>
                    {
                        new TableFillRow
                        {
                            Category = TableFillCategory.Cable, Name = "电缆",
                            Description = "1.名称:44*2+21", Quantity = "10",
                            CatalogMatched = false
                        },
                        new TableFillRow
                        {
                            Category = TableFillCategory.FlexibleConduit,
                            Name = "38mm软管", Description = "38mm旧模板",
                            Quantity = "1.5", Code = "3.7", CatalogMatched = true
                        }
                    };
                    var catalog = new List<ListItem>
                    {
                        new ListItem
                        {
                            Category = "电缆", Code = "1.1", Name = "多芯电缆",
                            Feature = "电缆模板", Unit = "m", Alias = "3*2.5"
                        },
                        new ListItem
                        {
                            Category = "桥架", Code = "2.1", Name = "桥架",
                            Feature = "桥架模板", Unit = "m", Alias = "200*100"
                        },
                        new ListItem
                        {
                            Category = "软管", Code = "3.8", Name = "20mm软管",
                            Feature = "20mm新模板", Unit = "m", Alias = "20mm"
                        }
                    };
                    var window = new FillReviewWindow(FillReviewData.Create(
                        new MachineRow { MachineId = "M1", CircuitName = "设备A", Cable = "44*2+21" },
                        rows), catalog);
                    window.Show();
                    Pump(3);
                    FillReviewItem item = window.Data.CableItem();

                    // 桥架项目不得写进电缆行。
                    window.ApplyCatalogReplacement(item, "电缆", catalog[1]);
                    Assert.True(window.ErrorBar.IsOpen);
                    Assert.Contains("只能选择“电缆”类别", window.ErrorBar.Message);
                    Assert.False(item.CatalogMatched);

                    // 同类别替换：行恢复可勾选并进入 SelectedRows。
                    window.ApplyCatalogReplacement(item, "电缆", catalog[0]);
                    Assert.True(item.CatalogMatched);
                    Assert.Equal("1.1", item.Code);
                    Assert.Equal("3*2.5", window.CableBox.Text);
                    FillReviewItem hose = window.Data.FlexibleConduitItem();
                    Assert.Equal("20", window.Data.Machine.Dia);
                    Assert.Equal("3.8", hose.Code);
                    Assert.Equal("3.8",
                        ((FillReviewRow)window.ReviewGrid.Items[1]).Code);
                    Assert.Contains("1.1",
                        window.Data.SelectedRows().Select(row => row.Code));
                    var row = (FillReviewRow)window.ReviewGrid.Items[0];
                    Assert.False(row.IsUnmatched);
                    Assert.True(row.CanToggleIncluded);
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
        public void CreateCatalogPicker_OpensRequestedCableCategory()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var rows = new List<TableFillRow>
                    {
                        new TableFillRow
                        {
                            Category = TableFillCategory.Cable, Name = "电缆",
                            Description = "1.名称:44*2+21", Quantity = "10",
                            CatalogMatched = false
                        }
                    };
                    var catalog = new List<ListItem>
                    {
                        new ListItem
                        {
                            Category = "电缆", Code = "1.1", Name = "多芯电缆",
                            Feature = "电缆模板", Unit = "m", Alias = "3*2.5"
                        },
                        new ListItem
                        {
                            Category = "桥架", Code = "2.1", Name = "桥架",
                            Feature = "桥架模板", Unit = "m", Alias = "200*100"
                        }
                    };
                    var window = new FillReviewWindow(FillReviewData.Create(
                        new MachineRow { MachineId = "M1", CircuitName = "设备A", Cable = "44*2+21" },
                        rows), catalog);
                    window.Show();
                    Pump(3);

                    ManualListItemWindow picker = window.CreateCatalogPicker("电缆");
                    try
                    {
                        picker.Show();
                        Pump(2);
                        Assert.Equal("电缆", picker.SelectedCategory);
                        Assert.DoesNotContain(picker.CategoryNames, name => name == "桥架");
                        Assert.Single(picker.CatalogGrid.Items);
                        var row = (ManualListItemWindow.CatalogRow)picker.CatalogGrid.Items[0];
                        Assert.Equal("1.1", row.Item.Code);
                    }
                    finally { picker.Close(); }
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
        public void AddCatalogRow_AppendsManualItemAndSelectsIt()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var rows = new List<TableFillRow>
                    {
                        new TableFillRow { Category = TableFillCategory.Cable, Name = "电缆", Quantity = "5", Code = "1.1", CatalogMatched = true }
                    };
                    var catalog = new List<ListItem>
                    {
                        new ListItem
                        {
                            Category = "插座", Code = "8.9", Name = "插座",
                            Feature = "普通五孔插座", Unit = "个", Alias = "五孔"
                        }
                    };
                    var window = new FillReviewWindow(FillReviewData.Create(
                        new MachineRow { MachineId = "M1", CircuitName = "设备A", Cable = "C1" }, rows),
                        catalog);
                    window.Show();
                    Pump(3);

                    window.AddCatalogRow(catalog[0], "3");
                    Pump(2);
                    FillReviewItem added = window.Data.Items.Last();
                    Assert.Equal(TableFillCategory.Manual, added.Category);
                    Assert.True(added.Included);
                    Assert.True(added.CatalogMatched);
                    Assert.Equal("3", added.Quantity);
                    Assert.Equal(2, window.ReviewGrid.Items.Count);
                    Assert.Equal(1, window.ReviewTabs.SelectedIndex);
                    Assert.Same(window.ReviewGrid.SelectedItem,
                        window.ReviewGrid.Items[1]);
                    Assert.Contains("将生成 2 / 2", window.CountText.Text);
                    Assert.Contains("8.9",
                        window.Data.SelectedRows().Select(row => row.Code));
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
        public void RestoreDefaults_RebuildsAutomaticItemsFromSnapshot()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var rows = new List<TableFillRow>
                    {
                        new TableFillRow { Category = TableFillCategory.Cable, Name = "电缆", Quantity = "5", Code = "1.1", CatalogMatched = true },
                        new TableFillRow { Category = TableFillCategory.Bridge, Name = "桥架", Quantity = "2", Code = "2.1", CatalogMatched = true }
                    };
                    var window = new FillReviewWindow(FillReviewData.Create(
                        new MachineRow { MachineId = "M1", CircuitName = "设备A", Cable = "C1" }, rows));
                    window.Show();
                    Pump(3);

                    ((FillReviewRow)window.ReviewGrid.Items[0]).Included = false;
                    var bridge = (FillReviewRow)window.ReviewGrid.Items[1];
                    window.ReviewGrid.SelectedItem = bridge;
                    Click(window.RemoveItemButton);
                    Pump(2);
                    Assert.Single(window.Data.Items);

                    window.ReviewTabs.SelectedIndex = 1;
                    Pump(1);
                    Click(window.FindButton("恢复默认"));
                    Pump(2);
                    Assert.Equal(2, window.Data.Items.Count);
                    Assert.All(window.Data.Items, item => Assert.True(item.Included));
                    Assert.Equal(2, window.ReviewGrid.Items.Count);
                    Assert.Contains("将生成 2 / 2", window.CountText.Text);
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
        [InlineData("", true)]
        [InlineData("0", true)]
        [InlineData("2500.5", true)]
        [InlineData("-1", false)]
        [InlineData("abc", false)]
        [InlineData("1e999", false)]
        public void ValidQuantity_FollowsFieldRange(string input, bool expected)
            => Assert.Equal(expected, FillReviewWindow.ValidQuantity(input));

        private static string ReviewTabHeader(FillReviewWindow window, int index)
            => ((System.Windows.Controls.TabItem)window.ReviewTabs.Items[index]).Header.ToString();

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

    /// <summary>测试辅助：按内容查找窗口按钮（Wpf.Ui Button）。</summary>
    internal static class FillReviewWindowTestExtensions
    {
        internal static Wpf.Ui.Controls.Button FindButton(
            this FillReviewWindow window, string content)
        {
            return FindAll<Wpf.Ui.Controls.Button>(window).FirstOrDefault(button =>
                Convert.ToString(button.Content) == content);
        }

        private static List<T> FindAll<T>(System.Windows.DependencyObject root)
            where T : System.Windows.DependencyObject
        {
            var result = new List<T>();
            if (root == null) return result;
            int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
                if (child is T match) result.Add(match);
                result.AddRange(FindAll<T>(child));
            }
            return result;
        }
    }
}
