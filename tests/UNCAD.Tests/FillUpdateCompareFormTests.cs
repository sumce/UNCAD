using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using UNCAD.Core.Fill;
using UNCAD.UI;
using Xunit;

namespace UNCAD.Tests
{
    public sealed class FillUpdateCompareFormTests
    {
        [Fact]
        public void MultipleFrameLayout_DoesNotOverlapFrameListAndTables()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var items = new List<FillUpdateCompareItem>
                    {
                        Item("M1"), Item("M2")
                    };
                    using (var form = new FillUpdateCompareForm(items))
                    {
                        form.Show();
                        Application.DoEvents();

                        ListBox frameList = Find<ListBox>(form);
                        List<ListView> tables = FindAll<ListView>(form);
                        Assert.NotNull(frameList);
                        Assert.Equal(2, tables.Count);

                        Rectangle frameBounds = ScreenBounds(frameList);
                        Rectangle previousBounds = ScreenBounds(tables[0]);
                        Rectangle plannedBounds = ScreenBounds(tables[1]);
                        Assert.True(frameBounds.Right <= previousBounds.Left,
                            "Frame selector overlaps the previous-items table.");
                        Assert.True(plannedBounds.Left > previousBounds.Left);
                        Assert.True(previousBounds.Width > 0 && plannedBounds.Width > 0);
                    }
                }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure != null) throw failure;
        }

        private static FillUpdateCompareItem Item(string machineId)
        {
            return new FillUpdateCompareItem
            {
                MachineId = machineId,
                DeviceName = "Device",
                HasHistory = true,
                PreviousRows = new List<FillRowDiff>(),
                PlannedRows = new List<FillRowDiff>()
            };
        }

        private static Rectangle ScreenBounds(Control control)
            => control.RectangleToScreen(control.ClientRectangle);

        private static T Find<T>(Control root) where T : Control
            => FindAll<T>(root).FirstOrDefault();

        private static List<T> FindAll<T>(Control root) where T : Control
        {
            var result = new List<T>();
            foreach (Control child in root.Controls)
            {
                if (child is T match) result.Add(match);
                result.AddRange(FindAll<T>(child));
            }
            return result;
        }
    }
}
