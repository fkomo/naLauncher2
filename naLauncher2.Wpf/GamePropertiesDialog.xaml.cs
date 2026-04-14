using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace naLauncher2.Wpf
{
    public partial class GamePropertiesDialog : Window
    {
        readonly string _originalId;
        readonly Dictionary<string, string> _pendingExtensions;
        readonly string _originalDeveloper;
        readonly string _originalGenres;
        readonly string _originalRating;
        readonly string _originalSummary;
        readonly string _originalImagePath;
        readonly string _originalCompleted;
        readonly string _originalReleaseDate;
        readonly int _originalExtensionsCount;

        public string NewId => TitleBox.Text.Trim();
        public GameInfo Game { get; }

        public GamePropertiesDialog(string id, GameInfo game)
        {
            _originalId = id;
            Game = game;
            _pendingExtensions = new Dictionary<string, string>(game.Extensions);
            _originalDeveloper = game.Developer ?? string.Empty;
            _originalGenres = game.Genres.Length > 0 ? string.Join(", ", game.Genres) : string.Empty;
            _originalRating = game.Rating.HasValue ? game.Rating.Value.ToString() : string.Empty;
            _originalSummary = game.Summary ?? string.Empty;
            _originalImagePath = game.ImagePath ?? string.Empty;
            _originalCompleted = game.Completed.HasValue ? game.Completed.Value.ToString("yyyy-MM-dd HH:mm") : string.Empty;
            _originalReleaseDate = game.ReleaseDate.HasValue ? game.ReleaseDate.Value.ToString("yyyy-MM-dd") : string.Empty;
            _originalExtensionsCount = game.Extensions.Count;

            InitializeComponent();

            TitleBox.Text = id;
            DeveloperBox.Text = _originalDeveloper;
            GenresBox.Text = _originalGenres;
            RatingBox.Text = _originalRating;
            SummaryBox.Text = _originalSummary;
            ImagePathBox.Text = _originalImagePath;
            ShortcutText.Text = game.Shortcut ?? "(not installed)";
            AddedText.Text = game.Added.ToString("yyyy-MM-dd HH:mm");
            CompletedBox.Text = _originalCompleted;
            ReleaseDateBox.Text = _originalReleaseDate;
            PlayedText.Text = game.Played.Count > 0
                ? $"{game.Played.Count} session{(game.Played.Count != 1 ? "s" : "")}, last played {game.LastPlayed!.Value:yyyy-MM-dd HH:mm}"
                : "(never played)";

            if (_pendingExtensions.Count > 0)
            {
                ExtensionsList.Visibility = Visibility.Visible;
                ExtensionsList.ItemsSource = _pendingExtensions;
            }

            TitleBox.TextChanged += (_, _) => UpdateSaveButton();
            DeveloperBox.TextChanged += (_, _) => UpdateSaveButton();
            GenresBox.TextChanged += (_, _) => UpdateSaveButton();
            RatingBox.TextChanged += (_, _) => UpdateSaveButton();
            SummaryBox.TextChanged += (_, _) => UpdateSaveButton();
            ImagePathBox.TextChanged += (_, _) => UpdateSaveButton();
            CompletedBox.TextChanged += (_, _) => UpdateSaveButton();
            ReleaseDateBox.TextChanged += (_, _) => UpdateSaveButton();

            ExtensionKeyBox.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter) { ExtensionValueBox.Focus(); e.Handled = true; }
            };
            ExtensionValueBox.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter) { AddExtension(); e.Handled = true; }
            };

            Loaded += (_, _) => { TitleBox.Focus(); TitleBox.SelectAll(); };
        }

        void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();

        void RemoveExtension_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement { Tag: string key })
            {
                _pendingExtensions.Remove(key);
                ExtensionsList.ItemsSource = null;
                ExtensionsList.ItemsSource = _pendingExtensions;
                if (_pendingExtensions.Count == 0)
                {
                    ExtensionsList.Visibility = Visibility.Collapsed;
                }
                UpdateSaveButton();
            }
        }

        void AddExtension_Click(object sender, MouseButtonEventArgs e) => AddExtension();

        void AddExtension()
        {
            var key = ExtensionKeyBox.Text.Trim();
            var value = ExtensionValueBox.Text.Trim();
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(value))
                return;

            _pendingExtensions[key] = value;
            ExtensionsList.ItemsSource = null;
            ExtensionsList.ItemsSource = _pendingExtensions;
            ExtensionsList.Visibility = Visibility.Visible;

            ExtensionKeyBox.Clear();
            ExtensionValueBox.Clear();
            ExtensionKeyBox.Focus();
            UpdateSaveButton();
        }

        void BrowseImagePath_Click(object sender, MouseButtonEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select game image",
                Filter = "Image files (*.jpg;*.jpeg;*.png;*.bmp;*.gif)|*.jpg;*.jpeg;*.png;*.bmp;*.gif|All files (*.*)|*.*",
                CheckFileExists = true,
            };

            if (!string.IsNullOrEmpty(ImagePathBox.Text))
                dialog.InitialDirectory = System.IO.Path.GetDirectoryName(ImagePathBox.Text);

            if (dialog.ShowDialog(this) == true)
                ImagePathBox.Text = dialog.FileName;
        }

        void ClearImagePath_Click(object sender, MouseButtonEventArgs e)
        {
            ImagePathBox.Text = string.Empty;
        }

        bool HasChanges() =>
            TitleBox.Text.Trim() != _originalId ||
            DeveloperBox.Text != _originalDeveloper ||
            GenresBox.Text != _originalGenres ||
            RatingBox.Text != _originalRating ||
            SummaryBox.Text != _originalSummary ||
            ImagePathBox.Text != _originalImagePath ||
            CompletedBox.Text != _originalCompleted ||
            ReleaseDateBox.Text != _originalReleaseDate ||
            _pendingExtensions.Count != _originalExtensionsCount;

        void UpdateSaveButton()
        {
            var hasChanges = HasChanges();
            SaveButton.Visibility = hasChanges ? Visibility.Visible : Visibility.Collapsed;
            CloseButton.Text = hasChanges ? "Cancel" : "Close";
        }

        void Save_Click(object sender, MouseButtonEventArgs e)
        {
            var confirm = new ConfirmationDialog("Save changes?") { Owner = this };
            if (confirm.ShowDialog() != true)
                return;
            ApplyChanges();
            DialogResult = true;
        }

        void Close_Click(object sender, MouseButtonEventArgs e) => DialogResult = false;

        void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if (FocusManager.GetFocusedElement(this) is TextBox tb && tb.AcceptsReturn)
                    return;
                if (!HasChanges())
                    return;
                var confirm = new ConfirmationDialog("Save changes?") { Owner = this };
                if (confirm.ShowDialog() != true)
                    return;
                ApplyChanges();
                DialogResult = true;
            }
            else if (e.Key == Key.Escape)
            {
                DialogResult = false;
            }
        }

        void SetCompletedToday_Click(object sender, MouseButtonEventArgs e)
        {
            CompletedBox.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        }

        void ClearCompleted_Click(object sender, MouseButtonEventArgs e)
        {
            CompletedBox.Text = string.Empty;
        }

        void ApplyChanges()
        {
            Game.Developer = string.IsNullOrWhiteSpace(DeveloperBox.Text) ? null : DeveloperBox.Text.Trim();
            Game.Genres = string.IsNullOrWhiteSpace(GenresBox.Text)
                ? []
                : GenresBox.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            Game.Summary = string.IsNullOrWhiteSpace(SummaryBox.Text) ? null : SummaryBox.Text.Trim();
            Game.ImagePath = string.IsNullOrWhiteSpace(ImagePathBox.Text) ? null : ImagePathBox.Text.Trim();
            Game.Rating = int.TryParse(RatingBox.Text, out int rating) && rating >= 0 && rating <= 100
                ? rating
                : null;
            Game.Completed = DateTime.TryParse(CompletedBox.Text.Trim(), out var completed)
                ? completed
                : null;
            Game.ReleaseDate = DateTime.TryParse(ReleaseDateBox.Text.Trim(), out var releaseDate)
                ? releaseDate
                : null;
            Game.Extensions = new Dictionary<string, string>(_pendingExtensions);
        }
    }
}
