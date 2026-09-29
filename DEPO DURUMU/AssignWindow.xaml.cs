using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DEPO_DURUMU.Data;

namespace DEPO_DURUMU
{
    /// <summary>
    /// Bir ürünü (ya da adetli üründen bir miktarını) bir kişiye zimmetlemek için açılan pencere.
    /// Zimmetle'ye basınca doğrudan Zimmetler defterine yazar; başarılıysa DialogResult true olur.
    /// </summary>
    public partial class AssignWindow : Window
    {
        private readonly int _productId;
        private readonly int _available;
        private readonly List<int> _bulkIds;
        private List<KnownPerson> _people = new List<KnownPerson>();

        public AssignWindow(int productId, string productDescription, int availableQuantity)
        {
            InitializeComponent();

            _productId = productId;
            _available = availableQuantity;

            ProductText.Text = productDescription;
            AvailableText.Text = "Zimmetlenebilir miktar: " + availableQuantity;

            QuantityBox.Text = "1";

            if (availableQuantity <= 1)
            {
                QuantityBox.IsEnabled = false;
                QuantityLabel.Foreground = Brushes.Gray;
            }

            try
            {
                _people = AssignmentRepository.GetKnownPeople();
                PersonBox.ItemsSource = _people;
                DepartmentBox.ItemsSource = AssignmentRepository.GetKnownDepartments();
            }
            catch (Exception)
            {
                // Öneri listesi okunamazsa kutular boş açılır; zimmetleme yine de yapılabilir.
            }

            Loaded += (s, e) => PersonBox.Focus();
        }

        /// <summary>
        /// Birden fazla ürünü aynı kişiye toplu zimmetlemek için açılır: her ürünün zimmetlenebilir
        /// miktarının tamamı zimmetlenir ve tek tutanak yazdırılır.
        /// </summary>
        public AssignWindow(List<int> productIds, string summaryText)
        {
            InitializeComponent();

            _bulkIds = productIds;

            ProductText.Text = summaryText;
            AvailableText.Text = "Her ürünün zimmetlenebilir miktarının tamamı zimmetlenir.";

            QuantityBox.Text = "Tamamı";
            QuantityBox.IsEnabled = false;
            QuantityLabel.Foreground = Brushes.Gray;

            try
            {
                _people = AssignmentRepository.GetKnownPeople();
                PersonBox.ItemsSource = _people;
                DepartmentBox.ItemsSource = AssignmentRepository.GetKnownDepartments();
            }
            catch (Exception)
            {
                // Öneri listesi okunamazsa kutular boş açılır; zimmetleme yine de yapılabilir.
            }

            Loaded += (s, e) => PersonBox.Focus();
        }

        /// <summary>Listeden daha önce zimmet yapılmış bir kişi seçilince sicil ve birimi otomatik gelir.</summary>
        private void PersonBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var person = PersonBox.SelectedItem as KnownPerson;
            if (person == null)
            {
                return;
            }

            RegistryNoBox.Text = person.RegistryNo ?? "";
            DepartmentBox.Text = person.Department ?? "";
        }

        private void AssignButton_Click(object sender, RoutedEventArgs e)
        {
            string personName = (PersonBox.Text ?? "").Trim();

            if (personName.Length == 0)
            {
                ShowWarning("Kişi adı boş bırakılamaz.");
                PersonBox.Focus();
                return;
            }

            int quantity = 0;

            if (_bulkIds == null)
            {
                if (!int.TryParse(QuantityBox.Text.Trim(), out quantity) || quantity < 1)
                {
                    ShowWarning("Miktar 1 veya daha büyük bir tam sayı olmalı.");
                    QuantityBox.Focus();
                    return;
                }

                if (quantity > _available)
                {
                    ShowWarning("Bu üründen zimmetlenebilecek miktar en fazla " + _available + ".");
                    QuantityBox.Focus();
                    return;
                }
            }

            // Yazım hatasını yakalamak için: benzer ama farklı yazılmış bir kişi daha önce varsa sor.
            var knownNames = _people.Select(p => p.Name).ToList();
            var exact = knownNames.Any(n => string.Equals(n, personName, StringComparison.CurrentCultureIgnoreCase));

            if (!exact)
            {
                var close = SimilarityHelper.FindClosestMatch(personName, knownNames);
                if (close != null)
                {
                    var answer = MessageBox.Show(this,
                        "\"" + personName + "\" yazdın, ama daha önce \"" + close + "\" adıyla zimmet yapılmış.\n\n" +
                        "Evet: \"" + close + "\" kullanılsın\n" +
                        "Hayır: yazdığım gibi kalsın\n" +
                        "İptal: geri dön",
                        "Şunu mu demek istediniz?", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

                    if (answer == MessageBoxResult.Cancel)
                    {
                        return;
                    }

                    if (answer == MessageBoxResult.Yes)
                    {
                        personName = close;
                        var known = _people.FirstOrDefault(p => p.Name == close);
                        if (known != null)
                        {
                            if (string.IsNullOrWhiteSpace(RegistryNoBox.Text))
                            {
                                RegistryNoBox.Text = known.RegistryNo ?? "";
                            }

                            if (string.IsNullOrWhiteSpace(DepartmentBox.Text))
                            {
                                DepartmentBox.Text = known.Department ?? "";
                            }
                        }
                    }
                }
            }

            var newIds = new List<int>();

            try
            {
                if (_bulkIds == null)
                {
                    newIds.Add(AssignmentRepository.Assign(
                        _productId,
                        quantity,
                        personName,
                        RegistryNoBox.Text.Trim(),
                        (DepartmentBox.Text ?? "").Trim(),
                        NoteBox.Text.Trim()));
                }
                else
                {
                    foreach (var id in _bulkIds)
                    {
                        var available = AssignmentRepository.GetAvailableQuantity(id);
                        if (available < 1)
                        {
                            continue;
                        }

                        newIds.Add(AssignmentRepository.Assign(
                            id,
                            available,
                            personName,
                            RegistryNoBox.Text.Trim(),
                            (DepartmentBox.Text ?? "").Trim(),
                            NoteBox.Text.Trim()));
                    }
                }
            }
            catch (Exception ex)
            {
                var extra = newIds.Count > 0
                    ? "\n\nBundan önce " + newIds.Count + " ürün zimmetlendi (Zimmetler ekranından görebilirsin)."
                    : "";

                MessageBox.Show(this, "Zimmetlenemedi:\n" + ex.Message + extra, "Depo Durumu",
                    MessageBoxButton.OK, MessageBoxImage.Error);

                if (newIds.Count > 0)
                {
                    DialogResult = true;
                }

                return;
            }

            // Zimmet kaydedildi: tutanak hemen yazdırılmaz, önce ekranda (önizleme) açılır.
            // Yazdırmak istenirse önizleme penceresindeki "Yazdır" düğmesi kullanılır.
            var assignments = newIds
                .Select(id => AssignmentRepository.GetById(id))
                .Where(a => a != null)
                .ToList();

            if (assignments.Count > 0)
            {
                AssignmentReceiptPrinter.ShowPreview(this, assignments);
            }

            DialogResult = true;
        }

        private void ShowWarning(string message)
        {
            MessageBox.Show(this, message, "Depo Durumu", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}