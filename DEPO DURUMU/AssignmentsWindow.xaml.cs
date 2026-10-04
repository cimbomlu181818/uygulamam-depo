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
    /// Kayıtlar düzenlenebilir, iade alınabilir (toplu da) ve yazdırılabilir. İade alınınca iade tutanağı açılır.
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
                MessageBox.Show(this, "Zimmet listesi okunamadı:\n" + ex.Message, "Depo Takip",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                _all = new List<Assignment>();
            }

            _checkedIds.IntersectWith(_all.Select(a => a.Id));

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

            var count = GetChecked().Count;
            CheckedText.Text = count == 0 ? "" : count + " kayıt işaretli";
        }

        /// <summary>Listede görünen ve işaretli tüm kayıtlar (iade edilmiş olanlar dahil).</summary>
        private List<Assignment> GetChecked()
        {
            return _visible
                .Where(r => r.IsChecked)
                .Select(r => r.Item)
                .ToList();
        }

        private void SelectAllButton_Click(object sender, RoutedEventArgs e)
        {
            foreach (var row in _visible)
            {
                row.IsChecked = true;
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
                MessageBox.Show(this, "Önce listeden bir kayıt seç.", "Depo Takip",
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
            // Onay kutusuyla işaretlenmiş kayıtlar varsa iade edilmemiş olanların hepsi birlikte iade alınır.
            var checkedAll = GetChecked();
            if (checkedAll.Count > 0)
            {
                var active = checkedAll.Where(a => !a.IsReturned).ToList();

                if (active.Count == 0)
                {
                    MessageBox.Show(this, "İşaretli kayıtların hepsi zaten iade alınmış.", "Depo Takip",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                ReturnWithReceipt(active);
                return;
            }

            var selected = GetSelected();
            if (selected == null)
            {
                return;
            }

            if (selected.IsReturned)
            {
                MessageBox.Show(this, "Bu zimmet zaten iade alınmış.", "Depo Takip",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // İade BURADA yapılmaz: iade tutanağı önizlemesi açılır, iade ancak orada Kaydet'e basılınca alınır.
            ReturnWithReceipt(new List<Assignment> { selected });
        }

        /// <summary>
        /// İade alınacak zimmetler için iade tutanağını KAYDETMEDEN önizler; iade ancak önizlemede
        /// "Kaydet"e basılınca yapılır, "Vazgeç"te hiçbir şey değişmez. Tutanak tek kişinin adına düzenlendiği için
        /// kayıtlar farklı kişilere aitse her kişi için ayrı önizleme açılır (her biri ayrı Kaydet/Vazgeç).
        /// </summary>
        private void ReturnWithReceipt(List<Assignment> list)
        {
            var stamp = AssignmentRepository.NowRaw();
            var returnedIds = new List<int>();

            var groups = list
                .GroupBy(a => (a.PersonName ?? "").Trim(), StringComparer.Create(Turkish, true))
                .ToList();

            foreach (var group in groups)
            {
                var members = group.OrderBy(a => a.Id).ToList();
                var drafts = members.Select(a => MakeReturnDraft(a, stamp)).ToList();

                AssignmentReceiptPrinter.ShowSavePreview(
                    this, drafts, true, owner => SaveReturns(owner, members, drafts, stamp, returnedIds));
            }

            if (returnedIds.Count > 0)
            {
                foreach (var id in returnedIds)
                {
                    _checkedIds.Remove(id);
                }

                LoadAssignments();
            }
        }

        /// <summary>İade tutanağında gösterilecek, henüz kaydedilmemiş iade kaydı (defterdeki kayda dokunmaz).</summary>
        private static Assignment MakeReturnDraft(Assignment source, string stamp)
        {
            return new Assignment
            {
                Id = source.Id,
                ProductId = source.ProductId,
                TypeName = source.TypeName,
                SystemName = source.SystemName,
                SerialNo = source.SerialNo,
                Quantity = source.Quantity,
                PersonName = source.PersonName,
                RegistryNo = source.RegistryNo,
                Department = source.Department,
                AssignedAt = source.AssignedAt,
                AssignedNote = source.AssignedNote,
                IsReturned = true,
                ReturnedAt = stamp,
                ReturnedNote = null,
                IsLinked = source.IsLinked
            };
        }

        /// <summary>
        /// Önizlemede "Kaydet"e basılınca çalışır: zimmetleri iade alınmış yapar.
        /// True dönerse önizleme kapanır. Hiçbiri alınamadıysa false döner ve önizleme açık kalır.
        /// </summary>
        private bool SaveReturns(Window owner, List<Assignment> members, List<Assignment> drafts,
            string stamp, List<int> returnedIds)
        {
            var done = new List<Assignment>();
            var failed = new List<string>();

            foreach (var member in members)
            {
                try
                {
                    AssignmentRepository.Return(member.Id, null, stamp);
                    done.Add(member);
                    returnedIds.Add(member.Id);
                }
                catch (Exception ex)
                {
                    failed.Add(member.ProductText + " | " + member.PersonName + ": " + ex.Message);
                }
            }

            if (failed.Count > 0)
            {
                MessageBox.Show(owner,
                    done.Count + " zimmet iade alındı, " + failed.Count + " kayıt alınamadı:\n\n" +
                    string.Join("\n", failed.Take(8)),
                    "Depo Takip", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            if (done.Count == 0)
            {
                return false;
            }

            // Yazdırma sadece gerçekten iade alınanları bassın diye alınamayanlar tutanak listesinden çıkarılır.
            if (failed.Count > 0)
            {
                var doneIds = new HashSet<int>(done.Select(d => d.Id));
                drafts.RemoveAll(d => !doneIds.Contains(d.Id));
            }

            return true;
        }

        /// <summary>
        /// Yazdır: İşaretli kayıt varsa hepsi için tek tutanak, yoksa seçili satır için tutanak açılır.
        /// Toplu tutanak sadece aynı kişiye ait kayıtlar için çıkar; farklı kişiler işaretliyse uyarılır ve yazdırılmaz.
        /// Kayıtlar iade edilmişse iade tutanağı, zimmetteyse zimmet tutanağı açılır.
        /// </summary>
        private void PrintButton_Click(object sender, RoutedEventArgs e)
        {
            var list = GetChecked();

            if (list.Count == 0)
            {
                var selected = GetSelected();
                if (selected == null)
                {
                    return;
                }

                list = new List<Assignment> { selected };
            }

            if (list.Count > 1)
            {
                var people = list
                    .Select(a => (a.PersonName ?? "").Trim())
                    .Distinct(StringComparer.Create(Turkish, true))
                    .ToList();

                if (people.Count > 1)
                {
                    MessageBox.Show(this,
                        "İşaretli kayıtlar farklı kişilere ait:\n\n" +
                        string.Join("\n", people.Take(8).Select(n => "• " + n)) +
                        (people.Count > 8 ? "\n... ve " + (people.Count - 8) + " kişi daha" : "") +
                        "\n\nToplu tutanak sadece aynı kişiye ait kayıtlar için çıkarılır. " +
                        "Lütfen aynı kişiye ait kayıtları işaretleyip tekrar dene.",
                        "Depo Takip", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (list.Select(a => a.IsReturned).Distinct().Count() > 1)
                {
                    MessageBox.Show(this,
                        "İşaretli kayıtların bir kısmı zimmette, bir kısmı iade edilmiş.\n\n" +
                        "Zimmet tutanağı ile iade tutanağı birlikte çıkarılamaz. " +
                        "Sadece zimmetteki ya da sadece iade edilmiş kayıtları işaretleyip tekrar dene.",
                        "Depo Takip", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            list = list.OrderBy(a => a.Id).ToList();

            // Önce tutanak ekranda açılır; yazdırmak için pencerede "Yazdır"a basılır.
            if (list[0].IsReturned)
            {
                AssignmentReceiptPrinter.ShowReturnPreview(this, list);
            }
            else
            {
                AssignmentReceiptPrinter.ShowPreview(this, list);
            }
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

        public bool IsChecked
        {
            get { return _checkedIds.Contains(Item.Id); }
            set
            {
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