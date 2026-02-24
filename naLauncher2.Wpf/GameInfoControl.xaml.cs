using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace naLauncher2.Wpf
{
    /// <summary>
    /// Interaction logic for GameInfoControl.xaml
    /// </summary>
    public partial class GameInfoControl : UserControl
    {
        public const double ControlWidth = 460;
        public const double ControlHeight = 260;
        public const double ShadowBlurRadius = 16;

        readonly int _originalZIndex;
        readonly Brush _originalBorderBrush;
        readonly Brush _originalNameLabelForeground;
        readonly Effect _originalNameLabelEffect;

        readonly string _id;
        public string Id => _id;

        public GameInfoControl(string id)
        {
            _id = id;

            InitializeComponent();

            // Store original colors and z-index for restoration
            _originalBorderBrush = MainBorder.BorderBrush;
            _originalNameLabelForeground = NameLabel.Foreground;
            _originalNameLabelEffect = NameLabel.Effect;
            _originalZIndex = 0;

            NameLabel.Text = _id;

            var game = GameLibrary.Instance.Games[_id];

            LoadImageAsync(game.ImagePath, game.Installed);
        }

        void UserControl_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            // Change cursor to hand
            this.Cursor = System.Windows.Input.Cursors.Hand;

            // Bring control to front by setting highest z-index
            Canvas.SetZIndex(this, 1000);

            // Highlight border color (keep thickness same to prevent image movement)
            MainBorder.BorderBrush = new SolidColorBrush(Colors.LightSkyBlue);

            // Highlight name label - white text with light sky blue glow
            NameLabel.Foreground = new SolidColorBrush(Colors.White);
            NameLabel.Effect = new DropShadowEffect
            {
                Color = Colors.LightSkyBlue,
                BlurRadius = 1,
                ShadowDepth = 0
            };

            // Glass sheen fade in
            var dur = new Duration(TimeSpan.FromMilliseconds(150));
            GlassOverlay.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(GlassOverlay.Opacity, 1, dur));

            // Subtle scale-up lift
            HoverScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(HoverScale.ScaleX, 1.03, dur));
            HoverScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(HoverScale.ScaleY, 1.03, dur));
        }

        void UserControl_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            // Restore cursor to arrow
            this.Cursor = System.Windows.Input.Cursors.Arrow;

            // Restore original z-index
            Canvas.SetZIndex(this, _originalZIndex);

            // Restore original border color
            MainBorder.BorderBrush = _originalBorderBrush;

            // Restore original name label color and effect
            NameLabel.Foreground = _originalNameLabelForeground;
            NameLabel.Effect = _originalNameLabelEffect;

            // Glass sheen fade out
            var dur = new Duration(TimeSpan.FromMilliseconds(200));
            GlassOverlay.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(GlassOverlay.Opacity, 0, dur));

            // Scale back to normal
            HoverScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(HoverScale.ScaleX, 1, dur));
            HoverScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(HoverScale.ScaleY, 1, dur));
        }

        async void LoadImageAsync(string? imagePath, bool isInstalled)
        {
            try
            {
                // Load image on background thread to avoid blocking UI
                var bitmap = await Task.Run(() => LoadImageBitmap(imagePath) ?? ImageNotFound()!);

                // Update UI on main thread
                ControlImage.Source = isInstalled
                    ? (BitmapSource)bitmap
                    : new FormatConvertedBitmap(bitmap, PixelFormats.Gray8, null, 0);
                LoadingText.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                LoadingText.Text = "Error loading image";

                Debug.WriteLine($"Error loading image: {ex}");
            }
        }

        static BitmapImage ImageNotFound() => LoadImageBitmap(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "image-not-found.png"))!;

        static BitmapImage? LoadImageBitmap(string? imagePath)
        {
            if (imagePath is null || !File.Exists(imagePath))
                return null;

            try
            {
                var bitmapImage = new BitmapImage();

                bitmapImage.BeginInit();
                bitmapImage.UriSource = new Uri(imagePath, UriKind.Absolute);
                bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                bitmapImage.EndInit();
                bitmapImage.Freeze();

                return bitmapImage;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading image from path '{imagePath}': {ex}");
            }

            return null;
        }

        static Color GetMetacriticColor(int score)
        {
            if (score >= 75)
                return Color.FromArgb(0xff, 0x66, 0xcc, 0x33);

            else if (score < 50)
                return Color.FromArgb(0xff, 0xff, 0x00, 0x00);

            else
                return Color.FromArgb(0xff, 0xff, 0xcc, 0x33);
        }
    }
}
