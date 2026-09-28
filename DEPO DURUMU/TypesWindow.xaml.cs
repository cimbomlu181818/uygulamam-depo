using System.Globalization;
using System.Linq;
using System.Windows;
using DEPO_DURUMU.Data;

namespace DEPO_DURUMU
{
    public partial class TypesWindow : Window
    {
        public TypesWindow()
        {
            InitializeComponent();
            LoadTypes();
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

            var existingTypeNames = ProductTypeRepository.GetAll().Select(t => t.Name).ToList();
            var turkish = new CultureInfo("tr-TR");

            var typeExists = existingTypeNames.Any(n =>
                string.Compare(n, name, turkish, CompareOptions.IgnoreCase) == 0);
            if (typeExists)
            {
                MessageBox.Show("Bu ürün cinsi zaten var.", "Depo Durumu",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var closeType = SimilarityHelper.FindClosestMatch(name, existingTypeNames);
            if (closeType != null)
            {
                var confirm = MessageBox.Show(
                    "\"" + closeType + "\" mi demek istediniz?\n\n" +
                    "Yine de \"" + name + "\" adında yeni bir ürün cinsi eklemek istiyor musunuz?",
                    "Şunu mu demek istediniz?", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (confirm != MessageBoxResult.Yes)
                {
                    return;
                }
            }

            ProductTypeRepository.Add(name);
            LogRepository.Add(name, "", "Cins eklendi");
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
            LogRepository.Add(newName, "Eski ad: " + selected.Name, "Cins yeniden adlandırıldı");
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
            if (deleted)
            {
                LogRepository.Add(selected.Name, "", "Cins silindi");
            }

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

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}