using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DEPO_DURUMU.Data;

namespace DEPO_DURUMU
{
    /// <summary>
    /// Tutanaklar defteri: yazdırılmış teslim-tesellüm tutanaklarının kaydı. Bilgi amaçlıdır;
    /// kayıtlar açılıp düzeltilebilir, tekrar yazdırılabilir ve silinebilir.
    /// </summary>
    public partial class HandoversWindow : Window
    {
        private static readonly CultureInfo Turkish = new CultureInfo("tr-TR");

        private List<Handover> _all = new List<Handover>();

        public HandoversWindow()
        {
            InitializeComponent();
            LoadHandovers();
            SearchBox.Focus();
        }

        private void LoadHandovers()
        {
            try
            {
                _all = HandoverRepository.GetAll();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Tutanak listesi okunamadı:\n" + ex.Message, "Depo Durumu",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                _all = new List<Handover>();
            }

            ApplyFilter();
        }

        private void ApplyFilter()
        {
            if (HandoversGrid == null)
            {
                return;
            }

            var text = SearchBox.Text.Trim();
            var list = text.Length == 0
                ? _all
                : _all.Where(h => Turkish.CompareInfo.IndexOf(h.SearchText, text, CompareOptions.IgnoreCase) >= 0).ToList();

            HandoversGrid.ItemsSource = list;

            CountText.Text = text.Length == 0
                ? _all.Count + " tutanak"
                : list.Count + " / " + _all.Count + " tutanak";
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            SearchPlaceholder.Visibility = string.IsNullOrEmpty(SearchBox.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;

            ApplyFilter();
        }

        private Handover GetSelected()
        {
            var selected = HandoversGrid.SelectedItem as Handover;
            if (selected == null)
            {
                MessageBox.Show(this, "Önce listeden bir tutanak seç.", "Depo Durumu",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }

            return selected;
        }

        private void NewButton_Click(object sender, RoutedEventArgs e)
        {
            var window = new HandoverWindow { Owner = this };
            window.ShowDialog();

            if (window.Changed)
            {
                LoadHandovers();
            }
        }

        private void HandoversGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (HandoversGrid.SelectedItem is Handover)
            {
                OpenButton_Click(sender, e);
            }
        }

        private void OpenButton_Click(object sender, RoutedEventArgs e)
        {
            var selected = GetSelected();
            if (selected == null)
            {
                return;
            }

            // Liste bu arada değişmiş olabilir; güncel hâlini defterden yeniden oku.
            var current = HandoverRepository.GetById(selected.Id);
            if (current == null)
            {
                MessageBox.Show(this, "Bu tutanak artık defterde yok.", "Depo Durumu",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                LoadHandovers();
                return;
            }

            var window = new HandoverWindow(current) { Owner = this };
            window.ShowDialog();

            if (window.Changed)
            {
                LoadHandovers();
            }
        }

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            var selected = GetSelected();
            if (selected == null)
            {
                return;
            }

            var answer = MessageBox.Show(this,
                "No: " + selected.Id + " | " + selected.FromUnit + " -> " + selected.ToUnit + " | " +
                selected.HandoverDate + "\n" + selected.ItemCount + " kalem: " + selected.ItemsSummary +
                "\n\nBu tutanak defterden KALICI olarak silinecek (stok ve zimmet etkilenmez). Onaylıyor musun?",
                "Depo Durumu", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (answer != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                HandoverRepository.Delete(selected.Id);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Silinemedi:\n" + ex.Message, "Depo Durumu",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            LoadHandovers();
        }
    }
}
