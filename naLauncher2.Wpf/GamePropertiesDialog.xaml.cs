using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace naLauncher2.Wpf
{
    public partial class GamePropertiesDialog : Window
    {
        readonly string _originalId;

        public string NewId => TitleBox.Text.Trim();
        public GameInfo Game { get; }

        public GamePropertiesDialog(string id, GameInfo game)
        {
            _originalId = id;
            Game = game;

            InitializeComponent();

            TitleBox.Text = id;
            DeveloperBox.Text = game.Developer ?? string.Empty;
            GenresBox.Text = game.Genres.Length > 0 ? string.Join(", ", game.Genres) : string.Empty;
            RatingBox.Text = game.Rating.HasValue ? game.Rating.Value.ToString() : string.Empty;
            SummaryBox.Text = game.Summary ?? string.Empty;
            ImagePathBox.Text = game.ImagePath ?? string.Empty;
            ShortcutText.Text = game.Shortcut ?? "(not installed)";
            AddedText.Text = game.Added.ToString("yyyy-MM-dd HH:mm");
            CompletedText.Text = game.Completed.HasValue
                ? game.Completed.Value.ToString("yyyy-MM-dd HH:mm")
                : "(not completed)";
            PlayedText.Text = game.Played.Count > 0
                ? $"{game.Played.Count} session{(game.Played.Count != 1 ? "s" : "")}, last played {game.LastPlayed!.Value:yyyy-MM-dd HH:mm}"
                : "(never played)";

            if (game.Extensions.Count > 0)
            {
                ExtensionsLabel.Visibility = Visibility.Visible;
                ExtensionsList.Visibility = Visibility.Visible;
                ExtensionsList.ItemsSource = game.Extensions;
            }

            Loaded += (_, _) => { TitleBox.Focus(); TitleBox.SelectAll(); };
        }

        void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();

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

        void Save_Click(object sender, MouseButtonEventArgs e)
        {
            ApplyChanges();
            DialogResult = true;
        }

        void Cancel_Click(object sender, MouseButtonEventArgs e) => DialogResult = false;

        void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if (FocusManager.GetFocusedElement(this) is TextBox tb && tb.AcceptsReturn)
                    return;
                ApplyChanges();
                DialogResult = true;
            }
            else if (e.Key == Key.Escape)
            {
                DialogResult = false;
            }
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
        }
    }
}
