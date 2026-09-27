using System.Windows;
using System.Windows.Controls;
using DEPO_DURUMU.Data;

namespace DEPO_DURUMU
{
    public partial class LogWindow : Window
    {
        public LogWindow()
        {
            InitializeComponent();
            LoadAll();
        }

        private void LoadAll()
        {
            LogGrid.ItemsSource = LogRepository.GetAll();
        }

        private void LogSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            LogSearchPlaceholder.Visibility = string.IsNullOrEmpty(LogSearchBox.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;

            var text = LogSearchBox.Text.Trim();

            LogGrid.ItemsSource = text.Length >= 2
                ? LogRepository.Search(text)
                : LogRepository.GetAll();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}