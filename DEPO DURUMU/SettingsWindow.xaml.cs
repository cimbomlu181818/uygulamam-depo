using System.Windows;
using DEPO_DURUMU.Data;

namespace DEPO_DURUMU
{
    public partial class SettingsWindow : Window
    {
        public SettingsWindow()
        {
            InitializeComponent();
            LoadTypes();
            LoadProperties();
        }

        private void LoadTypes()
        {
            TypeList.ItemsSource = null;
            TypeList.ItemsSource = ProductTypeRepository.GetAll();
        }

        private void AddTypeButton_Click(object sender, RoutedEventArgs e)
        {
            var name = NewTypeNameBox.Text.Trim();
            if (string.IsNullOrEmpty(name))
            {
                MessageBox.Show("Lütfen bir ürün cinsi adı yazın.", "Depo Durumu",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ProductTypeRepository.Add(name);
            NewTypeNameBox.Text = "";
            LoadTypes();
        }

        private void RenameTypeButton_Click(object sender, RoutedEventArgs e)
        {
            var selected = TypeList.SelectedItem as ProductType;
            if (selected == null)
            {
                MessageBox.Show("Önce listeden bir ürün cinsi seçin.", "Depo Durumu",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var newName = SimpleInputWindow.Ask(this, "Yeniden Adlandır", "Yeni ad:", selected.Name);

            if (newName == null)
            {
                return;
            }

            ProductTypeRepository.Rename(selected.Id, newName);
            LoadTypes();
        }

        private void DeleteTypeButton_Click(object sender, RoutedEventArgs e)
        {
            var selected = TypeList.SelectedItem as ProductType;
            if (selected == null)
            {
                MessageBox.Show("Önce listeden bir ürün cinsi seçin.", "Depo Durumu",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var result = MessageBox.Show(
                "\"" + selected.Name + "\" ürün cinsini silmek istediğine emin misin?",
                "Depo Durumu", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            var deleted = ProductTypeRepository.Delete(selected.Id);
            if (!deleted)
            {
                MessageBox.Show(
                    "\"" + selected.Name + "\" cinsine ait ürünler olduğu için silinemiyor.\n\n" +
                    "Önce o cinse ait tüm ürünleri silin, sonra tekrar deneyin.",
                    "Depo Durumu", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            LoadTypes();
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

            PropertyDefinitionRepository.Add(name, dataType, false);

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
    }
}