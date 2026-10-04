using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using DEPO_DURUMU.Data;

namespace DEPO_DURUMU
{
    /// <summary>
    /// Notlar sayfasındaki sütun filtresi: A→Z / Z→A sıralama, aranabilir değer listesi
    /// ve her değer için onay kutusu (Excel'deki otomatik filtre gibi).
    /// </summary>
    public partial class NoteFilterWindow : Window
    {
        public enum FilterOutcome
        {
            None,
            Apply,
            SortAscending,
            SortDescending,
            Clear
        }

        internal sealed class FilterItem : INotifyPropertyChanged
        {
            private bool _isChecked;

            public FilterItem(string value, bool isChecked)
            {
                Value = value;
                _isChecked = isChecked;
            }

            public string Value { get; private set; }

            public string Text
            {
                get { return Value.Length == 0 ? "(Boş)" : Value; }
            }

            public bool IsChecked
            {
                get { return _isChecked; }
                set
                {
                    if (_isChecked == value)
                    {
                        return;
                    }

                    _isChecked = value;
                    var handler = PropertyChanged;
                    if (handler != null)
                    {
                        handler(this, new PropertyChangedEventArgs("IsChecked"));
                    }
                }
            }

            public event PropertyChangedEventHandler PropertyChanged;
        }

        private static readonly CultureInfo Turkish = new CultureInfo("tr-TR");

        private readonly List<FilterItem> _items = new List<FilterItem>();
        private List<FilterItem> _visible = new List<FilterItem>();
        private bool _updating;

        public FilterOutcome Outcome { get; private set; }

        /// <summary>Tamam'a basılınca: işaretli değerler.</summary>
        public HashSet<string> Allowed { get; private set; }

        /// <summary>
        /// columnName: sütun harfi (A, B...). values: sütundaki farklı değerler (boş değer "" olarak,
        /// sıralı). currentAllowed: şu an uygulanan filtre (yoksa null).
        /// </summary>
        public NoteFilterWindow(string columnName, IList<string> values, HashSet<string> currentAllowed)
        {
            InitializeComponent();

            Title = columnName + " sütunu";
            Outcome = FilterOutcome.None;

            foreach (var value in values)
            {
                var item = new FilterItem(value, currentAllowed == null || currentAllowed.Contains(value));
                item.PropertyChanged += Item_PropertyChanged;
                _items.Add(item);
            }

            ClearButton.IsEnabled = currentAllowed != null;

            RefreshList();
            UpdateStates();

            Loaded += delegate { SearchBox.Focus(); };
        }

        private void RefreshList()
        {
            var text = SearchBox.Text.Trim();

            _visible = _items
                .Where(i => text.Length == 0 ||
                            Turkish.CompareInfo.IndexOf(i.Text, text, CompareOptions.IgnoreCase) >= 0)
                .ToList();

            ValuesList.ItemsSource = _visible;
            UpdateStates();
        }

        private void Item_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (!_updating)
            {
                UpdateStates();
            }
        }

        /// <summary>"Tümünü Seç" kutusunun durumunu ve Tamam düğmesinin açık/kapalı olmasını günceller.</summary>
        private void UpdateStates()
        {
            if (_visible.Count == 0)
            {
                SelectAllBox.IsChecked = false;
            }
            else if (_visible.All(i => i.IsChecked))
            {
                SelectAllBox.IsChecked = true;
            }
            else if (_visible.Any(i => i.IsChecked))
            {
                SelectAllBox.IsChecked = null;
            }
            else
            {
                SelectAllBox.IsChecked = false;
            }

            OkButton.IsEnabled = _items.Any(i => i.IsChecked);
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            SearchPlaceholder.Visibility = string.IsNullOrEmpty(SearchBox.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;

            RefreshList();
        }

        private void SelectAllBox_Click(object sender, RoutedEventArgs e)
        {
            // Görünen değerlerin hepsi işaretliyse hepsini kaldır, değilse hepsini işaretle.
            var target = !(_visible.Count > 0 && _visible.All(i => i.IsChecked));

            _updating = true;
            foreach (var item in _visible)
            {
                item.IsChecked = target;
            }
            _updating = false;

            UpdateStates();
        }

        private void SortAscending_Click(object sender, RoutedEventArgs e)
        {
            Outcome = FilterOutcome.SortAscending;
            DialogResult = true;
        }

        private void SortDescending_Click(object sender, RoutedEventArgs e)
        {
            Outcome = FilterOutcome.SortDescending;
            DialogResult = true;
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            Outcome = FilterOutcome.Clear;
            DialogResult = true;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            var allowed = new HashSet<string>(NoteFormula.TextComparer);
            foreach (var item in _items)
            {
                if (item.IsChecked)
                {
                    allowed.Add(item.Value);
                }
            }

            Allowed = allowed;

            // Hepsi işaretliyse filtre yok demektir.
            Outcome = _items.All(i => i.IsChecked) ? FilterOutcome.Clear : FilterOutcome.Apply;
            DialogResult = true;
        }
    }
}
