using System;
using System.Windows;
using DEPO_DURUMU.Data;

namespace DEPO_DURUMU
{
    /// <summary>
    /// Zimmetler defterindeki bir kaydı düzeltmek için pencere (kâğıtta yapılan düzeltmeler
    /// sisteme de işlenebilsin diye). Kaydedilirse DialogResult true olur.
    /// </summary>
    public partial class AssignmentEditWindow : Window
    {
        private readonly Assignment _assignment;

        public AssignmentEditWindow(int assignmentId)
        {
            InitializeComponent();

            _assignment = AssignmentRepository.GetById(assignmentId);
            if (_assignment == null)
            {
                MessageBox.Show("Zimmet kaydı bulunamadı (silinmiş olabilir).", "Depo Takip",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                Loaded += (s, e) => Close();
                return;
            }

            TypeBox.Text = _assignment.TypeName;
            SystemNameBox.Text = _assignment.SystemName;
            SerialBox.Text = _assignment.SerialNo;
            PersonBox.Text = _assignment.PersonName;
            RegistryBox.Text = _assignment.RegistryNo;
            DepartmentBox.Text = _assignment.Department;
            QuantityBox.Text = _assignment.Quantity.ToString();
            AssignedAtBox.Text = _assignment.AssignedAtText;
            NoteBox.Text = _assignment.AssignedNote;

            ReturnedCheck.IsChecked = _assignment.IsReturned;
            ReturnedAtBox.Text = _assignment.ReturnedAtText;
            ReturnedNoteBox.Text = _assignment.ReturnedNote;
            UpdateReturnPanel();

            // Aktif zimmette ve ürün depodaysa ürün bilgileri üründen canlı geliyor; burada değiştirilmez.
            var lockProduct = _assignment.IsLinked;
            TypeBox.IsEnabled = !lockProduct;
            SystemNameBox.IsEnabled = !lockProduct;
            SerialBox.IsEnabled = !lockProduct;

            InfoText.Text = lockProduct
                ? "Ürün depoda ve zimmet aktif: ürün bilgileri üründen otomatik gelir, buradan değiştirilemez. " +
                  "Seri No gibi ürün bilgisini ürün detayından düzeltebilirsin."
                : "Ürün bilgileri bu kayıtta sabit metin olarak duruyor; gerekirse buradan düzeltebilirsin.";
        }

        private void ReturnedCheck_Changed(object sender, RoutedEventArgs e)
        {
            UpdateReturnPanel();

            // İade işaretlenince tarih boşsa şimdiki zamanı öner.
            if (ReturnedCheck.IsChecked == true && string.IsNullOrWhiteSpace(ReturnedAtBox.Text))
            {
                ReturnedAtBox.Text = AssignmentRepository.FormatTime(AssignmentRepository.NowRaw());
            }
        }

        private void UpdateReturnPanel()
        {
            ReturnPanel.Visibility = ReturnedCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (_assignment == null)
            {
                return;
            }

            int quantity;
            if (!int.TryParse(QuantityBox.Text.Trim(), out quantity) || quantity < 1)
            {
                Warn("Miktar 1 veya daha büyük bir tam sayı olmalı.");
                return;
            }

            string assignedRaw;
            if (!AssignmentRepository.TryParseUiTime(AssignedAtBox.Text, out assignedRaw))
            {
                Warn("Zimmet tarihi geçersiz. Örnek: 28.09.2026 14:30 (ya da sadece 28.09.2026).");
                return;
            }

            var isReturned = ReturnedCheck.IsChecked == true;
            string returnedRaw = null;

            if (isReturned && !AssignmentRepository.TryParseUiTime(ReturnedAtBox.Text, out returnedRaw))
            {
                Warn("İade tarihi geçersiz. Örnek: 28.09.2026 14:30 (ya da sadece 28.09.2026).");
                return;
            }

            if (isReturned && string.CompareOrdinal(returnedRaw, assignedRaw) < 0)
            {
                var answer = MessageBox.Show(this,
                    "İade tarihi zimmet tarihinden önce görünüyor. Yine de kaydedilsin mi?",
                    "Depo Takip", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (answer != MessageBoxResult.Yes)
                {
                    return;
                }
            }

            var edited = new Assignment
            {
                Id = _assignment.Id,
                ProductId = _assignment.ProductId,
                TypeName = TypeBox.Text.Trim(),
                SystemName = SystemNameBox.Text.Trim(),
                SerialNo = SerialBox.Text.Trim(),
                Quantity = quantity,
                PersonName = PersonBox.Text.Trim(),
                RegistryNo = RegistryBox.Text.Trim(),
                Department = DepartmentBox.Text.Trim(),
                AssignedAt = assignedRaw,
                AssignedNote = NoteBox.Text.Trim(),
                IsReturned = isReturned,
                ReturnedAt = returnedRaw,
                ReturnedNote = ReturnedNoteBox.Text.Trim()
            };

            try
            {
                AssignmentRepository.Update(edited);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Depo Takip", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            DialogResult = true;
        }

        private void Warn(string message)
        {
            MessageBox.Show(this, message, "Depo Takip", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
