using naLauncher2.Wpf.Common;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace naLauncher2.Wpf
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        const double ScrollFriction = 0.88;
        const double ScrollImpulse = 20;
        const double ScrollMaxVelocity = 400;
        const double Gap = 16;
        const double SectionGap = 32;

        // horizontal scroll — New Games
        readonly TranslateTransform _newGamesTransform = new();
        double _newGamesOffsetX, _newGamesVelocityX, _newGamesMaxScrollX;

        // horizontal scroll — Recent Games
        readonly TranslateTransform _lastPlayedTransform = new();
        double _lastPlayedOffsetX, _lastPlayedVelocityX, _recentGamesMaxScrollX;

        // vertical scroll — User Games
        readonly TranslateTransform _allGamesTransform = new();
        double _allGamesOffsetY, _allGamesVelocityY, _userGamesMaxScrollY;
        (GameInfoControl Control, double LocalTop)[] _visibleControls = [];

        bool _scrollAnimating = false;
        int _controlsPerRow;
        double _gridOffset;
        UserGamesFilterMode _userGamesFilterMode = UserGamesFilterMode.Installed;
        DateTime _userGamesDropdownLastClosed = DateTime.MinValue;
        bool _newGamesCollapsed = false;
        bool _recentGamesCollapsed = false;
        bool _userGamesCollapsed = false;

        public MainWindow()
        {
            InitializeComponent();
            NewGamesContainer.RenderTransform = _newGamesTransform;
            RecentGamesContainer.RenderTransform = _lastPlayedTransform;
            UserGamesContainer.RenderTransform = _allGamesTransform;
        }

        async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            await GameLibrary.Instance.Load(@"c:\Users\filip\AppData\Roaming\Ujeby\naLauncher2\library.json");

            double screenWidth = RootGrid.ActualWidth;
            _controlsPerRow = (int)((screenWidth + Gap) / (GameInfoControl.ControlWidth + Gap));

            double totalWidth = _controlsPerRow * GameInfoControl.ControlWidth + (_controlsPerRow - 1) * Gap;
            _gridOffset = (screenWidth - totalWidth) / 2;

            var newGames = GameLibrary.Instance.NewGamesIds();
            var recentGames = GameLibrary.Instance.RecentGamesIds();
            var userGames = GameLibrary.Instance.InstalledGamesIds();

            PopulateHorizontalSection(NewGamesContainer, newGames);
            PopulateHorizontalSection(RecentGamesContainer, recentGames);
            PopulateGridSection(UserGamesContainer, userGames);

            NewGamesCount.Text = $"({newGames.Length})";
            RecentGamesCount.Text = $"({recentGames.Length})";
            UserGamesCount.Text = $"({userGames.Length})";

            double shadowOffset = GameInfoControl.ShadowBlurRadius;
            double canvasTopMargin = SectionGap - shadowOffset;
            NewGamesCanvas.Margin = new Thickness(0, canvasTopMargin, 0, 0);
            RecentGamesCanvas.Margin = new Thickness(0, canvasTopMargin, 0, 0);
            UserGamesCanvas.Margin = new Thickness(0, canvasTopMargin, 0, 0);

            RootGrid.UpdateLayout();

            double HorizontalContentWidth(int n) => n > 0 ? 2 * _gridOffset + n * (GameInfoControl.ControlWidth + Gap) - Gap : 0;

            _newGamesMaxScrollX = Math.Max(0, HorizontalContentWidth(newGames.Length) - screenWidth);
            _recentGamesMaxScrollX = Math.Max(0, HorizontalContentWidth(recentGames.Length) - screenWidth);

            _userGamesMaxScrollY = Math.Max(0, GridContentHeight(userGames.Length) - UserGamesCanvas.ActualHeight + _gridOffset);

            _visibleControls = UserGamesContainer.Children.OfType<GameInfoControl>()
                .Select(c => (Control: c, LocalTop: Canvas.GetTop(c)))
                .ToArray();

            UpdateViewportCulling();
            UpdateScrollThumbs();
        }

        double GridContentHeight(int count)
        {
            if (count == 0) return 0;
            int rows = (int)Math.Ceiling((double)count / _controlsPerRow);
            return GameInfoControl.ShadowBlurRadius + rows * GameInfoControl.ControlHeight + (rows - 1) * Gap + Gap;
        }

        void PopulateHorizontalSection(Canvas container, string[] gameIds)
        {
            using var tb = new TimedBlock($"{nameof(MainWindow)}.{nameof(PopulateHorizontalSection)}({gameIds.Length} games)");

            for (int i = 0; i < gameIds.Length; i++)
            {
                var control = new GameInfoControl(gameIds[i]) { CacheMode = new BitmapCache() };
                container.Children.Add(control);
                Canvas.SetLeft(control, _gridOffset + i * (GameInfoControl.ControlWidth + Gap));
                Canvas.SetTop(control, GameInfoControl.ShadowBlurRadius);
            }
        }

        void PopulateGridSection(Canvas container, string[] gameIds)
        {
            using var tb = new TimedBlock($"{nameof(MainWindow)}.{nameof(PopulateGridSection)}({gameIds.Length} games)");

            for (int i = 0; i < gameIds.Length; i++)
            {
                var control = new GameInfoControl(gameIds[i]) { CacheMode = new BitmapCache() };
                container.Children.Add(control);
                Canvas.SetLeft(control, _gridOffset + (i % _controlsPerRow) * (GameInfoControl.ControlWidth + Gap));
                Canvas.SetTop(control, GameInfoControl.ShadowBlurRadius + (i / _controlsPerRow) * (GameInfoControl.ControlHeight + Gap));
            }
        }

        void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
                this.Close();
        }

        void UpdateViewportCulling()
        {
            if (_visibleControls.Length == 0 || UserGamesCanvas.ActualHeight == 0)
                return;

            double viewportTop = _allGamesOffsetY;
            double viewportBottom = _allGamesOffsetY + UserGamesCanvas.ActualHeight;
            double buffer = GameInfoControl.ControlHeight;

            foreach (var (control, localTop) in _visibleControls)
            {
                bool inViewport = localTop + GameInfoControl.ControlHeight + buffer > viewportTop
                               && localTop - buffer < viewportBottom;

                var target = inViewport ? Visibility.Visible : Visibility.Collapsed;
                if (control.Visibility != target)
                    control.Visibility = target;
            }
        }

        void HorizontalCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            double impulse = -e.Delta / 120.0 * ScrollImpulse;

            if (sender == NewGamesCanvas && _newGamesMaxScrollX > 0)
                _newGamesVelocityX = Math.Clamp(_newGamesVelocityX + impulse, -ScrollMaxVelocity, ScrollMaxVelocity);
            else if (sender == RecentGamesCanvas && _recentGamesMaxScrollX > 0)
                _lastPlayedVelocityX = Math.Clamp(_lastPlayedVelocityX + impulse, -ScrollMaxVelocity, ScrollMaxVelocity);

            if (!_scrollAnimating)
            {
                _scrollAnimating = true;
                CompositionTarget.Rendering += OnScrollRendering;
            }

            e.Handled = true;
        }

        void VerticalCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (_userGamesMaxScrollY > 0)
                _allGamesVelocityY = Math.Clamp(_allGamesVelocityY + (-e.Delta / 120.0 * ScrollImpulse), -ScrollMaxVelocity, ScrollMaxVelocity);

            if (!_scrollAnimating)
            {
                _scrollAnimating = true;
                CompositionTarget.Rendering += OnScrollRendering;
            }

            e.Handled = true;
        }

        void OnScrollRendering(object? sender, EventArgs e)
        {
            bool anyActive = false;

            if (Math.Abs(_newGamesVelocityX) >= 0.5)
            {
                anyActive = true;
                double next = Math.Clamp(_newGamesOffsetX + _newGamesVelocityX, 0, _newGamesMaxScrollX);
                if (next <= 0 || next >= _newGamesMaxScrollX) _newGamesVelocityX = 0;
                _newGamesOffsetX = next;
                _newGamesVelocityX *= ScrollFriction;
                _newGamesTransform.X = -_newGamesOffsetX;
            }
            else _newGamesVelocityX = 0;

            if (Math.Abs(_lastPlayedVelocityX) >= 0.5)
            {
                anyActive = true;
                double next = Math.Clamp(_lastPlayedOffsetX + _lastPlayedVelocityX, 0, _recentGamesMaxScrollX);
                if (next <= 0 || next >= _recentGamesMaxScrollX) _lastPlayedVelocityX = 0;
                _lastPlayedOffsetX = next;
                _lastPlayedVelocityX *= ScrollFriction;
                _lastPlayedTransform.X = -_lastPlayedOffsetX;
            }
            else _lastPlayedVelocityX = 0;

            if (Math.Abs(_allGamesVelocityY) >= 0.5)
            {
                anyActive = true;
                double next = Math.Clamp(_allGamesOffsetY + _allGamesVelocityY, 0, _userGamesMaxScrollY);

                if (next <= 0 || next >= _userGamesMaxScrollY) _allGamesVelocityY = 0;

                _allGamesOffsetY = next;
                _allGamesVelocityY *= ScrollFriction;
                _allGamesTransform.Y = -_allGamesOffsetY;

                UpdateViewportCulling();
            }
            else _allGamesVelocityY = 0;

            if (!anyActive)
            {
                _scrollAnimating = false;
                CompositionTarget.Rendering -= OnScrollRendering;
            }

            UpdateScrollThumbs();
        }

        void UpdateScrollThumbs()
        {
            UpdateScrollThumb(NewGamesScrollThumb, NewGamesScrollTrack, _newGamesOffsetX, _newGamesMaxScrollX, NewGamesCanvas.ActualWidth);
            UpdateScrollThumb(RecentGamesScrollThumb, RecentGamesScrollTrack, _lastPlayedOffsetX, _recentGamesMaxScrollX, RecentGamesCanvas.ActualWidth);
            UpdateScrollThumb(UserGamesScrollThumb, UserGamesScrollTrack, _allGamesOffsetY, _userGamesMaxScrollY, UserGamesCanvas.ActualHeight);
        }

        static void UpdateScrollThumb(Rectangle thumb, Rectangle track, double offset, double maxScroll, double viewport)
        {
            double trackWidth = track.ActualWidth;
            if (trackWidth <= 0 || maxScroll <= 0)
            {
                thumb.Width = 0;
                return;
            }
            double thumbWidth = Math.Max(8, trackWidth * viewport / (viewport + maxScroll));
            double thumbLeft = (offset / maxScroll) * (trackWidth - thumbWidth);
            thumb.Width = thumbWidth;
            thumb.Margin = new Thickness(thumbLeft, 3, 0, 0);
        }

        void UserGamesLabel_Click(object sender, MouseButtonEventArgs e)
        {
            if ((DateTime.UtcNow - _userGamesDropdownLastClosed).TotalMilliseconds > 300)
            {
                UpdateFilterOptionHighlight();
                UserGamesDropdown.IsOpen = true;
            }
        }

        void UserGamesDropdown_Closed(object? sender, EventArgs e)
        {
            _userGamesDropdownLastClosed = DateTime.UtcNow;
        }

        void UserGamesFilter_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is TextBlock tb && tb.Tag is string tag && Enum.TryParse<UserGamesFilterMode>(tag, out var mode))
            {
                _userGamesFilterMode = mode;
                UserGamesDropdown.IsOpen = false;
                RefreshUserGames();
            }
        }

        void UpdateFilterOptionHighlight()
        {
            FilterOptionInstalled.Foreground = _userGamesFilterMode == UserGamesFilterMode.Installed ? Brushes.LightSkyBlue : Brushes.White;
            FilterOptionRemoved.Foreground = _userGamesFilterMode == UserGamesFilterMode.Removed ? Brushes.LightSkyBlue : Brushes.White;
            FilterOptionFinished.Foreground = _userGamesFilterMode == UserGamesFilterMode.Finished ? Brushes.LightSkyBlue : Brushes.White;
            FilterOptionUnfinished.Foreground = _userGamesFilterMode == UserGamesFilterMode.Unfinished ? Brushes.LightSkyBlue : Brushes.White;
            FilterOptionAll.Foreground = _userGamesFilterMode == UserGamesFilterMode.All ? Brushes.LightSkyBlue : Brushes.White;
        }

        void RefreshUserGames()
        {
            var all = GameLibrary.Instance.Games.AsEnumerable();
            var gameIds = (_userGamesFilterMode switch
            {
                UserGamesFilterMode.Removed => all.Where(x => !x.Value.Installed),
                UserGamesFilterMode.Finished => all.Where(x => x.Value.Finished),
                UserGamesFilterMode.Unfinished => all.Where(x => x.Value.Installed && !x.Value.Finished),
                UserGamesFilterMode.All => all,
                _ => all.Where(x => x.Value.Installed),
            })
                .Select(x => x.Key)
                .Order()
                .ToArray();

            UserGamesLabel.Text = _userGamesFilterMode.ToString();
            UserGamesContainer.Children.Clear();
            PopulateGridSection(UserGamesContainer, gameIds);
            UserGamesCount.Text = $"({gameIds.Length})";

            _userGamesMaxScrollY = Math.Max(0, GridContentHeight(gameIds.Length) - UserGamesCanvas.ActualHeight + _gridOffset);
            _allGamesOffsetY = 0;
            _allGamesVelocityY = 0;
            _allGamesTransform.Y = 0;

            _visibleControls = UserGamesContainer.Children.OfType<GameInfoControl>()
                .Select(c => (Control: c, LocalTop: Canvas.GetTop(c)))
                .ToArray();

            UpdateViewportCulling();
            UpdateScrollThumbs();
        }

        void NewGamesToggle_Click(object sender, MouseButtonEventArgs e)
        {
            _newGamesCollapsed = !_newGamesCollapsed;
            NewGamesToggle.Text = _newGamesCollapsed ? "\u25B6" : "\u25BC";
            NewGamesCanvas.Visibility = _newGamesCollapsed ? Visibility.Collapsed : Visibility.Visible;
            NewGamesScrollTrack.Visibility = _newGamesCollapsed ? Visibility.Collapsed : Visibility.Visible;
            NewGamesScrollThumb.Visibility = _newGamesCollapsed ? Visibility.Collapsed : Visibility.Visible;
            NewGamesDivider.Visibility = _newGamesCollapsed ? Visibility.Visible : Visibility.Collapsed;
        }

        void RecentGamesToggle_Click(object sender, MouseButtonEventArgs e)
        {
            _recentGamesCollapsed = !_recentGamesCollapsed;
            RecentGamesToggle.Text = _recentGamesCollapsed ? "\u25B6" : "\u25BC";
            RecentGamesCanvas.Visibility = _recentGamesCollapsed ? Visibility.Collapsed : Visibility.Visible;
            RecentGamesScrollTrack.Visibility = _recentGamesCollapsed ? Visibility.Collapsed : Visibility.Visible;
            RecentGamesScrollThumb.Visibility = _recentGamesCollapsed ? Visibility.Collapsed : Visibility.Visible;
            RecentGamesDivider.Visibility = _recentGamesCollapsed ? Visibility.Visible : Visibility.Collapsed;
        }

        void UserGamesToggle_Click(object sender, MouseButtonEventArgs e)
        {
            _userGamesCollapsed = !_userGamesCollapsed;
            UserGamesToggle.Text = _userGamesCollapsed ? "\u25B6" : "\u25BC";
            UserGamesCanvas.Visibility = _userGamesCollapsed ? Visibility.Collapsed : Visibility.Visible;
            UserGamesCanvasRow.Height = _userGamesCollapsed ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
            UserGamesScrollTrack.Visibility = _userGamesCollapsed ? Visibility.Collapsed : Visibility.Visible;
            UserGamesScrollThumb.Visibility = _userGamesCollapsed ? Visibility.Collapsed : Visibility.Visible;
            UserGamesDivider.Visibility = _userGamesCollapsed ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
