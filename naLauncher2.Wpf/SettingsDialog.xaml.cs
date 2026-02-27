using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;

namespace naLauncher2.Wpf
{
    public partial class SettingsDialog : Window
    {
        public string? SelectedLibraryPath { get; private set; }
        public string? SelectedImageCachePath { get; private set; }
        public string[] SelectedSources => [.. _sources];

        readonly ObservableCollection<string> _sources = [];

        public SettingsDialog()
        {
            InitializeComponent();

            var appSettings = AppSettings.Instance;

            SelectedLibraryPath = appSettings.LibraryPath;
            LibraryPathText.Text = appSettings.LibraryPath ?? "(not set)";

            SelectedImageCachePath = appSettings.ImageCachePath;
            ImageCachePathText.Text = appSettings.ImageCachePath ?? "(default)";

            foreach (var s in appSettings.Sources)
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

        void BrowseImageCache_Click(object sender, MouseButtonEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Select image cache directory",
            };

            if (dialog.ShowDialog(this) == true)
            {
                SelectedImageCachePath = dialog.FolderName;
                ImageCachePathText.Text = dialog.FolderName;
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

        async void Save_Click(object sender, MouseButtonEventArgs e)
        {
            if (SelectedLibraryPath == null)
            {
                MessageBox.Show(this, "Please select a game library file.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            AppSettings.Instance.LibraryPath = SelectedLibraryPath;
            AppSettings.Instance.ImageCachePath = SelectedImageCachePath;
            AppSettings.Instance.Sources = SelectedSources;

            await AppSettings.Instance.Save();

            DialogResult = true;
        }

        void Cancel_Click(object sender, MouseButtonEventArgs e) => DialogResult = false;

        void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
                DialogResult = false;
        }
    }
}
