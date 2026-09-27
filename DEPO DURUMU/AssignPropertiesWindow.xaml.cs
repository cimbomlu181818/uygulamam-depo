using System.Linq;
using System.Windows;
using DEPO_DURUMU.Data;

namespace DEPO_DURUMU
{
    public partial class AssignPropertiesWindow : Window
    {
        private readonly int _productTypeId;

        public AssignPropertiesWindow(int productTypeId, string typeName)
        {
            InitializeComponent();

            _productTypeId = productTypeId;
            TitleText.Text = "\"" + typeName + "\" için özellikler";

            LoadLists();
        }

        private void LoadLists()
        {
            var assigned = TypePropertyRepository.GetForType(_productTypeId);
            var assignedIds = assigned.Select(p => p.Id).ToList();

            var all = PropertyDefinitionRepository.GetAll();
            var available = all.Where(p => !assignedIds.Contains(p.Id)).ToList();

            AvailableList.ItemsSource = null;
            AvailableList.DisplayMemberPath = "Name";
            AvailableList.ItemsSource = available;

            AssignedList.ItemsSource = null;
            AssignedList.DisplayMemberPath = "Name";
            AssignedList.ItemsSource = assigned;
        }

        private void AddButton_Click(object sender, RoutedEventArgs e)
        {
            var selected = AvailableList.SelectedItem as PropertyDefinition;
            if (selected == null)
            {
                MessageBox.Show("Önce sol listeden bir özellik seçin.", "Depo Durumu",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            TypePropertyRepository.AddPropertyToType(_productTypeId, selected.Id);
            LoadLists();
        }

        private void RemoveButton_Click(object sender, RoutedEventArgs e)
        {
            var selected = AssignedList.SelectedItem as PropertyDefinition;
            if (selected == null)
            {
                MessageBox.Show("Önce sağ listeden bir özellik seçin.", "Depo Durumu",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            TypePropertyRepository.RemovePropertyFromType(_productTypeId, selected.Id);
            LoadLists();
        }

        private void MoveUpButton_Click(object sender, RoutedEventArgs e)
        {
            MoveSelected(-1);
        }

        private void MoveDownButton_Click(object sender, RoutedEventArgs e)
        {
            MoveSelected(1);
        }

        /// <summary>
        /// Seçili özelliği listede bir yukarı (-1) veya bir aşağı (+1) taşır ve
        /// yeni sırayı veritabanına kaydeder.
        /// </summary>
        private void MoveSelected(int direction)
        {
            var selected = AssignedList.SelectedItem as PropertyDefinition;
            if (selected == null)
            {
                MessageBox.Show("Önce sağ listeden bir özellik seçin.", "Depo Durumu",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var assigned = TypePropertyRepository.GetForType(_productTypeId);
            var ids = assigned.Select(p => p.Id).ToList();

            var index = ids.IndexOf(selected.Id);
            var newIndex = index + direction;

            if (index < 0 || newIndex < 0 || newIndex >= ids.Count)
            {
                return;
            }

            var temp = ids[newIndex];
            ids[newIndex] = ids[index];
            ids[index] = temp;

            TypePropertyRepository.UpdateOrder(_productTypeId, ids);
            LoadLists();

            // Taşıdığımız özelliği yeni yerinde seçili tut.
            AssignedList.SelectedItem = (AssignedList.ItemsSource as System.Collections.Generic.List<PropertyDefinition>)
                ?.FirstOrDefault(p => p.Id == selected.Id);
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }
    }
}