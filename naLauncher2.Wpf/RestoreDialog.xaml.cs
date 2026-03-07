using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Ujeby.Tools;

namespace naLauncher2.Wpf
{
    public partial class RestoreDialog : Window
    {
        public string? SelectedBackupPath { get; private set; }
        public string? SelectedDisplayDate { get; private set; }

        public RestoreDialog(string libraryDir)
        {
            InitializeComponent();

            var entries = Directory.GetFiles(libraryDir, "*.bak")
                .Select(BackupEntry.From)
                .OrderByDescending(x => x.DateTime ?? System.DateTime.MinValue)
                .DistinctBy(x => x.ContentHash)
                .ToList();

            if (entries.Count == 0)
            {
                EmptyText.Visibility = Visibility.Visible;
                BackupScroll.Visibility = Visibility.Collapsed;
            }
            else
            {
                BackupList.ItemsSource = entries;
            }
        }

        void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();

        void BackupList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
            RestoreButton.Visibility = BackupList.SelectedItem != null ? Visibility.Visible : Visibility.Collapsed;

        void BackupList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (BackupList.SelectedItem is BackupEntry entry)
                Confirm(entry);
        }

        void Restore_Click(object sender, MouseButtonEventArgs e)
        {
            if (BackupList.SelectedItem is BackupEntry entry)
                Confirm(entry);
        }

        void Confirm(BackupEntry entry)
        {
            SelectedBackupPath = entry.Path;
            SelectedDisplayDate = entry.DisplayDate;
            DialogResult = true;
        }

        void Cancel_Click(object sender, MouseButtonEventArgs e) => DialogResult = false;

        void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && BackupList.SelectedItem is BackupEntry entry)
                Confirm(entry);
            else if (e.Key == Key.Escape)
                DialogResult = false;
        }

        sealed record BackupEntry(string Path, System.DateTime? DateTime, long LibrarySize, string ContentHash)
        {
            const string TimestampFormat = "yyyyMMddHHmmss";

            public string DisplayDate => DateTime.HasValue
                ? DateTime.Value.ToString("g")
                : System.IO.Path.GetFileNameWithoutExtension(Path);

            public string DisplaySize => LibrarySize switch
            {
                >= 1_048_576 => $"{LibrarySize / 1_048_576.0:F1} MB",
                >= 1_024     => $"{LibrarySize / 1_024.0:F1} KB",
                _            => $"{LibrarySize} B",
            };

            public static BackupEntry From(string path)
            {
                var baseName = System.IO.Path.GetFileNameWithoutExtension(path);
                var parts = baseName.Split('_');
                var dt = System.DateTime.TryParseExact(parts[^1], TimestampFormat,
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
                    ? parsed : (System.DateTime?)null;

                var rawBytes = File.ReadAllBytes(path);
                var contentHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(rawBytes));
                var size = GZip.Decompress(rawBytes).Length;

                return new BackupEntry(path, dt, size, contentHash);
            }
        }
    }
}
