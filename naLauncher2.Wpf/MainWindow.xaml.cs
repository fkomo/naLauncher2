using naLauncher2.Wpf.Common;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
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
        const double GroupFadeAnimationDuration = 150; // [in miliseconds]

        const double Gap = 16; // space between controls, both horizontally and vertically [in pixels]
        const double SectionGap = 32; // vertical space between sections [in pixels]
        const double GamePlacementDelayMs = 25; // delay between each game placement animation [in milliseconds]
        const double GamePlacementDurationMs = 200; // duration of game placement animation [in milliseconds]

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

        bool _newGamesCollapsed = true;
        bool _recentGamesCollapsed = true;
        bool _userGamesCollapsed = true;

        /// <summary>
        /// Initializes the main window and assigns render transforms to scrollable containers.
        /// </summary>
        public MainWindow()
        {
            InitializeComponent();
            NewGamesContainer.RenderTransform = _newGamesTransform;
            RecentGamesContainer.RenderTransform = _lastPlayedTransform;
            UserGamesContainer.RenderTransform = _allGamesTransform;
            ApplyNewGamesState();
            ApplyRecentGamesState();
            ApplyUserGamesState();
        }

        /// <summary>
        /// Loads the game library, calculates layout parameters, populates all sections,
        /// and initializes scroll state when the window finishes loading.
        /// </summary>
        async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            if (!await LoadLibraryAndSettings())
                return;

            _userGamesFilterMode = AppSettings.Instance.UserGamesFilterMode;

            double screenWidth = RootGrid.ActualWidth;
            _controlsPerRow = (int)((screenWidth + Gap) / (GameInfoControl.ControlWidth + Gap));

            double totalWidth = _controlsPerRow * GameInfoControl.ControlWidth + (_controlsPerRow - 1) * Gap;
            _gridOffset = (screenWidth - totalWidth) / 2;

            var newGames = GameLibrary.Instance.NewGames().ToArray();
            var recentGames = GameLibrary.Instance.RecentGames().ToArray();
            var userGames = GetUserGames();

            PopulateHorizontalSection(NewGamesContainer, newGames);
            if (newGames.Length > 0)
            {
                _newGamesCollapsed = false;
                ApplyNewGamesState();
            }

            PopulateHorizontalSection(RecentGamesContainer, recentGames);
            if (recentGames.Length > 0)
            {
                _recentGamesCollapsed = false;
                ApplyRecentGamesState();
            }

            PopulateGridSection(UserGamesContainer, userGames);
            if (userGames.Length > 0)
            {
                _userGamesCollapsed = false;
                ApplyUserGamesState();
            }

            NewGamesCount.Text = $"({newGames.Length})";
            RecentGamesCount.Text = $"({recentGames.Length})";
            UserGamesCount.Text = $"({userGames.Length})";
            UserGamesLabel.Text = _userGamesFilterMode.ToString();

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

        string[] GetUserGames()
        {
            var all = GameLibrary.Instance.Games.AsEnumerable();

            return (_userGamesFilterMode switch
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
        }

        /// <summary>
        /// Load app settings and game library from file. If the library path is not set in settings, prompts the user to select a JSON file.
        /// </summary>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        async Task<bool> LoadLibraryAndSettings()
        {
            await AppSettings.Instance.Load(System.IO.Path.Combine(AppContext.BaseDirectory, "settings.json"));

            if (AppSettings.Instance.LibraryPath is null)
            {
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Select game library file",
                    Filter = "JSON files (*.json)|*.json",
                    CheckFileExists = true,
                };

                if (dialog.ShowDialog() != true)
                {
                    this.Close();
                    return false;
                }

                AppSettings.Instance.LibraryPath = dialog.FileName;
                await AppSettings.Instance.Save();
            }

            await GameLibrary.Instance.Load(AppSettings.Instance.LibraryPath ?? throw new Exception($"missing {nameof(AppSettings.Instance.LibraryPath)}"));

            return true;
        }

        /// <summary>
        /// Calculates the total pixel height required to display <paramref name="count"/> games in a grid layout.
        /// </summary>
        /// <param name="count">Number of game items to lay out.</param>
        /// <returns>Total content height in pixels, or 0 if the count is zero.</returns>
        double GridContentHeight(int count)
        {
            if (count == 0) return 0;
            int rows = (int)Math.Ceiling((double)count / _controlsPerRow);
            return GameInfoControl.ShadowBlurRadius + rows * GameInfoControl.ControlHeight + (rows - 1) * Gap + Gap;
        }

        /// <summary>
        /// Creates <see cref="GameInfoControl"/> instances for each game and arranges them
        /// in a single horizontal row inside the given canvas.
        /// </summary>
        /// <param name="container">Target canvas that will hold the controls.</param>
        /// <param name="games">Ordered array of game to display.</param>
        void PopulateHorizontalSection(Canvas container, string[] games)
        {
            using var tb = new TimedBlock($"{nameof(MainWindow)}.{nameof(PopulateHorizontalSection)}({games.Length} games)");

            for (int i = 0; i < games.Length; i++)
            {
                var control = new GameInfoControl(games[i]) { CacheMode = new BitmapCache(), Opacity = 0 };
                container.Children.Add(control);
                Canvas.SetLeft(control, _gridOffset + i * (GameInfoControl.ControlWidth + Gap));
                Canvas.SetTop(control, GameInfoControl.ShadowBlurRadius);

                var fadeIn = new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(GamePlacementDurationMs)))
                {
                    BeginTime = TimeSpan.FromMilliseconds(i * GamePlacementDelayMs)
                };
                control.BeginAnimation(UIElement.OpacityProperty, fadeIn);
            }
        }

        /// <summary>
        /// Creates <see cref="GameInfoControl"/> instances for each game and arranges them
        /// in a multi-row grid inside the given canvas.
        /// </summary>
        /// <param name="container">Target canvas that will hold the controls.</param>
        /// <param name="games">Ordered array of games to display.</param>
        void PopulateGridSection(Canvas container, string[] games)
        {
            using var tb = new TimedBlock($"{nameof(MainWindow)}.{nameof(PopulateGridSection)}({games.Length} games)");

            for (int i = 0; i < games.Length; i++)
            {
                var control = new GameInfoControl(games[i]) { CacheMode = new BitmapCache(), Opacity = 0 };
                container.Children.Add(control);
                Canvas.SetLeft(control, _gridOffset + (i % _controlsPerRow) * (GameInfoControl.ControlWidth + Gap));
                Canvas.SetTop(control, GameInfoControl.ShadowBlurRadius + (i / _controlsPerRow) * (GameInfoControl.ControlHeight + Gap));

                var fadeIn = new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(GamePlacementDurationMs)))
                {
                    BeginTime = TimeSpan.FromMilliseconds(i * GamePlacementDelayMs)
                };
                control.BeginAnimation(UIElement.OpacityProperty, fadeIn);
            }
        }

        /// <summary>
        /// Closes the window when the Escape key is pressed.
        /// </summary>
        void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
                this.Close();
        }

        /// <summary>
        /// Shows or collapses user-game controls based on whether they fall within
        /// the current vertical viewport, plus a one-row buffer above and below.
        /// </summary>
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

        /// <summary>
        /// Applies a horizontal scroll impulse to the New Games or Recent Games section
        /// when the mouse wheel is used over the corresponding canvas.
        /// </summary>
        void HorizontalCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            double impulse = -e.Delta / 120.0 * ScrollImpulse;

            if (sender == NewGamesCanvas && _newGamesMaxScrollX > 0)
            {
                _newGamesVelocityX = Math.Clamp(_newGamesVelocityX + impulse, -ScrollMaxVelocity, ScrollMaxVelocity);
                FadeScrollThumb(NewGamesScrollThumb, true);
            }
            else if (sender == RecentGamesCanvas && _recentGamesMaxScrollX > 0)
            {
                _lastPlayedVelocityX = Math.Clamp(_lastPlayedVelocityX + impulse, -ScrollMaxVelocity, ScrollMaxVelocity);
                FadeScrollThumb(RecentGamesScrollThumb, true);
            }

            if (!_scrollAnimating)
            {
                _scrollAnimating = true;
                CompositionTarget.Rendering += OnScrollRendering;
            }

            e.Handled = true;
        }

        /// <summary>
        /// Applies a vertical scroll impulse to the User Games section
        /// when the mouse wheel is used over its canvas.
        /// </summary>
        void VerticalCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (_userGamesMaxScrollY > 0)
            {
                _allGamesVelocityY = Math.Clamp(_allGamesVelocityY + (-e.Delta / 120.0 * ScrollImpulse), -ScrollMaxVelocity, ScrollMaxVelocity);
                FadeScrollThumb(UserGamesScrollThumb, true);
            }

            if (!_scrollAnimating)
            {
                _scrollAnimating = true;
                CompositionTarget.Rendering += OnScrollRendering;
            }

            e.Handled = true;
        }

        /// <summary>
        /// Advances the inertia-based scroll animation each frame by applying friction to all
        /// active velocities, clamping offsets to their valid ranges, and updating scroll thumbs.
        /// Unregisters itself from <see cref="CompositionTarget.Rendering"/> once all motion stops.
        /// </summary>
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
                if (Math.Abs(_newGamesVelocityX) < 0.5) { _newGamesVelocityX = 0; FadeScrollThumb(NewGamesScrollThumb, false); }
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
                if (Math.Abs(_lastPlayedVelocityX) < 0.5) { _lastPlayedVelocityX = 0; FadeScrollThumb(RecentGamesScrollThumb, false); }
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
                if (Math.Abs(_allGamesVelocityY) < 0.5) { _allGamesVelocityY = 0; FadeScrollThumb(UserGamesScrollThumb, false); }
            }
            else _allGamesVelocityY = 0;

            if (!anyActive)
            {
                _scrollAnimating = false;
                CompositionTarget.Rendering -= OnScrollRendering;
            }

            UpdateScrollThumbs();
        }

        /// <summary>
        /// Refreshes the position and size of all three scroll thumb indicators.
        /// </summary>
        void UpdateScrollThumbs()
        {
            UpdateScrollThumb(NewGamesScrollThumb, NewGamesScrollTrack, _newGamesOffsetX, _newGamesMaxScrollX, NewGamesCanvas.ActualWidth);
            UpdateScrollThumb(RecentGamesScrollThumb, RecentGamesScrollTrack, _lastPlayedOffsetX, _recentGamesMaxScrollX, RecentGamesCanvas.ActualWidth);
            UpdateScrollThumb(UserGamesScrollThumb, UserGamesScrollTrack, _allGamesOffsetY, _userGamesMaxScrollY, UserGamesCanvas.ActualHeight);
        }

        /// <summary>
        /// Recalculates the width and left margin of a scroll thumb rectangle so that it
        /// proportionally represents the current scroll position within the track.
        /// </summary>
        /// <param name="thumb">The thumb rectangle to update.</param>
        /// <param name="track">The track rectangle that constrains the thumb.</param>
        /// <param name="offset">Current scroll offset in pixels.</param>
        /// <param name="maxScroll">Maximum allowed scroll offset in pixels.</param>
        /// <param name="viewport">Visible viewport size in pixels.</param>
        static void FadeScrollThumb(Rectangle thumb, bool visible)
        {
            thumb.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(visible ? 0.8 : 0.0, TimeSpan.FromMilliseconds(visible ? 100 : 500)));
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

        /// <summary>
        /// Opens the filter dropdown when the User Games label is clicked,
        /// guarding against an immediate re-open after the dropdown is closed.
        /// </summary>
        void UserGamesLabel_Click(object sender, MouseButtonEventArgs e)
        {
            if ((DateTime.UtcNow - _userGamesDropdownLastClosed).TotalMilliseconds > 300)
            {
                UpdateFilterOptionHighlight();
                UserGamesDropdown.IsOpen = true;
            }
        }

        /// <summary>
        /// Records the UTC timestamp when the filter dropdown closes so that
        /// accidental immediate re-opens can be suppressed.
        /// </summary>
        void UserGamesDropdown_Closed(object? sender, EventArgs e)
        {
            _userGamesDropdownLastClosed = DateTime.UtcNow;
        }

        /// <summary>
        /// Applies the selected filter mode from the dropdown and refreshes the User Games grid.
        /// </summary>
        async void UserGamesFilter_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is TextBlock tb && tb.Tag is string tag && Enum.TryParse<UserGamesFilterMode>(tag, out var mode))
            {
                _userGamesFilterMode = mode;
                UserGamesDropdown.IsOpen = false;
                RefreshUserGames();
                AppSettings.Instance.UserGamesFilterMode = _userGamesFilterMode;
                await AppSettings.Instance.Save();
            }
        }

        /// <summary>
        /// Updates the foreground color of each filter option in the dropdown
        /// to highlight the currently active filter mode.
        /// </summary>
        void UpdateFilterOptionHighlight()
        {
            FilterOptionInstalled.Foreground = _userGamesFilterMode == UserGamesFilterMode.Installed ? Brushes.LightSkyBlue : Brushes.White;
            FilterOptionRemoved.Foreground = _userGamesFilterMode == UserGamesFilterMode.Removed ? Brushes.LightSkyBlue : Brushes.White;
            FilterOptionFinished.Foreground = _userGamesFilterMode == UserGamesFilterMode.Finished ? Brushes.LightSkyBlue : Brushes.White;
            FilterOptionUnfinished.Foreground = _userGamesFilterMode == UserGamesFilterMode.Unfinished ? Brushes.LightSkyBlue : Brushes.White;
            FilterOptionAll.Foreground = _userGamesFilterMode == UserGamesFilterMode.All ? Brushes.LightSkyBlue : Brushes.White;
        }

        /// <summary>
        /// Re-queries the game library using the active filter, repopulates the User Games grid,
        /// and resets scroll state and viewport culling.
        /// </summary>
        void RefreshUserGames()
        {
            var userGames = GetUserGames();

            UserGamesLabel.Text = _userGamesFilterMode.ToString();
            UserGamesContainer.Children.Clear();
            PopulateGridSection(UserGamesContainer, userGames);
            UserGamesCount.Text = $"({userGames.Length})";

            _userGamesMaxScrollY = Math.Max(0, GridContentHeight(userGames.Length) - UserGamesCanvas.ActualHeight + _gridOffset);
            _allGamesOffsetY = 0;
            _allGamesVelocityY = 0;
            _allGamesTransform.Y = 0;

            _visibleControls = UserGamesContainer.Children.OfType<GameInfoControl>()
                .Select(c => (Control: c, LocalTop: Canvas.GetTop(c)))
                .ToArray();

            UpdateViewportCulling();
            UpdateScrollThumbs();
        }

        /// <summary>
        /// Recalculates the User Games scroll range and viewport culling after the available
        /// canvas height changes (e.g. when an adjacent section is collapsed or expanded).
        /// </summary>
        void RefreshUserGamesViewport()
        {
            RootGrid.UpdateLayout();
            _userGamesMaxScrollY = Math.Max(0, GridContentHeight(_visibleControls.Length) - UserGamesCanvas.ActualHeight + _gridOffset);
            _allGamesOffsetY = Math.Min(_allGamesOffsetY, _userGamesMaxScrollY);
            _allGamesTransform.Y = -_allGamesOffsetY;
            UpdateViewportCulling();
            UpdateScrollThumbs();
        }

        void ApplyNewGamesState()
        {
            NewGamesToggle.Text = _newGamesCollapsed ? "\u25B6" : "\u25BC";
            NewGamesCanvas.Visibility = _newGamesCollapsed ? Visibility.Collapsed : Visibility.Visible;
            NewGamesScrollTrack.Visibility = _newGamesCollapsed ? Visibility.Collapsed : Visibility.Visible;
            NewGamesScrollThumb.Visibility = _newGamesCollapsed ? Visibility.Collapsed : Visibility.Visible;
            NewGamesDivider.Visibility = _newGamesCollapsed ? Visibility.Visible : Visibility.Collapsed;
        }

        void ApplyRecentGamesState()
        {
            RecentGamesToggle.Text = _recentGamesCollapsed ? "\u25B6" : "\u25BC";
            RecentGamesCanvas.Visibility = _recentGamesCollapsed ? Visibility.Collapsed : Visibility.Visible;
            RecentGamesScrollTrack.Visibility = _recentGamesCollapsed ? Visibility.Collapsed : Visibility.Visible;
            RecentGamesScrollThumb.Visibility = _recentGamesCollapsed ? Visibility.Collapsed : Visibility.Visible;
            RecentGamesDivider.Visibility = _recentGamesCollapsed ? Visibility.Visible : Visibility.Collapsed;
        }

        void ApplyUserGamesState()
        {
            UserGamesToggle.Text = _userGamesCollapsed ? "\u25B6" : "\u25BC";
            UserGamesCanvas.Visibility = _userGamesCollapsed ? Visibility.Collapsed : Visibility.Visible;
            UserGamesCanvasRow.Height = _userGamesCollapsed ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
            UserGamesScrollTrack.Visibility = _userGamesCollapsed ? Visibility.Collapsed : Visibility.Visible;
            UserGamesScrollThumb.Visibility = _userGamesCollapsed ? Visibility.Collapsed : Visibility.Visible;
            UserGamesDivider.Visibility = _userGamesCollapsed ? Visibility.Visible : Visibility.Collapsed;
        }

        static DoubleAnimation FadeAnimation(double from, double to) =>
            new(from, to, new Duration(TimeSpan.FromMilliseconds(GroupFadeAnimationDuration)));

        /// <summary>
        /// Toggles a group section between expanded and collapsed states with a fade animation.
        /// </summary>
        /// <param name="getCollapsed">Returns the current collapsed state of the section.</param>
        /// <param name="setCollapsed">Sets the collapsed state of the section.</param>
        /// <param name="toggle">Arrow glyph TextBlock to update.</param>
        /// <param name="canvas">Content canvas to fade in or out.</param>
        /// <param name="scrollTrack">Scroll track indicator to show or hide.</param>
        /// <param name="scrollThumb">Scroll thumb indicator to show or hide.</param>
        /// <param name="divider">Divider shown when collapsed, hidden when expanded.</param>
        /// <param name="canvasRow">Optional grid row whose height is set to 0 / Star alongside visibility.</param>
        /// <param name="onExpand">Optional callback invoked immediately when the section is expanding.</param>
        /// <param name="onViewportChange">Optional callback invoked after expand or after the collapse animation completes.</param>
        static void ToggleGroupSection(Func<bool> getCollapsed, Action<bool> setCollapsed, TextBlock toggle, Canvas canvas, Rectangle scrollTrack, Rectangle scrollThumb, Rectangle divider,
            RowDefinition? canvasRow = null, Action? onExpand = null, Action? onViewportChange = null)
        {
            bool collapsed = !getCollapsed();
            setCollapsed(collapsed);
            toggle.Text = collapsed ? "\u25B6" : "\u25BC";

            if (collapsed)
            {
                var anim = FadeAnimation(canvas.Opacity, 0);
                anim.Completed += (s, ea) =>
                {
                    if (!getCollapsed()) return;
                    canvas.BeginAnimation(UIElement.OpacityProperty, null);
                    canvas.Visibility = Visibility.Collapsed;
                    if (canvasRow != null) canvasRow.Height = new GridLength(0);
                    scrollTrack.Visibility = Visibility.Collapsed;
                    scrollThumb.Visibility = Visibility.Collapsed;
                    divider.Visibility = Visibility.Visible;
                    onViewportChange?.Invoke();
                };
                canvas.BeginAnimation(UIElement.OpacityProperty, anim);
            }
            else
            {
                onExpand?.Invoke();
                double fromOpacity = canvas.Visibility == Visibility.Collapsed ? 0 : canvas.Opacity;
                if (canvasRow != null) canvasRow.Height = new GridLength(1, GridUnitType.Star);
                canvas.Visibility = Visibility.Visible;
                scrollTrack.Visibility = Visibility.Visible;
                scrollThumb.Visibility = Visibility.Visible;
                divider.Visibility = Visibility.Collapsed;
                canvas.BeginAnimation(UIElement.OpacityProperty, FadeAnimation(fromOpacity, 1));
                onViewportChange?.Invoke();
            }
        }

        void NewGamesToggle_Click(object sender, MouseButtonEventArgs e) =>
            ToggleGroupSection(() => _newGamesCollapsed, v => _newGamesCollapsed = v,
                NewGamesToggle, NewGamesCanvas,
                NewGamesScrollTrack, NewGamesScrollThumb, NewGamesDivider,
                onExpand: () => { _newGamesOffsetX = 0; _newGamesVelocityX = 0; _newGamesTransform.X = 0; },
                onViewportChange: RefreshUserGamesViewport);

        void RecentGamesToggle_Click(object sender, MouseButtonEventArgs e) =>
            ToggleGroupSection(() => _recentGamesCollapsed, v => _recentGamesCollapsed = v,
                RecentGamesToggle, RecentGamesCanvas,
                RecentGamesScrollTrack, RecentGamesScrollThumb, RecentGamesDivider,
                onExpand: () => { _lastPlayedOffsetX = 0; _lastPlayedVelocityX = 0; _lastPlayedTransform.X = 0; },
                onViewportChange: RefreshUserGamesViewport);

        void UserGamesToggle_Click(object sender, MouseButtonEventArgs e) =>
            ToggleGroupSection(() => _userGamesCollapsed, v => _userGamesCollapsed = v,
                UserGamesToggle, UserGamesCanvas,
                UserGamesScrollTrack, UserGamesScrollThumb, UserGamesDivider,
                canvasRow: UserGamesCanvasRow,
                onExpand: () => { _allGamesOffsetY = 0; _allGamesVelocityY = 0; _allGamesTransform.Y = 0; },
                onViewportChange: RefreshUserGamesViewport);
    }
}
