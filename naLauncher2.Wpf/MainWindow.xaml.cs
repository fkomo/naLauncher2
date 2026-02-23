using naLauncher2.Wpf.Common;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace naLauncher2.Wpf
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        double _scrollOffsetY = 0;
        double _scrollVelocity = 0;
        bool _scrollAnimating = false;

        readonly TranslateTransform _scrollTransform = new();
        GameInfoControl[] _visibleControls = [];
        double _naturalMaxBottom = 0;

        const double ScrollFriction = 0.88;
        const double ScrollImpulse = 20;
        const double ScrollMaxVelocity = 400;

        double _gridOffset;

        public MainWindow()
        {
            InitializeComponent();
            GameControlsContainer.RenderTransform = _scrollTransform;
        }

        async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            await GameLibrary.Instance.Load(@"c:\Users\filip\AppData\Roaming\Ujeby\naLauncher2\library.json");

            AddGameLibraryControls();

            // Arrange controls after canvas is laid out
            ArrangeControlsInGrid(GameLibrary.Instance.Games.Keys.Order(), animate: 0);
        }

        void AddGameLibraryControls()
        {
            using var tb = new TimedBlock($"{nameof(MainWindow)}.{nameof(AddGameLibraryControls)}()");

            var _random = new Random();

            foreach (var title in GameLibrary.Instance.Games.Keys)
            {
                var control = new GameInfoControl(title);
                control.CacheMode = new BitmapCache();

                // Get screen dimensions
                double screenWidth = SystemParameters.PrimaryScreenWidth;
                double screenHeight = SystemParameters.PrimaryScreenHeight;

                // Calculate random position ensuring control stays within screen bounds
                double left = _random.NextDouble() * (screenWidth - control.Width);
                double top = _random.NextDouble() * (screenHeight - control.Height);

                // Add control to inner canvas
                GameControlsContainer.Children.Add(control);
                Canvas.SetLeft(control, left);
                Canvas.SetTop(control, top);
            }
        }

        void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
                this.Close();
        }

        void ArrangeControlsInGrid(IEnumerable<string> controlIds, double gap = 16, int animate = 1000)
        {
            using var tb = new TimedBlock($"{nameof(MainWindow)}.{nameof(ArrangeControlsInGrid)}()");

            // Use actual canvas dimensions instead of SystemParameters which may differ due to DPI/scaling
            double screenWidth = ControlsCanvas.ActualWidth;
            double screenHeight = ControlsCanvas.ActualHeight;

            // Calculate how many controls fit horizontally
            int controlsPerRow = (int)((screenWidth + gap) / (GameInfoControl.ControlWidth + gap));

            // Calculate total width needed for one row of controls
            double totalWidth = (controlsPerRow * GameInfoControl.ControlWidth) + ((controlsPerRow - 1) * gap);

            // Calculate horizontal offset to center the grid, distributing remaining space equally
            double totalRemainingSpace = screenWidth - totalWidth;
            _gridOffset = totalRemainingSpace / 2;

            int index = 0;
            var duration = TimeSpan.FromMilliseconds(animate);
            var easing = new CubicEase { EasingMode = EasingMode.EaseOut };

            _scrollOffsetY = 0;
            _scrollVelocity = 0;
            _scrollTransform.Y = 0;
            if (_scrollAnimating)
            {
                CompositionTarget.Rendering -= OnScrollRendering;
                _scrollAnimating = false;
            }

            HideAllGameControls();

            foreach (var control in controlIds.Select(FindControl).Where(x => x is not null))
            {
                control!.Visibility = Visibility.Visible;
                
                int row = index / controlsPerRow;
                int col = index % controlsPerRow;

                double newLeft = _gridOffset + (col * (GameInfoControl.ControlWidth + gap));
                double newTop = _gridOffset + row * (GameInfoControl.ControlHeight + gap);

                if (animate > 0)
                {
                    // Get current position
                    double currentLeft = Canvas.GetLeft(control);
                    double currentTop = Canvas.GetTop(control);

                    // Calculate offset from current to new position
                    double offsetX = currentLeft - newLeft;
                    double offsetY = currentTop - newTop;

                    // Set render transform if not already set
                    if (control!.RenderTransform is not TranslateTransform)
                    {
                        control.RenderTransform = new TranslateTransform(offsetX, offsetY);
                        control.RenderTransformOrigin = new Point(0, 0);
                    }

                    // Set final position
                    Canvas.SetLeft(control, newLeft);
                    Canvas.SetTop(control, newTop);

                    // Animate from current offset to (0, 0)
                    var translateTransform = (TranslateTransform)control.RenderTransform;
                    var xAnimation = new DoubleAnimation(offsetX, 0, duration) { EasingFunction = easing };
                    var yAnimation = new DoubleAnimation(offsetY, 0, duration) { EasingFunction = easing };

                    translateTransform.BeginAnimation(TranslateTransform.XProperty, xAnimation);
                    translateTransform.BeginAnimation(TranslateTransform.YProperty, yAnimation);
                }
                else
                {
                    Canvas.SetLeft(control, newLeft);
                    Canvas.SetTop(control, newTop);
                }

                index++;
            }

            _visibleControls = GameInfoControls.Where(c => c.Visibility == Visibility.Visible).ToArray();
            _naturalMaxBottom = _visibleControls.Length > 0
                ? _visibleControls.Max(c => Canvas.GetTop(c) + GameInfoControl.ControlHeight)
                : 0;

            UpdateViewportCulling();
        }

        GameInfoControl? FindControl(string id) => GameInfoControls.FirstOrDefault(c => c.Id == id);

        IEnumerable<GameInfoControl> GameInfoControls => GameControlsContainer.Children.OfType<GameInfoControl>();

        void HideAllGameControls()
        {
            foreach (var control in GameInfoControls)
                control.Visibility = Visibility.Collapsed;
        }

        void UpdateViewportCulling()
        {
            if (_visibleControls.Length == 0 || ControlsCanvas.ActualHeight == 0)
                return;

            double viewportTop = _scrollOffsetY;
            double viewportBottom = _scrollOffsetY + ControlsCanvas.ActualHeight;
            double buffer = GameInfoControl.ControlHeight;

            foreach (var control in _visibleControls)
            {
                double top = Canvas.GetTop(control);
                bool inViewport = top + GameInfoControl.ControlHeight + buffer > viewportTop
                               && top - buffer < viewportBottom;

                var target = inViewport ? Visibility.Visible : Visibility.Collapsed;
                if (control.Visibility != target)
                    control.Visibility = target;
            }
        }

        void ControlsCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            _scrollVelocity = Math.Clamp(
                _scrollVelocity + (-e.Delta / 120.0 * ScrollImpulse),
                -ScrollMaxVelocity, ScrollMaxVelocity);

            if (!_scrollAnimating)
            {
                _scrollAnimating = true;
                CompositionTarget.Rendering += OnScrollRendering;
            }

            e.Handled = true;
        }

        void OnScrollRendering(object? sender, EventArgs e)
        {
            if (Math.Abs(_scrollVelocity) < 0.5 || _visibleControls.Length == 0)
            {
                _scrollVelocity = 0;
                CompositionTarget.Rendering -= OnScrollRendering;
                _scrollAnimating = false;
                return;
            }

            double maxScrollOffset = Math.Max(0, _naturalMaxBottom - ControlsCanvas.ActualHeight + _gridOffset);

            if (maxScrollOffset <= 0)
            {
                _scrollVelocity = 0;
                CompositionTarget.Rendering -= OnScrollRendering;
                _scrollAnimating = false;
                return;
            }

            double newOffset = Math.Clamp(_scrollOffsetY + _scrollVelocity, 0, maxScrollOffset);

            if (newOffset <= 0 || newOffset >= maxScrollOffset)
                _scrollVelocity = 0;

            _scrollOffsetY = newOffset;
            _scrollVelocity *= ScrollFriction;
            _scrollTransform.Y = -_scrollOffsetY;

            UpdateViewportCulling();
        }
    }
}