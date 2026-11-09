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
        readonly string _originalInstallDir;

        List<SessionRow> _sessions;

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
            _originalInstallDir = game.InstallDirIsManual ? game.InstallDir ?? string.Empty : string.Empty;
            _sessions = game.Played
                .Select(s => new SessionRow { Original = s, Start = s.Start, Duration = s.Duration, Running = SessionTracker.Instance.IsTracked(s) })
                .ToList();

            InitializeComponent();

            // the dialog grows with its content up to the screen height, then its fields scroll
            MaxHeight = SystemParameters.WorkArea.Height - 40;

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

            UpdatePlayTimeText();
            UpdateSessionsButton();

            InstallDirBox.Text = _originalInstallDir;
            UpdateInstallDirLabel();
            InstallDirBox.TextChanged += (_, _) => { UpdateInstallDirLabel(); UpdateSaveButton(); };

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

        /// <summary>
        /// Total playtime, and where it comes from. The launcher's figure and naLauncher's own tracking
        /// overlap for sessions that went through the launcher; the total counts those only once.
        /// </summary>
        void UpdatePlayTimeText()
        {
            PlayTimeText.Text = Game.TotalPlayTime is TimeSpan total ? PlayTimeFormat.Format(total) : "(unknown)";

            var parts = new List<string>();

            if (Game.LauncherPlayTime is LauncherPlayTime launcher)
                parts.Add($"{launcher.Source} reports {PlayTimeFormat.Format(launcher.Total)} (last changed {launcher.ReadAt:yyyy-MM-dd HH:mm})");

            int measured = Game.Played.Count(s => s.Measured);
            if (measured > 0)
                parts.Add($"naLauncher tracked {PlayTimeFormat.Format(Game.TrackedPlayTime)} in {measured} session{(measured != 1 ? "s" : "")}");

            int unmeasured = Game.Played.Count(s => !s.Measured && !SessionTracker.Instance.IsTracked(s));
            if (unmeasured > 0)
                parts.Add($"{unmeasured} launch{(unmeasured != 1 ? "es" : "")} without a measured duration");

            PlayTimeDetailText.Text = parts.Count > 0 ? string.Join("\n", parts) : "(never played)";
        }

        void UpdateSessionsButton()
        {
            int count = _sessions.Count(r => !r.Deleted);
            SessionsButton.Text = $"Sessions ({count}){(SessionsChanged() ? " – edited" : string.Empty)}…";
        }

        void Sessions_Click(object sender, MouseButtonEventArgs e)
        {
            var dialog = new SessionsDialog(TitleBox.Text.Trim(), _sessions) { Owner = this };
            if (dialog.ShowDialog() != true)
                return;

            _sessions = dialog.Rows;
            UpdateSessionsButton();
            UpdateSaveButton();
        }

        bool SessionsChanged() => _sessions.Any(r =>
            r.Original is null ? !r.Deleted : r.Deleted || r.Duration != r.Original.Duration);

        void BrowseInstallDir_Click(object sender, MouseButtonEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Select the game's install folder" };

            var current = string.IsNullOrEmpty(InstallDirBox.Text) ? Game.InstallDir : InstallDirBox.Text;
            if (!string.IsNullOrEmpty(current) && System.IO.Directory.Exists(current))
                dialog.InitialDirectory = current;

            if (dialog.ShowDialog(this) == true)
                InstallDirBox.Text = dialog.FolderName;
        }

        void AutoInstallDir_Click(object sender, MouseButtonEventArgs e)
        {
            InstallDirBox.Text = string.Empty;
        }

        /// <summary>
        /// An empty box means "detect automatically"; the label shows what that currently resolves to.
        /// </summary>
        void UpdateInstallDirLabel()
        {
            InstallDirLabel.Text = !string.IsNullOrWhiteSpace(InstallDirBox.Text)
                ? "INSTALL FOLDER (set by hand)"
                : !Game.InstallDirIsManual && Game.InstallDir is not null
                    ? $"INSTALL FOLDER (detected: {Game.InstallDir})"
                    : "INSTALL FOLDER (not detected)";
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
            _pendingExtensions.Count != _originalExtensionsCount ||
            InstallDirBox.Text.Trim() != _originalInstallDir ||
            SessionsChanged();

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
                if (!HasChanges() || SaveButton.Visibility != Visibility.Visible)
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

            var installDir = InstallDirBox.Text.Trim();
            if (installDir != _originalInstallDir)
            {
                // empty hands the folder back to automatic detection, which fills it on the next sync
                Game.InstallDirIsManual = installDir.Length > 0;
                Game.InstallDir = installDir.Length > 0 ? installDir : null;
            }

            ApplySessionChanges();
        }

        /// <summary>
        /// Applies the session edits to the game's own <see cref="Session"/> objects rather than replacing
        /// the list, so a session the tracker is following (or ends meanwhile) is never clobbered.
        /// </summary>
        void ApplySessionChanges()
        {
            if (!SessionsChanged())
                return;

            foreach (var row in _sessions)
            {
                if (row.Original is null)
                {
                    if (!row.Deleted && row.Duration is TimeSpan duration)
                        Game.Played.Add(new Session(row.Start, row.Start + duration, duration));
                    continue;
                }

                if (row.Running || !Game.Played.Contains(row.Original))
                    continue;

                if (row.Deleted)
                    Game.Played.Remove(row.Original);
                else if (row.Duration != row.Original.Duration)
                {
                    // the end follows the edited duration; a cleared duration makes the session unmeasured
                    row.Original.Duration = row.Duration;
                    row.Original.End = row.Duration.HasValue ? row.Original.Start + row.Duration.Value : null;
                }
            }

            // sessions added by hand may be older than others; LastPlayed relies on the list being in start order
            Game.Played = [.. Game.Played.OrderBy(s => s.Start)];
        }
    }
}
