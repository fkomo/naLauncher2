using System.Windows;
using System.Windows.Input;

namespace naLauncher2.Wpf
{
    public partial class LibrarySetupDialog : Window
    {
        public string? SelectedPath { get; private set; }

        public LibrarySetupDialog()
        {
            InitializeComponent();
        }

        void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();

        void LoadOption_Click(object sender, MouseButtonEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select game library file",
                Filter = "JSON files (*.json)|*.json",
                CheckFileExists = true,
            };

            if (dialog.ShowDialog(this) == true)
            {
                SelectedPath = dialog.FileName;
                DialogResult = true;
            }
        }

        void CreateOption_Click(object sender, MouseButtonEventArgs e)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Create new game library",
                Filter = "JSON files (*.json)|*.json",
                FileName = "library.json",
            };

            if (dialog.ShowDialog(this) == true)
            {
                SelectedPath = dialog.FileName;
                DialogResult = true;
            }
        }

        void ExitOption_Click(object sender, MouseButtonEventArgs e) => DialogResult = false;

        void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
                DialogResult = false;
        }
    }
}
