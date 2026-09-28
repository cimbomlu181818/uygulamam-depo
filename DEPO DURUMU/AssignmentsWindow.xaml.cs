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
    /// Zimmetler defteri: şu an kimde ne olduğunu ve (istenirse) iade edilmiş geçmiş kayıtları gösterir.
    /// Kayıtlar düzenlenebilir, iade alınabilir, yazdırılabilir ve silinebilir.
    /// </summary>
    public partial class AssignmentsWindow : Window
    {
        private static readonly CultureInfo Turkish = new CultureInfo("tr-TR");

        private List<Assignment> _all = new List<Assignment>();

        public AssignmentsWindow()
        {
            InitializeComponent();
            LoadAssignments();
            SearchBox.Focus();
        }

        private void LoadAssignments()
        {
            try
            {
                _all = AssignmentRepository.GetAll(ShowReturnedCheck.IsChecked != true);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Zimmet listesi okunamadı:\n" + ex.Message, "Depo Durumu",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                _all = new List<Assignment>();
            }

            ApplyFilter();
        }

        private void ApplyFilter()
        {
            if (AssignmentsGrid == null)
            {
                return;
            }

            var text = SearchBox.Text.Trim();
            IEnumerable<Assignment> shown = _all;

            if (text.Length > 0)
            {
                shown = _all.Where(a => Matches(a, text)).ToList();
            }

            var list = shown.ToList();
            AssignmentsGrid.ItemsSource = list;

            CountText.Text = text.Length == 0
                ? _all.Count + " kayıt"
                : list.Count + " / " + _all.Count + " kayıt";
        }

        private static bool Matches(Assignment a, string text)
        {
            return Contains(a.TypeName, text) || Contains(a.SystemName, text) || Contains(a.SerialNo, text) ||
                   Contains(a.PersonName, text) || Contains(a.RegistryNo, text) || Contains(a.Department, text) ||
                   Contains(a.AssignedNote, text) || Contains(a.ReturnedNote, text) ||
                   Contains(a.AssignedAtText, text) || Contains(a.ReturnedAtText, text);
        }

        private static bool Contains(string source, string text)
        {
            return Turkish.CompareInfo.IndexOf(source ?? "", text, CompareOptions.IgnoreCase) >= 0;
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            SearchPlaceholder.Visibility = string.IsNullOrEmpty(SearchBox.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;

            ApplyFilter();
        }

        private void ShowReturnedCheck_Changed(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded)
            {
                return;
            }

            LoadAssignments();
        }

        private Assignment GetSelected()
        {
            var selected = AssignmentsGrid.SelectedItem as Assignment;
            if (selected == null)
            {
                MessageBox.Show(this, "Önce listeden bir kayıt seç.", "Depo Durumu",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }

            return selected;
        }

        private void AssignmentsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (AssignmentsGrid.SelectedItem is Assignment)
            {
                EditButton_Click(sender, e);
            }
        }

        private void EditButton_Click(object sender, RoutedEventArgs e)
        {
            var selected = GetSelected();
            if (selected == null)
            {
                return;
            }

            var window = new AssignmentEditWindow(selected.Id) { Owner = this };
            if (window.ShowDialog() == true)
            {
                LoadAssignments();
            }
        }

        private void ReturnButton_Click(object sender, RoutedEventArgs e)
        {
            var selected = GetSelected();
            if (selected == null)
            {
                return;
            }

            if (selected.IsReturned)
            {
                MessageBox.Show(this, "Bu zimmet zaten iade alınmış.", "Depo Durumu",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var answer = MessageBox.Show(this,
                selected.PersonName + " üzerindeki bu zimmet iade alınacak:\n\n" + selected.ProductText +
                (selected.Quantity > 1 ? "\nMiktar: " + selected.Quantity : "") + "\n\nOnaylıyor musun?",
                "Depo Durumu", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (answer != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                AssignmentRepository.Return(selected.Id, null);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "İade alınamadı:\n" + ex.Message, "Depo Durumu",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            LoadAssignments();
        }

        private void PrintButton_Click(object sender, RoutedEventArgs e)
        {
            var selected = GetSelected();
            if (selected == null)
            {
                return;
            }

            AssignmentReceiptPrinter.Print(this, selected);
        }

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            var selected = GetSelected();
            if (selected == null)
            {
                return;
            }

            var effect = selected.IsReturned
                ? "Bu kayıt iade edilmiş; silinirse sadece geçmişten kaybolur."
                : "Bu zimmet hâlâ aktif; silinirse ürün yeniden zimmetlenebilir hâle gelir " +
                  "(iade alınmış sayılmaz, sadece kayıt silinir).";

            var answer = MessageBox.Show(this,
                selected.ProductText + "\n" + selected.PersonName + " | Miktar: " + selected.Quantity +
                " | " + selected.AssignedAtText + "\n\n" + effect +
                "\n\nBu zimmet kaydı KALICI olarak silinecek. Onaylıyor musun?",
                "Depo Durumu", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (answer != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                AssignmentRepository.Delete(selected.Id);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Silinemedi:\n" + ex.Message, "Depo Durumu",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            LoadAssignments();
        }
    }
}
