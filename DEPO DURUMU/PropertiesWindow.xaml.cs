using System.Globalization;
using System.Linq;
using System.Windows;
using DEPO_DURUMU.Data;

namespace DEPO_DURUMU
{
    public partial class PropertiesWindow : Window
    {
        public PropertiesWindow()
        {
            InitializeComponent();
            LoadProperties();
        }

        private void LoadProperties()
        {
            PropertyList.ItemsSource = null;
            PropertyList.ItemsSource = PropertyDefinitionRepository.GetAll();
        }

        private void AddPropertyButton_Click(object sender, RoutedEventArgs e)
        {
            var name = NewPropertyNameBox.Text.Trim();
            if (string.IsNullOrEmpty(name))
            {
                MessageBox.Show("Lütfen bir özellik adı yazın.", "Depo Durumu",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var dataTypeItem = NewPropertyDataTypeCombo.SelectedItem as System.Windows.Controls.ComboBoxItem;
            var dataType = dataTypeItem != null ? dataTypeItem.Content.ToString() : "Metin";

            var existingProperties = PropertyDefinitionRepository.GetAll();
            var turkishCulture = new CultureInfo("tr-TR");

            var propertyExists = existingProperties.Any(p =>
                string.Compare(p.Name, name, turkishCulture, CompareOptions.IgnoreCase) == 0);
            if (propertyExists)
            {
                MessageBox.Show("Bu özellik zaten var.", "Depo Durumu",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var closeProperty = SimilarityHelper.FindClosestMatch(name, existingProperties.Select(p => p.Name));
            if (closeProperty != null)
            {
                var confirm = MessageBox.Show(
                    "\"" + closeProperty + "\" mi demek istediniz?\n\n" +
                    "Yine de \"" + name + "\" adında yeni bir özellik eklemek istiyor musunuz?",
                    "Şunu mu demek istediniz?", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (confirm != MessageBoxResult.Yes)
                {
                    return;
                }
            }

            PropertyDefinitionRepository.Add(name, dataType, false);
            LogRepository.Add(null, name + " (" + dataType + ")", "Özellik eklendi");

            NewPropertyNameBox.Text = "";
            LoadProperties();
        }

        private void RenamePropertyButton_Click(object sender, RoutedEventArgs e)
        {
            var selected = PropertyList.SelectedItem as PropertyDefinition;
            if (selected == null)
            {
                MessageBox.Show("Önce listeden bir özellik seçin.", "Depo Durumu",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (IsFixedProperty(selected))
            {
                MessageBox.Show("\"" + selected.Name + "\" sabit bir özelliktir, adı değiştirilemez.",
                    "Depo Durumu", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var newName = SimpleInputWindow.Ask(this, "Yeniden Adlandır", "Yeni ad:", selected.Name);

            if (newName == null)
            {
                return;
            }

            PropertyDefinitionRepository.Rename(selected.Id, newName);
            LogRepository.Add(null, "Eski ad: " + selected.Name + " -> " + newName, "Özellik yeniden adlandırıldı");
            LoadProperties();
        }

        private void DeletePropertyButton_Click(object sender, RoutedEventArgs e)
        {
            var selected = PropertyList.SelectedItem as PropertyDefinition;
            if (selected == null)
            {
                MessageBox.Show("Önce listeden bir özellik seçin.", "Depo Durumu",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (IsFixedProperty(selected))
            {
                MessageBox.Show("\"" + selected.Name + "\" sabit bir özelliktir, silinemez.",
                    "Depo Durumu", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var result = MessageBox.Show(
                "\"" + selected.Name + "\" özelliğini silmek istediğine emin misin?",
                "Depo Durumu", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            var deleted = PropertyDefinitionRepository.Delete(selected.Id);
            if (deleted)
            {
                LogRepository.Add(null, selected.Name, "Özellik silindi");
            }

            if (!deleted)
            {
                MessageBox.Show(
                    "\"" + selected.Name + "\" özelliği en az bir üründe dolu bir değere sahip olduğu için silinemiyor.\n\n" +
                    "Önce o ürünlerin bu alanını boşaltın (ürünü düzenleyip değeri silin) ya da ilgili ürünleri silin, sonra tekrar deneyin.",
                    "Depo Durumu", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            LoadProperties();
        }

        /// <summary>
        /// "Seri No" ve "Sistem İsmi" her zaman var olması gereken sabit özelliklerdir;
        /// yeniden adlandırılamaz ve silinemezler.
        /// </summary>
        private static bool IsFixedProperty(PropertyDefinition property)
        {
            return property.Name == "Seri No" || property.Name == "Sistem İsmi";
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}