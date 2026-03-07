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
        public string? SelectedLogPath { get; private set; }
        public string[] SelectedSources => [.. _sources];

        readonly ObservableCollection<string> _sources = [];
        readonly ObservableCollection<string> _extensions = [];

        public SettingsDialog()
        {
            InitializeComponent();

            var appSettings = AppSettings.Instance;

            SelectedLibraryPath = appSettings.LibraryPath;
            LibraryPathText.Text = appSettings.LibraryPath ?? "(not set)";

            SelectedImageCachePath = appSettings.ImageCachePath;
            ImageCachePathText.Text = appSettings.ImageCachePath ?? "(default)";

            SelectedLogPath = appSettings.LogPath;
            LogPathText.Text = appSettings.LogPath ?? "(default)";

            foreach (var s in appSettings.Sources)
                _sources.Add(s);

            SourcesList.ItemsSource = _sources;

            GameExtensionsBox.Text = string.Join(", ", appSettings.GameExtensions);

            foreach (var ext in GameLibrary.Instance.ExtensionsUsed)
                _extensions.Add(ext);

            ExtensionsList.ItemsSource = _extensions;

            if (appSettings.TwitchDev != null)
            {
                TwitchClientIdBox.Text = appSettings.TwitchDev.ClientId;
                TwitchClientSecretBox.Text = appSettings.TwitchDev.ClientSecret;
            }

            _sources.CollectionChanged += (_, _) => MarkDirty();
            GameExtensionsBox.TextChanged += (_, _) => MarkDirty();
            TwitchClientIdBox.TextChanged += (_, _) => MarkDirty();
            TwitchClientSecretBox.TextChanged += (_, _) => MarkDirty();
        }

        void MarkDirty() => SaveButton.Visibility = Visibility.Visible;

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
                MarkDirty();
            }
        }

        async void RestoreLibrary_Click(object sender, MouseButtonEventArgs e)
        {
            var libraryPath = AppSettings.Instance.LibraryPath;
            if (libraryPath == null)
            {
                new MessageDialog("Error", "No game library is currently loaded.") { Owner = this }.ShowDialog();
                return;
            }

            var libraryDir = Path.GetDirectoryName(Path.GetFullPath(libraryPath)) ?? ".";

            var restoreDialog = new RestoreDialog(libraryDir) { Owner = this };
            if (restoreDialog.ShowDialog() != true)
                return;

            var confirmDialog = new ConfirmationDialog($"Restore library from backup of {restoreDialog.SelectedDisplayDate}?") { Owner = this };
            if (confirmDialog.ShowDialog() != true)
                return;

            await GameLibrary.Instance.Restore(restoreDialog.SelectedBackupPath!);
            await GameLibrary.Instance.Save();

            new MessageDialog("Restored", $"Library restored to {restoreDialog.SelectedDisplayDate}.") { Owner = this }.ShowDialog();

            (Owner as MainWindow)?.RefreshAllSections();
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
                MarkDirty();
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
                MarkDirty();
            }
        }

        void BrowseLogPath_Click(object sender, MouseButtonEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Select log directory",
            };

            if (dialog.ShowDialog(this) == true)
            {
                SelectedLogPath = dialog.FolderName;
                LogPathText.Text = dialog.FolderName;
                MarkDirty();
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
                new MessageDialog("Error", "Please select a game library file.") { Owner = this }.ShowDialog();
                return;
            }

            AppSettings.Instance.LibraryPath = SelectedLibraryPath;
            AppSettings.Instance.ImageCachePath = SelectedImageCachePath;
            AppSettings.Instance.LogPath = SelectedLogPath;
            AppSettings.Instance.Sources = SelectedSources;
            AppSettings.Instance.GameExtensions = GameExtensionsBox.Text
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (TwitchClientIdBox.Text != null && TwitchClientSecretBox.Text != null)
            {
                AppSettings.Instance.TwitchDev = new AppSettings.TwitchDevSettings
                {
                    ClientId = TwitchClientIdBox.Text,
                    ClientSecret = TwitchClientSecretBox.Text,
                };

                App.SettingsChanged();
            }

            if (_extensionsToRemove.Count > 0)
                await GameLibrary.Instance.RemoveExtensions(_extensionsToRemove.ToArray());

            await AppSettings.Instance.Save();

            DialogResult = true;
        }

        void Cancel_Click(object sender, MouseButtonEventArgs e) => DialogResult = false;

        readonly List<string> _extensionsToRemove = [];

        void RemoveExtension_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement { Tag: string extensionName })
                return;

            var dialog = new ConfirmationDialog($"Remove all '{extensionName}' extensions from library?") { Owner = this };
            if (dialog.ShowDialog() != true)
                return;

            _extensionsToRemove.Add(extensionName);
            _extensions.Remove(extensionName);

            MarkDirty();
        }

        void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
                DialogResult = false;
        }
    }
}
