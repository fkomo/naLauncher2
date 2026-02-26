using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;

namespace naLauncher2.Wpf
{
    public partial class SettingsDialog : Window
    {
        public string? SelectedLibraryPath { get; private set; }
        public string[] SelectedSources => [.. _sources];

        readonly ObservableCollection<string> _sources = [];

        public SettingsDialog(string? libraryPath, string[] sources)
        {
            InitializeComponent();

            SelectedLibraryPath = libraryPath;
            LibraryPathText.Text = libraryPath ?? "(not set)";

            foreach (var s in sources)
                _sources.Add(s);

            SourcesList.ItemsSource = _sources;
        }

        void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();

        void OpenLibrary_Click(object sender, MouseButtonEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select game library file",
                Filter = "JSON files (*.json)|*.json",
                CheckFileExists = true,
            };

            if (dialog.ShowDialog(this) == true)
            {
                SelectedLibraryPath = dialog.FileName;
                LibraryPathText.Text = dialog.FileName;
            }
        }

        void NewLibrary_Click(object sender, MouseButtonEventArgs e)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Create new game library",
                Filter = "JSON files (*.json)|*.json",
                FileName = "library.json",
            };

            if (dialog.ShowDialog(this) == true)
            {
                SelectedLibraryPath = dialog.FileName;
                LibraryPathText.Text = dialog.FileName;
            }
        }

        void AddSource_Click(object sender, MouseButtonEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Select a source directory for your games",
            };

            if (dialog.ShowDialog(this) == true && !_sources.Contains(dialog.FolderName))
                _sources.Add(dialog.FolderName);
        }

        void RemoveSource_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is string path)
                _sources.Remove(path);
        }

        void Save_Click(object sender, MouseButtonEventArgs e) => DialogResult = true;

        void Cancel_Click(object sender, MouseButtonEventArgs e) => DialogResult = false;

        void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
                DialogResult = false;
        }
    }
}
