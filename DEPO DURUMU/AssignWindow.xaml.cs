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

            int quantity;
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

            int newId;

            try
            {
                newId = AssignmentRepository.Assign(
                    _productId,
                    quantity,
                    personName,
                    RegistryNoBox.Text.Trim(),
                    (DepartmentBox.Text ?? "").Trim(),
                    NoteBox.Text.Trim());
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Zimmetlenemedi:\n" + ex.Message, "Depo Durumu",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var print = MessageBox.Show(this,
                "Ürün zimmetlendi.\n\nZimmet tutanağı şimdi yazdırılsın mı?",
                "Depo Durumu", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (print == MessageBoxResult.Yes)
            {
                var assignment = AssignmentRepository.GetById(newId);
                if (assignment != null)
                {
                    AssignmentReceiptPrinter.Print(this, assignment);
                }
            }

            DialogResult = true;
        }

        private void ShowWarning(string message)
        {
            MessageBox.Show(this, message, "Depo Durumu", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
