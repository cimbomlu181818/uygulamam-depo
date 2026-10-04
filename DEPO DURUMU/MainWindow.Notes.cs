using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DEPO_DURUMU.Data;

namespace DEPO_DURUMU
{
    // ---------- NOTLAR (ÇOKLU EXCEL) ----------
    // Barda "+ Yeni Excel" düğmesi vardır. Basınca ad sorar, yeni Excel'i oluşturur, açar ve barda adıyla bir düğme belirir.
    // Bardaki Excel düğmesine basınca o Excel açılır; sağ tıklayınca "Adını değiştir" ve "Sil" çıkar.
    public partial class MainWindow
    {
        private const int MaxNotebookNameLength = 30;

        // Barda şu an görünen Excel düğmeleri (yeniden çizerken önce bunlar kaldırılır).
        private readonly List<MenuItem> _notebookMenuItems = new List<MenuItem>();

        private void MainMenu_Loaded(object sender, RoutedEventArgs e)
        {
            RefreshNotebookButtons();
        }

        /// <summary>Bardaki Excel düğmelerini veritabanındaki listeye göre baştan kurar ("+ Yeni Excel"in hemen yanına).</summary>
        private void RefreshNotebookButtons()
        {
            foreach (var item in _notebookMenuItems)
            {
                MainMenu.Items.Remove(item);
            }

            _notebookMenuItems.Clear();

            List<NoteBook> books;
            try
            {
                books = NoteBookRepository.LoadAll();
            }
            catch (Exception)
            {
                // Liste okunamazsa bar yalnızca "+ Yeni Excel" ile kalır.
                return;
            }

            var position = MainMenu.Items.IndexOf(NewNotebookMenuItem) + 1;
            foreach (var book in books)
            {
                var item = CreateNotebookMenuItem(book);
                MainMenu.Items.Insert(position, item);
                position++;
                _notebookMenuItems.Add(item);
            }
        }

        private MenuItem CreateNotebookMenuItem(NoteBook book)
        {
            // Başlık TextBlock olur ki ad içindeki "_" işareti kısayol harfi sayılmasın.
            var item = new MenuItem
            {
                Header = new TextBlock { Text = book.Name },
                Tag = book,
                ToolTip = "Açmak için tıkla, adını değiştirmek veya silmek için sağ tıkla"
            };
            item.Click += NotebookMenuItem_Click;

            var rename = new MenuItem { Header = "Adını değiştir", Tag = book };
            rename.Click += NotebookRename_Click;

            var delete = new MenuItem
            {
                Header = "Sil",
                Foreground = new SolidColorBrush(Color.FromRgb(0xB0, 0x00, 0x20)),
                Tag = book
            };
            delete.Click += NotebookDelete_Click;

            var menu = new ContextMenu();
            menu.Items.Add(rename);
            menu.Items.Add(delete);
            item.ContextMenu = menu;

            return item;
        }

        private static NoteBook BookOf(object sender)
        {
            var element = sender as FrameworkElement;
            return element == null ? null : element.Tag as NoteBook;
        }

        private void OpenNotebook(NoteBook book)
        {
            var window = new NotesWindow(book.Id, book.Name) { Owner = this };
            window.ShowDialog();
        }

        private void NotebookMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var book = BookOf(sender);
            if (book != null)
            {
                OpenNotebook(book);
            }
        }

        /// <summary>Ad uygunsa true döner; değilse nedenini söyler. exceptId: adı değiştirilen Excel (kendi adıyla çakışmasın).</summary>
        private bool IsNotebookNameValid(string name, int exceptId)
        {
            if (name.Length > MaxNotebookNameLength)
            {
                MessageBox.Show(this, "Ad en fazla " + MaxNotebookNameLength + " karakter olabilir.", "Excel",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }

            foreach (var item in _notebookMenuItems)
            {
                var other = item.Tag as NoteBook;
                if (other != null && other.Id != exceptId &&
                    string.Equals(other.Name, name, StringComparison.CurrentCultureIgnoreCase))
                {
                    MessageBox.Show(this, "\"" + name + "\" adında bir Excel zaten var. Başka bir ad yaz.", "Excel",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return false;
                }
            }

            return true;
        }

        private void NewNotebookMenu_Click(object sender, RoutedEventArgs e)
        {
            var name = SimpleInputWindow.Ask(this, "Yeni Excel", "Bu Excel'in adı ne olsun?", "");
            if (name == null || !IsNotebookNameValid(name, 0))
            {
                return;
            }

            NoteBook book;
            try
            {
                book = NoteBookRepository.Insert(name);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Excel oluşturulamadı:\n" + ex.Message, "Excel",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            RefreshNotebookButtons();
            OpenNotebook(book);
        }

        private void NotebookRename_Click(object sender, RoutedEventArgs e)
        {
            var book = BookOf(sender);
            if (book == null)
            {
                return;
            }

            var name = SimpleInputWindow.Ask(this, "Adını değiştir", "Yeni ad:", book.Name);
            if (name == null || !IsNotebookNameValid(name, book.Id))
            {
                return;
            }

            try
            {
                NoteBookRepository.Rename(book.Id, name);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Ad değiştirilemedi:\n" + ex.Message, "Excel",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            RefreshNotebookButtons();
        }

        private void NotebookDelete_Click(object sender, RoutedEventArgs e)
        {
            var book = BookOf(sender);
            if (book == null)
            {
                return;
            }

            var answer = MessageBox.Show(this,
                "\"" + book.Name + "\" Excel'i ve içindeki tüm sayfalar silinsin mi?\n\nBu işlem geri alınamaz.",
                "Excel", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                NoteBookRepository.Delete(book.Id);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Excel silinemedi:\n" + ex.Message, "Excel",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            RefreshNotebookButtons();
        }
    }
}