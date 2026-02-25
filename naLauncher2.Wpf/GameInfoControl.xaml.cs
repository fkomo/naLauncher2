using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
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
        public const double GlassOverlayDuration = 150;
        public const double OnHoverScale = 1.05;
        public const double SummaryScrollSpeed = 10.0; // pixels per second
        public const double SummaryScrollDelay = 2000.0; // milliseconds before scroll starts

        readonly int _originalZIndex;
        readonly Brush _originalBorderBrush;

        DispatcherTimer? _summaryDelayTimer;
        DispatcherTimer? _summaryScrollTimer;
        DateTime _lastScrollTick;
        readonly Brush _originalNameLabelForeground;
        readonly Effect _originalNameLabelEffect;
        readonly bool _hasInfoOverlay;

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

            if (!string.IsNullOrEmpty(game.Developer))
                DeveloperText.Text = game.Developer;
            else
                DeveloperText.Visibility = Visibility.Collapsed;

            if (game.Genres?.Length > 0)
                GenresText.Text = string.Join(" | ", game.Genres);
            else
                GenresText.Visibility = Visibility.Collapsed;

            if (!string.IsNullOrEmpty(game.Summary))
                SummaryText.Text = game.Summary;
            else
                SummaryScroller.Visibility = Visibility.Collapsed;

            _hasInfoOverlay = !string.IsNullOrEmpty(game.Developer)
                || (game.Genres?.Length > 0)
                || !string.IsNullOrEmpty(game.Summary);

            LoadImageAsync(game.ImagePath, game.Installed);
        }

        void NameLabel_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (!_hasInfoOverlay)
                return;

            var dur = new Duration(TimeSpan.FromMilliseconds(GlassOverlayDuration));
            InfoOverlay.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(InfoOverlay.Opacity, 1, dur));
            StartSummaryScroll();
        }

        void NameLabel_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (!_hasInfoOverlay)
                return;

            var dur = new Duration(TimeSpan.FromMilliseconds(GlassOverlayDuration));
            InfoOverlay.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(InfoOverlay.Opacity, 0, dur));
            StopSummaryScroll();
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
            var dur = new Duration(TimeSpan.FromMilliseconds(GlassOverlayDuration));
            GlassOverlay.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(GlassOverlay.Opacity, 1, dur));

            // Subtle scale-up lift
            HoverScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(HoverScale.ScaleX, OnHoverScale, dur));
            HoverScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(HoverScale.ScaleY, OnHoverScale, dur));
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
            var dur = new Duration(TimeSpan.FromMilliseconds(GlassOverlayDuration));
            GlassOverlay.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(GlassOverlay.Opacity, 0, dur));

            // Scale back to normal
            HoverScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(HoverScale.ScaleX, 1, dur));
            HoverScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(HoverScale.ScaleY, 1, dur));

            StopSummaryScroll();
        }

        void StartSummaryScroll()
        {
            StopSummaryScroll();

            if (SummaryScroller.Visibility != Visibility.Visible)
                return;

            SummaryScroller.ScrollToTop();

            if (SummaryScroller.ScrollableHeight <= 0)
                return;

            _summaryDelayTimer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(SummaryScrollDelay)
            };
            _summaryDelayTimer.Tick += SummaryDelayTimer_Tick;
            _summaryDelayTimer.Start();
        }

        void SummaryDelayTimer_Tick(object? sender, EventArgs e)
        {
            _summaryDelayTimer!.Stop();
            _summaryDelayTimer.Tick -= SummaryDelayTimer_Tick;
            _summaryDelayTimer = null;

            _lastScrollTick = DateTime.UtcNow;
            _summaryScrollTimer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(16)
            };
            _summaryScrollTimer.Tick += SummaryScrollTimer_Tick;
            _summaryScrollTimer.Start();
        }

        void StopSummaryScroll()
        {
            if (_summaryDelayTimer is not null)
            {
                _summaryDelayTimer.Stop();
                _summaryDelayTimer.Tick -= SummaryDelayTimer_Tick;
                _summaryDelayTimer = null;
            }

            if (_summaryScrollTimer is null)
                return;

            _summaryScrollTimer.Stop();
            _summaryScrollTimer.Tick -= SummaryScrollTimer_Tick;
            _summaryScrollTimer = null;
        }

        void SummaryScrollTimer_Tick(object? sender, EventArgs e)
        {
            var now = DateTime.UtcNow;
            var delta = (now - _lastScrollTick).TotalSeconds;
            _lastScrollTick = now;

            var newOffset = SummaryScroller.VerticalOffset + SummaryScrollSpeed * delta;
            if (newOffset >= SummaryScroller.ScrollableHeight)
            {
                SummaryScroller.ScrollToBottom();
                StopSummaryScroll();
                return;
            }

            SummaryScroller.ScrollToVerticalOffset(newOffset);
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

        static BitmapImage ImageNotFound() => LoadImageBitmap(Path.Combine(AppContext.BaseDirectory, "Assets", "image-not-found.png"))!;

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
