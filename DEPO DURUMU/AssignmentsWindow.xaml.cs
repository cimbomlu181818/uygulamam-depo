using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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

        /// <summary>Şu an listede görünen satırlar (onay kutulu sarmalayıcılar).</summary>
        private List<AssignmentRow> _visible = new List<AssignmentRow>();

        /// <summary>İşaretli (onay kutusu seçili) zimmetlerin numaraları. Arama yapılınca işaretler kaybolmasın diye pencerede tutulur.</summary>
        private readonly HashSet<int> _checkedIds = new HashSet<int>();

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

            _checkedIds.IntersectWith(_all.Where(a => !a.IsReturned).Select(a => a.Id));

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

            _visible = list.Select(a => new AssignmentRow(a, _checkedIds, UpdateCheckedInfo)).ToList();
            AssignmentsGrid.ItemsSource = _visible;

            CountText.Text = text.Length == 0
                ? _all.Count + " kayıt"
                : list.Count + " / " + _all.Count + " kayıt";

            UpdateCheckedInfo();
        }

        /// <summary>"N seçili" yazısını günceller (sadece listede görünen işaretliler sayılır).</summary>
        private void UpdateCheckedInfo()
        {
            if (CheckedText == null)
            {
                return;
            }

            var count = GetCheckedActive().Count;
            CheckedText.Text = count == 0 ? "" : count + " kayıt işaretli";
        }

        /// <summary>Listede görünen, işaretli ve henüz iade edilmemiş zimmetler.</summary>
        private List<Assignment> GetCheckedActive()
        {
            return _visible
                .Where(r => r.IsChecked && !r.Item.IsReturned)
                .Select(r => r.Item)
                .ToList();
        }

        private void SelectAllButton_Click(object sender, RoutedEventArgs e)
        {
            foreach (var row in _visible)
            {
                if (row.CanCheck)
                {
                    row.IsChecked = true;
                }
            }

            UpdateCheckedInfo();
        }

        private void ClearSelectionButton_Click(object sender, RoutedEventArgs e)
        {
            foreach (var row in _visible)
            {
                row.IsChecked = false;
            }

            _checkedIds.Clear();
            UpdateCheckedInfo();
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
            var row = AssignmentsGrid.SelectedItem as AssignmentRow;
            if (row == null)
            {
                MessageBox.Show(this, "Önce listeden bir kayıt seç.", "Depo Durumu",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return null;
            }

            return row.Item;
        }

        private void AssignmentsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (IsInsideCheckBox(e.OriginalSource as DependencyObject))
            {
                return;
            }

            if (AssignmentsGrid.SelectedItem is AssignmentRow)
            {
                EditButton_Click(sender, e);
            }
        }

        private static bool IsInsideCheckBox(DependencyObject element)
        {
            while (element != null)
            {
                if (element is CheckBox)
                {
                    return true;
                }

                if (element is DataGridRow)
                {
                    return false;
                }

                element = (element is Visual || element is System.Windows.Media.Media3D.Visual3D)
                    ? VisualTreeHelper.GetParent(element)
                    : LogicalTreeHelper.GetParent(element);
            }

            return false;
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
            // Onay kutusuyla işaretlenmiş kayıtlar varsa hepsi birlikte iade alınır.
            var checkedList = GetCheckedActive();
            if (checkedList.Count > 0)
            {
                ReturnMany(checkedList);
                return;
            }

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

        /// <summary>İşaretlenen tüm zimmetleri tek onayla, aynı anda iade alır.</summary>
        private void ReturnMany(List<Assignment> list)
        {
            var lines = list.Take(12).Select(a =>
                "• " + a.PersonName + " — " + a.ProductText + (a.Quantity > 1 ? " (miktar: " + a.Quantity + ")" : ""));
            var preview = string.Join("\n", lines);

            if (list.Count > 12)
            {
                preview += "\n... ve " + (list.Count - 12) + " kayıt daha";
            }

            var answer = MessageBox.Show(this,
                list.Count + " zimmet iade alınacak:\n\n" + preview + "\n\nOnaylıyor musun?",
                "Depo Durumu", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (answer != MessageBoxResult.Yes)
            {
                return;
            }

            var done = 0;
            var failed = new List<string>();

            foreach (var assignment in list)
            {
                try
                {
                    AssignmentRepository.Return(assignment.Id, null);
                    done++;
                }
                catch (Exception ex)
                {
                    failed.Add(assignment.ProductText + " | " + assignment.PersonName + ": " + ex.Message);
                }
            }

            _checkedIds.Clear();
            LoadAssignments();

            if (failed.Count > 0)
            {
                MessageBox.Show(this,
                    done + " zimmet iade alındı, " + failed.Count + " kayıt alınamadı:\n\n" +
                    string.Join("\n", failed.Take(8)),
                    "Depo Durumu", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void PrintButton_Click(object sender, RoutedEventArgs e)
        {
            var selected = GetSelected();
            if (selected == null)
            {
                return;
            }

            // Önce tutanak ekranda açılır; yazdırmak için pencerede "Yazdır"a basılır.
            AssignmentReceiptPrinter.ShowPreview(this, new List<Assignment> { selected });
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

    /// <summary>
    /// Zimmet listesindeki bir satır: kaydın kendisi (Item) ve onay kutusunun durumu (IsChecked).
    /// Onay kutusu bilgisi kayıtta değil pencerede tutulur; kayıt sınıfına dokunulmaz.
    /// </summary>
    public class AssignmentRow : INotifyPropertyChanged
    {
        private readonly HashSet<int> _checkedIds;
        private readonly Action _changed;

        public AssignmentRow(Assignment item, HashSet<int> checkedIds, Action changed)
        {
            Item = item;
            _checkedIds = checkedIds;
            _changed = changed;
        }

        public Assignment Item { get; private set; }

        /// <summary>İade edilmiş kayıtlar işaretlenemez.</summary>
        public bool CanCheck
        {
            get { return !Item.IsReturned; }
        }

        public bool IsChecked
        {
            get { return CanCheck && _checkedIds.Contains(Item.Id); }
            set
            {
                if (!CanCheck)
                {
                    return;
                }

                var changed = value ? _checkedIds.Add(Item.Id) : _checkedIds.Remove(Item.Id);
                if (!changed)
                {
                    return;
                }

                OnPropertyChanged("IsChecked");

                if (_changed != null)
                {
                    _changed();
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged(string name)
        {
            var handler = PropertyChanged;
            if (handler != null)
            {
                handler(this, new PropertyChangedEventArgs(name));
            }
        }
    }
}