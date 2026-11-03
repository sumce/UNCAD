using System.Runtime.CompilerServices;
using UNCAD.Core.Fill;

namespace UNCAD.UI
{
    /// <summary>
    /// 清单评审表格的行模型（D-038）：直接包装 <see cref="FillReviewItem"/>，
    /// 单元格编辑（勾选/数量）通过 TwoWay 绑定即时写回 Item，替代旧
    /// DataGridView 的 ReadGridRow 回读；其余列只读展示。未匹配行
    /// （CatalogMatched=false）的勾选被禁用且强制为 false。
    /// </summary>
    public sealed class FillReviewRow : System.ComponentModel.INotifyPropertyChanged
    {
        /// <summary>列值变化通知（列名：Included/Quantity/…），窗口据此联动统计。</summary>
        internal event System.Action<FillReviewRow, string> ColumnChanged;

        public FillReviewRow(FillReviewItem item)
        {
            Item = item;
        }

        public FillReviewItem Item { get; }

        public string CategoryLabel => FillReviewData.CategoryName(Item.Category);

        /// <summary>未匹配固定清单的行：底色警告、勾选禁用、不得进入 SelectedRows。</summary>
        public bool IsUnmatched => !Item.CatalogMatched;

        /// <summary>未匹配行不可勾选（旧版单元格 ReadOnly 的等价物）。</summary>
        public bool CanToggleIncluded => Item.CatalogMatched;

        public bool Included
        {
            get { return Item.Included; }
            set
            {
                if (Item.Included == value) return;
                Item.Included = value;
                OnPropertyChanged();
                ColumnChanged?.Invoke(this, "Included");
            }
        }

        public string Name
        {
            get { return Item.Name; }
            set
            {
                if (ReferenceEquals(Item.Name, value) || Item.Name == value) return;
                Item.Name = (value ?? "").Trim();
                OnPropertyChanged();
            }
        }

        public string Description
        {
            get { return Item.Description; }
            set
            {
                if (ReferenceEquals(Item.Description, value) || Item.Description == value) return;
                Item.Description = (value ?? "").Trim();
                OnPropertyChanged();
            }
        }

        public string Unit
        {
            get { return Item.Unit; }
            set
            {
                if (ReferenceEquals(Item.Unit, value) || Item.Unit == value) return;
                Item.Unit = (value ?? "").Trim();
                OnPropertyChanged();
            }
        }

        /// <summary>数量 / 长度：唯一可编辑文本列，改动即时写回并通知窗口联动。</summary>
        public string Quantity
        {
            get { return Item.Quantity; }
            set
            {
                if (ReferenceEquals(Item.Quantity, value) || Item.Quantity == value) return;
                Item.Quantity = (value ?? "").Trim();
                OnPropertyChanged();
                ColumnChanged?.Invoke(this, "Quantity");
            }
        }

        public string Code
        {
            get { return Item.Code; }
            set
            {
                if (ReferenceEquals(Item.Code, value) || Item.Code == value) return;
                Item.Code = (value ?? "").Trim();
                OnPropertyChanged();
            }
        }

        /// <summary>替换为固定清单 / 恢复默认后，从 Item 重新拉取全部显示值。</summary>
        internal void Refresh()
        {
            OnPropertyChanged(nameof(Included));
            OnPropertyChanged(nameof(CategoryLabel));
            OnPropertyChanged(nameof(IsUnmatched));
            OnPropertyChanged(nameof(CanToggleIncluded));
            OnPropertyChanged(nameof(Name));
            OnPropertyChanged(nameof(Description));
            OnPropertyChanged(nameof(Unit));
            OnPropertyChanged(nameof(Quantity));
            OnPropertyChanged(nameof(Code));
        }

        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this,
                new System.ComponentModel.PropertyChangedEventArgs(name));
    }
}
