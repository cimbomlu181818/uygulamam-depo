using System.Windows;

namespace DEPO_DURUMU
{
    public partial class SimpleInputWindow : Window
    {
        public string ResultText { get; private set; }

        public SimpleInputWindow(string title, string prompt, string defaultValue)
        {
            InitializeComponent();
            Title = title;
            PromptText.Text = prompt;
            InputBox.Text = defaultValue;
            InputBox.Focus();
            InputBox.SelectAll();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            ResultText = InputBox.Text;
            DialogResult = true;
        }

        /// <summary>
        /// Kullanıcıdan tek satır metin ister. İptal edilirse veya boş bırakılırsa null döner.
        /// </summary>
        public static string Ask(Window owner, string title, string prompt, string defaultValue = "")
        {
            var window = new SimpleInputWindow(title, prompt, defaultValue) { Owner = owner };
            var ok = window.ShowDialog();

            if (ok == true && !string.IsNullOrWhiteSpace(window.ResultText))
            {
                return window.ResultText.Trim();
            }

            return null;
        }
    }
}