using naLauncher2.Wpf.Api;
using System.Diagnostics;
using System.Reflection.Emit;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
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
        const double MoveDurationMs = 300; // duration of position-change (move) animation [in milliseconds]

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

        GamesSortMode _userGamesSortMode = GamesSortMode.Title;
        bool _userGamesSortDescending = false;

        string _userGamesTitleFilter = string.Empty;
        HashSet<string> _userGamesGenreFilter = [];
        bool _newGamesCollapsed = true;
        bool _recentGamesCollapsed = true;
        bool _recentGamesInstalledOnly = true;
        bool _newGamesSortDescending = true;
        bool _recentGamesSortDescending = true;

        string? _contextMenuTargetId;

        bool _isRefreshing = false;
        readonly Queue<string> _contextRefreshQueue = new();
        string[]? _pendingNewGameDataRefresh;

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

            UserGamesSection.Visibility = Visibility.Visible;

            _userGamesFilterMode = AppSettings.Instance.UserGamesFilterMode;
            _userGamesSortMode = AppSettings.Instance.UserGamesSortMode;
            _userGamesSortDescending = AppSettings.Instance.UserGamesSortDescending;
            _userGamesGenreFilter = [.. AppSettings.Instance.UserGamesGenreFilter];
            _newGamesCollapsed = AppSettings.Instance.NewGamesCollapsed;
            _recentGamesCollapsed = AppSettings.Instance.RecentGamesCollapsed;
            _recentGamesInstalledOnly = AppSettings.Instance.RecentGamesInstalledOnly;
            _newGamesSortDescending = AppSettings.Instance.NewGamesSortDescending;
            _recentGamesSortDescending = AppSettings.Instance.RecentGamesSortDescending;

            double screenWidth = RootGrid.ActualWidth;
            _controlsPerRow = (int)((screenWidth + Gap) / (GameInfoControl.ControlWidth + Gap));

            double totalWidth = _controlsPerRow * GameInfoControl.ControlWidth + (_controlsPerRow - 1) * Gap;
            _gridOffset = (screenWidth - totalWidth) / 2;

            var newGames = GetNewGames();
            var recentGames = GetRecentGames();
            var userGames = GetUserGames();

            PopulateHorizontalSection(NewGamesContainer, newGames,
                id => $"added {TimeAgo(GameLibrary.Instance.Games[id].Added)}");
            if (newGames.Length == 0)
                _newGamesCollapsed = true;
            ApplyNewGamesState();

            PopulateHorizontalSection(RecentGamesContainer, recentGames,
                id => $"played {TimeAgo(GameLibrary.Instance.Games[id].LastPlayed!.Value)}");
            if (recentGames.Length == 0)
                _recentGamesCollapsed = true;
            ApplyRecentGamesState();

            PopulateGridSection(UserGamesContainer, userGames);

            UserGamesLabel.Text = GetUserGamesLabelText(_userGamesFilterMode, userGames.Length);
            UpdateGenresLabel();
            UserGamesOrderLabel.Text = _userGamesSortMode.ToString();
            UserGamesOrderDirectionToggle.Text = _userGamesSortDescending ? "\u25BC" : "\u25B2";
            NewGamesOrderDirectionToggle.Text = _newGamesSortDescending ? "\u25BC" : "\u25B2";
            RecentGamesOrderDirectionToggle.Text = _recentGamesSortDescending ? "\u25BC" : "\u25B2";
            NewGamesOrderDirectionToggle.ToolTip = $"{newGames.Length} {(newGames.Length == 1 ? "game" : "games")}";
            RecentGamesOrderDirectionToggle.ToolTip = $"{recentGames.Length} {(recentGames.Length == 1 ? "game" : "games")}";
            UserGamesOrderDirectionToggle.ToolTip = $"{userGames.Length} {(userGames.Length == 1 ? "game" : "games")}";
            UpdateRecentGamesInstalledOnlyToggle();

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

            if (_pendingNewGameDataRefresh is not null)
                await RefreshNewGameDataInBackground(_pendingNewGameDataRefresh);
        }

        string[] GetUserGames()
        {
            static bool FilterGameTitle(string gameTitle, string filter)
            {
                var filterParts = filter.ToLower().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var gameTitleParts = gameTitle.ToLower().Split(' ', StringSplitOptions.RemoveEmptyEntries);

                return filterParts
                    .All(x => gameTitleParts.Any(xx => xx.Contains(x)));
            }

            var all = GameLibrary.Instance.Games.AsEnumerable();

            var filtered = _userGamesFilterMode switch
            {
                UserGamesFilterMode.Removed => all.Where(x => x.Value.Removed),
                UserGamesFilterMode.Completed => all.Where(x => x.Value.Completed.HasValue),
                UserGamesFilterMode.MissingData => all.Where(x => x.Value.MissingImage),
                UserGamesFilterMode.Steam => all.Where(x => x.Value.Extensions?.ContainsKey(GameInfoExtension.SteamAppId.ToString()) == true),
                UserGamesFilterMode.Igdb => all.Where(x => x.Value.Extensions?.ContainsKey(GameInfoExtension.IgdbId.ToString()) == true),
                UserGamesFilterMode.All => all,
                _ => all.Where(x => x.Value.Installed),
            };

            if (!string.IsNullOrEmpty(_userGamesTitleFilter))
                filtered = filtered.Where(x => FilterGameTitle(x.Key, _userGamesTitleFilter));

            if (_userGamesGenreFilter.Count > 0)
                filtered = filtered.Where(x => x.Value.Genres != null && x.Value.Genres.Any(g => _userGamesGenreFilter.Contains(g)));

            var sorted = _userGamesSortMode switch
            {
                GamesSortMode.Added => filtered.OrderBy(x => x.Value.Added),
                GamesSortMode.Completed => filtered.OrderBy(x => x.Value.Completed),
                GamesSortMode.Played => filtered.OrderBy(x => x.Value.Played.Count),
                GamesSortMode.Rating => filtered.OrderBy(x => x.Value.Rating),
                _ => filtered.OrderBy(x => x.Key),
            };

            return (_userGamesSortDescending ? sorted.Reverse() : (IEnumerable<KeyValuePair<string, GameInfo>>)sorted)
                .Select(x => x.Key)
                .ToArray();
        }

        string[] GetNewGames()
        {
            var games = GameLibrary.Instance.Games
                .Where(x => x.Value.Installed && x.Value.NotPlayed)
                .OrderBy(x => x.Value.Added);

            return (_newGamesSortDescending ? games.Reverse() : (IEnumerable<KeyValuePair<string, GameInfo>>)games)
                .Select(x => x.Key)
                .ToArray();
        }

        string[] GetRecentGames()
        {
            var games = GameLibrary.Instance.Games
                .Where(x => (!_recentGamesInstalledOnly || x.Value.Installed) && !x.Value.NotPlayed)
                .OrderBy(x => x.Value.LastPlayed);

            return (_recentGamesSortDescending ? games.Reverse() : (IEnumerable<KeyValuePair<string, GameInfo>>)games)
                .Select(x => x.Key)
                .ToArray();
        }

        /// <summary>
        /// Load app settings and game library from file. If the library path is not set in settings,
        /// opens the settings dialog so the user can configure it.
        /// </summary>
        async Task<bool> LoadLibraryAndSettings()
        {
            if (AppSettings.Instance.LibraryPathMissing)
            {
                var settingsDialog = new SettingsDialog() { Owner = this };
                if (settingsDialog.ShowDialog() != true || settingsDialog.SelectedLibraryPath is null)
                {
                    this.Close();
                    return false;
                }
            }

            await GameLibrary.Instance.Load(AppSettings.Instance.LibraryPath!);

            // backup on start in case the user has made changes to their library outside of the launcher and we want to avoid losing data
            await GameLibrary.Instance.Backup();

            var refreshResult = await GameLibrary.Instance.RefreshSources(AppSettings.Instance.Sources, 
                extensions: AppSettings.Instance.GameExtensions,
                topLevelOnly: AppSettings.Instance.TopLevelOnly);
            
            if (refreshResult.NewGames?.Length > 0)
                _pendingNewGameDataRefresh = refreshResult.NewGames;

            return true;
        }

        async void SettingsButton_Click(object sender, MouseButtonEventArgs e)
        {
            var libraryPathBefore = AppSettings.Instance.LibraryPath;

            var dialog = new SettingsDialog() { Owner = this };
            if (dialog.ShowDialog() != true)
                return;

            if (libraryPathBefore != AppSettings.Instance.LibraryPath)
            {
                await GameLibrary.Instance.Load(AppSettings.Instance.LibraryPath!);
                RefreshAllSections();
            }
        }

        async void RefreshButton_Click(object sender, MouseButtonEventArgs e)
        {
            var glow = TryStartRefreshAnimation();
            if (glow is null)
                return;

            if (await GameLibrary.Instance.RefreshMissingGameImagesFromCache(AppSettings.Instance.ImageCachePath))
                RefreshAllSections();

            var games = GameLibrary.Instance.Games.Keys.ToArray();
            GameInfoControl[]? prevControls = null;
            bool changed = false;

            for (int i = 0; i < games.Length; i++)
            {
                RefreshProgressText.Text = $"{games[i]} [{i + 1} / {games.Length}]";
                RefreshProgressText.Visibility = Visibility.Visible;

                if (prevControls is not null)
                    foreach (var c in prevControls) c.StopRefreshGlow();
                prevControls = FindGameControls(games[i]);
                foreach (var c in prevControls) c.StartRefreshGlow();

                if (await GameLibrary.Instance.RefreshGameData(games[i], silent: true))
                    changed = true;
            }

            if (prevControls is not null)
                foreach (var c in prevControls) c.StopRefreshGlow();

            if (changed)
                RefreshAllSections();

            StopRefreshAnimation(glow);
        }

        DropShadowEffect? TryStartRefreshAnimation()
        {
            if (_isRefreshing)
                return null;

            _isRefreshing = true;
            RefreshButton.Cursor = Cursors.Arrow;

            var spin = new DoubleAnimation(0, 360, new Duration(TimeSpan.FromMilliseconds(800)))
            {
                RepeatBehavior = RepeatBehavior.Forever
            };
            RefreshButtonRotate.BeginAnimation(RotateTransform.AngleProperty, spin);

            var glow = new DropShadowEffect { Color = Colors.LightSkyBlue, BlurRadius = GameInfoControl.ShadowBlurRadius, ShadowDepth = 0, Opacity = 0 };

            RefreshButton.Effect = glow;
            RefreshButton.Foreground = Brushes.LightSkyBlue;
            RefreshButton.Opacity = 1;

            RefreshProgressText.Effect = glow;
            RefreshProgressText.Foreground = Brushes.LightSkyBlue;
            RefreshProgressText.Opacity = 1;

            glow.BeginAnimation(DropShadowEffect.OpacityProperty,
                new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(200))));

            return glow;
        }

        void StopRefreshAnimation(DropShadowEffect glow)
        {
            RefreshButtonRotate.BeginAnimation(RotateTransform.AngleProperty, null);
            RefreshButtonRotate.Angle = 0;
            RefreshProgressText.Visibility = Visibility.Collapsed;

            var glowOut = new DoubleAnimation(glow.Opacity, 0, new Duration(TimeSpan.FromMilliseconds(200)));
            glowOut.Completed += (_, _) =>
            {
                RefreshButton.Effect = null;
                RefreshButton.ClearValue(TextBlock.ForegroundProperty);
                RefreshButton.ClearValue(UIElement.OpacityProperty);
                RefreshButton.Cursor = Cursors.Hand;

                RefreshProgressText.Effect = null;
                RefreshProgressText.ClearValue(TextBlock.ForegroundProperty);
                RefreshProgressText.ClearValue(UIElement.OpacityProperty);

                _isRefreshing = false;
            };
            glow.BeginAnimation(DropShadowEffect.OpacityProperty, glowOut);
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
        /// Returns a human-readable "X ago" string for the given point in time.
        /// </summary>
        static string TimeAgo(DateTime dt)
        {
            var span = DateTime.Now - dt;
            if (span.TotalDays >= 365) { int n = (int)(span.TotalDays / 365); return $"{n} {(n == 1 ? "year" : "years")} ago"; }
            if (span.TotalDays >= 30) { int n = (int)(span.TotalDays / 30); return $"{n} {(n == 1 ? "month" : "months")} ago"; }
            if (span.TotalDays >= 7) { int n = (int)(span.TotalDays / 7); return $"{n} {(n == 1 ? "week" : "weeks")} ago"; }
            if (span.TotalDays >= 1) { int n = (int)span.TotalDays; return $"{n} {(n == 1 ? "day" : "days")} ago"; }
            if (span.TotalHours >= 1) { int n = (int)span.TotalHours; return $"{n} {(n == 1 ? "hour" : "hours")} ago"; }
            return "just now";
        }

        /// <summary>
        /// Creates <see cref="GameInfoControl"/> instances for each game and arranges them
        /// in a single horizontal row inside the given canvas.
        /// </summary>
        /// <param name="container">Target canvas that will hold the controls.</param>
        /// <param name="games">Ordered array of game to display.</param>
        /// <param name="subLabelSelector">Optional delegate that returns a sub-label string for a given game id.</param>
        void PopulateHorizontalSection(Canvas container, string[] games, Func<string, string?>? subLabelSelector = null)
        {
            double subLabelY = GameInfoControl.ShadowBlurRadius + GameInfoControl.ControlHeight + 8;

            for (int i = 0; i < games.Length; i++)
            {
                double x = _gridOffset + i * (GameInfoControl.ControlWidth + Gap);
                var delay = new Duration(TimeSpan.FromMilliseconds(GamePlacementDurationMs));
                var beginTime = TimeSpan.FromMilliseconds(i * GamePlacementDelayMs);

                var control = new GameInfoControl(games[i]) { CacheMode = new BitmapCache(), Opacity = 0 };
                container.Children.Add(control);
                Canvas.SetLeft(control, x);
                Canvas.SetTop(control, GameInfoControl.ShadowBlurRadius);
                control.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, delay) { BeginTime = beginTime });

                if (subLabelSelector is not null)
                {
                    var labelText = subLabelSelector(games[i]);
                    if (!string.IsNullOrEmpty(labelText))
                    {
                        var subLabel = new TextBlock
                        {
                            Text = labelText,
                            Tag = games[i],
                            Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                            FontSize = 11,
                            FontStyle = FontStyles.Italic,
                            Width = GameInfoControl.ControlWidth,
                            TextAlignment = TextAlignment.Center,
                            TextTrimming = TextTrimming.CharacterEllipsis,
                            Opacity = 0,
                        };
                        container.Children.Add(subLabel);
                        Canvas.SetLeft(subLabel, x);
                        Canvas.SetTop(subLabel, subLabelY);
                        subLabel.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, delay) { BeginTime = beginTime });
                    }
                }
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
            bool isRatingSortActive = _userGamesSortMode == GamesSortMode.Rating;
            for (int i = 0; i < games.Length; i++)
            {
                var control = new GameInfoControl(games[i], isRatingSortActive) { CacheMode = new BitmapCache(), Opacity = 0 };
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
        /// Ctrl+1/2/3 toggle the New, Recent and User Games groups respectively.
        /// </summary>
        void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                this.Close();
                return;
            }

            if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                switch (e.Key)
                {
                    case Key.D1:
                    case Key.NumPad1:
                        ToggleNewGames();
                        e.Handled = true;
                        break;

                    case Key.D2:
                    case Key.NumPad2:
                        ToggleRecentGames();
                        e.Handled = true;
                        break;
                }
            }
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

        void ShowDropdown(Border panel, FrameworkElement anchor)
        {
            Point pos = anchor.TransformToAncestor(RootGrid).Transform(new Point(0, anchor.ActualHeight + 4));
            Canvas.SetLeft(panel, pos.X);
            Canvas.SetTop(panel, pos.Y);
            UserGamesFilterPanel.Visibility = Visibility.Collapsed;
            UserGamesOrderPanel.Visibility = Visibility.Collapsed;
            UserGamesGenresPanel.Visibility = Visibility.Collapsed;
            GameContextMenuPanel.Visibility = Visibility.Collapsed;
            panel.Visibility = Visibility.Visible;
            DropdownOverlay.Visibility = Visibility.Visible;
        }

        void HideDropdowns()
        {
            DropdownOverlay.Visibility = Visibility.Collapsed;
        }

        void DropdownOverlay_Click(object sender, MouseButtonEventArgs e)
        {
            HideDropdowns();
        }

        void DropdownOverlay_RightClick(object sender, MouseButtonEventArgs e)
        {
            HideDropdowns();
            e.Handled = true;
        }

        void ShowGameContextMenu(Point pos)
        {
            UserGamesFilterPanel.Visibility = Visibility.Collapsed;
            UserGamesOrderPanel.Visibility = Visibility.Collapsed;
            UserGamesGenresPanel.Visibility = Visibility.Collapsed;
            GameContextMenuPanel.Visibility = Visibility.Visible;
            DropdownOverlay.Visibility = Visibility.Visible;

            GameContextMenuPanel.UpdateLayout();

            double x = Math.Min(pos.X, RootGrid.ActualWidth - GameContextMenuPanel.ActualWidth - 16);
            double y = Math.Min(pos.Y, RootGrid.ActualHeight - GameContextMenuPanel.ActualHeight - 16);
            Canvas.SetLeft(GameContextMenuPanel, Math.Max(0, x));
            Canvas.SetTop(GameContextMenuPanel, Math.Max(0, y));

            if (_contextMenuTargetId is not null && GameLibrary.Instance.Games.TryGetValue(_contextMenuTargetId, out var game))
            {
                SetMenuItemEnabled(ContextMenuRun, game.Installed);
                SetMenuItemEnabled(ContextMenuRemove, game.Installed);
                SetMenuItemEnabled(ContextMenuDelete, !game.Installed);
                SetMenuItemEnabled(ContextMenuMarkAsCompleted, !game.Completed.HasValue);
                SetMenuItemEnabled(ContextMenuExplore, true);
                SetMenuItemEnabled(ContextMenuRefresh, AppSettings.Instance.TwitchDev != null);
                SetMenuItemEnabled(ContextMenuProperties, true);
            }
        }

        static void SetMenuItemEnabled(System.Windows.Controls.TextBlock item, bool enabled)
        {
            item.Opacity = enabled ? 1.0 : 0.3;
            item.RemoveHandler(UIElement.PreviewMouseLeftButtonUpEvent, (MouseButtonEventHandler)ConsumeClick);
            if (!enabled)
                item.AddHandler(UIElement.PreviewMouseLeftButtonUpEvent, (MouseButtonEventHandler)ConsumeClick);
        }

        static void ConsumeClick(object sender, MouseButtonEventArgs e) => e.Handled = true;

        async void RootGrid_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount != 2)
                return;

            var control = FindAncestorOrSelf<GameInfoControl>((DependencyObject)e.OriginalSource);
            if (control is null)
                return;

            if (!GameLibrary.Instance.Games.TryGetValue(control.Id, out var game) || !game.Installed)
                return;

            e.Handled = true;

            await RunGame(game);
        }

        void RootGrid_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            var control = FindAncestorOrSelf<GameInfoControl>((DependencyObject)e.OriginalSource);
            if (control is null)
                return;

            _contextMenuTargetId = control.Id;
            ShowGameContextMenu(e.GetPosition(RootGrid));
            e.Handled = true;
        }

        async Task RunGame(GameInfo game)
        {
            try
            {
                Process.Start(new ProcessStartInfo(game.Shortcut!) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Log.WriteLine($"Error running game '{_contextMenuTargetId}': {ex}");
            }

            game.Played.Add(DateTime.Now);

            await GameLibrary.Instance.Save();

            RefreshAllSections();
        }

        async void GameContextMenu_Run_Click(object sender, MouseButtonEventArgs e)
        {
            HideDropdowns();

            if (_contextMenuTargetId is null || !GameLibrary.Instance.Games.TryGetValue(_contextMenuTargetId, out var game) || !game.Installed)
                return;

            await RunGame(game);
        }

        async void GameContextMenu_Remove_Click(object sender, MouseButtonEventArgs e)
        {
            HideDropdowns();

            if (_contextMenuTargetId is null || !GameLibrary.Instance.Games.TryGetValue(_contextMenuTargetId, out var game) || !game.Installed)
                return;

            var dialog = new ConfirmationDialog($"Remove/Uninstall \"{_contextMenuTargetId}\"?") { Owner = this };
            if (dialog.ShowDialog() != true)
                return;

            if (game.Shortcut is not null && System.IO.File.Exists(game.Shortcut))
                System.IO.File.Delete(game.Shortcut);

            game.Shortcut = null;

            await GameLibrary.Instance.Save();

            Process.Start(new ProcessStartInfo("appwiz.cpl") { UseShellExecute = true });

            RefreshAllSections();
        }

        async void GameContextMenu_Delete_Click(object sender, MouseButtonEventArgs e)
        {
            HideDropdowns();

            if (_contextMenuTargetId is null || !GameLibrary.Instance.Games.ContainsKey(_contextMenuTargetId))
                return;

            var dialog = new ConfirmationDialog($"Delete \"{_contextMenuTargetId}\" from the library?") { Owner = this };
            if (dialog.ShowDialog() != true)
                return;

            if (!GameLibrary.Instance.Games.Remove(_contextMenuTargetId, out _))
            {
                new MessageDialog("Error", $"Failed to remove '{_contextMenuTargetId}' from library.").ShowDialog();
                return;
            }

            await GameLibrary.Instance.Save();

            RefreshAllSections();
        }

        async void GameContextMenu_MarkAsCompleted_Click(object sender, MouseButtonEventArgs e)
        {
            HideDropdowns();

            if (_contextMenuTargetId is null || !GameLibrary.Instance.Games.TryGetValue(_contextMenuTargetId, out var game))
                return;

            var dialog = new ConfirmationDialog($"Mark \"{_contextMenuTargetId}\" as completed?") { Owner = this };
            if (dialog.ShowDialog() != true)
                return;

            game.Completed = DateTime.Now;

            await GameLibrary.Instance.Save();

            RefreshAllSections();
        }

        void GameContextMenu_Explore_Click(object sender, MouseButtonEventArgs e)
        {
            HideDropdowns();

            if (_contextMenuTargetId is null || !GameLibrary.Instance.Games.TryGetValue(_contextMenuTargetId, out var game))
                return;

            var gameTitle = _contextMenuTargetId;

            var steamUrl = false;
            if (game.Extensions.TryGetValue(GameInfoExtension.SteamAppId.ToString(), out var steamAppId))
            {
                Process.Start(new ProcessStartInfo(SteamClient.GetStoreUrl(steamAppId)) { UseShellExecute = true });
                steamUrl = true;
            }

            if (GameLibrary.Instance.Games[gameTitle].Extensions.TryGetValue(GameInfoExtension.IgdbUrl.ToString(), out string? igdbUrl))
                Process.Start(new ProcessStartInfo(igdbUrl) { UseShellExecute = true });
            
            else if (!steamUrl)
                Process.Start(new ProcessStartInfo(IgdbClient.GetGameSearchUrl(gameTitle)) { UseShellExecute = true });
        }

        async Task RefreshNewGameDataInBackground(params string[] newGames)
        {
            var glow = TryStartRefreshAnimation();
            if (glow is null)
            {
                // A refresh is already running; queue the requested games so the running
                // loop picks them up, and mark each control immediately as "pending".
                foreach (var g in newGames)
                    if (!_contextRefreshQueue.Contains(g))
                    {
                        _contextRefreshQueue.Enqueue(g);
                        foreach (var c in FindGameControls(g)) c.StartRefreshGlow();
                    }
                return;
            }

            GameInfoControl[]? prevControls = null;

            for (var i = 0; i < newGames.Length; i++)
            {
                RefreshProgressText.Visibility = Visibility.Visible;
                RefreshProgressText.Text = newGames.Length == 1
                    ? newGames[i]
                    : $"{newGames[i]} [{i + 1} / {newGames.Length}]";

                if (prevControls is not null)
                    foreach (var c in prevControls) c.StopRefreshGlow();

                prevControls = FindGameControls(newGames[i]);
                foreach (var c in prevControls) c.StartRefreshGlow();

                await GameLibrary.Instance.RefreshGameData(newGames[i]);
            }

            // Drain any context-menu refreshes queued while the main pass was running.
            while (_contextRefreshQueue.Count > 0)
            {
                var id = _contextRefreshQueue.Dequeue();
                RefreshProgressText.Visibility = Visibility.Visible;
                RefreshProgressText.Text = _contextRefreshQueue.Count > 0
                    ? $"{id} [+{_contextRefreshQueue.Count} queued]"
                    : id;

                if (prevControls is not null)
                    foreach (var c in prevControls) c.StopRefreshGlow();
                prevControls = FindGameControls(id);
                foreach (var c in prevControls) c.StartRefreshGlow();

                await GameLibrary.Instance.RefreshGameData(id);
            }

            if (prevControls is not null)
                foreach (var c in prevControls) c.StopRefreshGlow();
            StopRefreshAnimation(glow);
            RefreshAllSections();
        }

        async void GameContextMenu_Refresh_Click(object sender, MouseButtonEventArgs e)
        {
            HideDropdowns();

            if (_contextMenuTargetId is null)
                return;

            await RefreshNewGameDataInBackground(_contextMenuTargetId);
        }

        async void GameContextMenu_Properties_Click(object sender, MouseButtonEventArgs e)
        {
            HideDropdowns();

            if (_contextMenuTargetId is null || !GameLibrary.Instance.Games.TryGetValue(_contextMenuTargetId, out var game))
                return;

            var dialog = new GamePropertiesDialog(_contextMenuTargetId, game) { Owner = this };
            if (dialog.ShowDialog() != true)
                return;

            var newName = dialog.NewId;
            if (string.IsNullOrEmpty(newName))
                return;

            if (newName != _contextMenuTargetId)
            {
                if (GameLibrary.Instance.Games.ContainsKey(newName))
                {
                    new MessageDialog("Error", $"A game named '{newName}' already exists.") { Owner = this }.ShowDialog();
                    return;
                }

                if (!GameLibrary.Instance.Games.Remove(_contextMenuTargetId, out _))
                {
                    new MessageDialog("Error", $"Failed to remove '{_contextMenuTargetId}' from library.") { Owner = this }.ShowDialog();
                    return;
                }

                GameLibrary.Instance.Games[newName] = game;
            }

            await GameLibrary.Instance.Save();

            RefreshAllSections();
        }

        /// <summary>
        /// Incrementally updates a horizontal canvas section: games that moved slide to their new
        /// position, new games fade in, and removed games fade out.
        /// </summary>
        void UpdateHorizontalSection(Canvas container, string[] games, Func<string, string?> subLabelSelector)
        {
            double subLabelY = GameInfoControl.ShadowBlurRadius + GameInfoControl.ControlHeight + 8;
            var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
            var moveDuration = new Duration(TimeSpan.FromMilliseconds(MoveDurationMs));
            var fadeDuration = new Duration(TimeSpan.FromMilliseconds(GamePlacementDurationMs));

            var existing = container.Children.OfType<GameInfoControl>().ToDictionary(c => c.Id);
            var existingLabels = container.Children.OfType<TextBlock>()
                .Where(tb => tb.Tag is string).ToDictionary(tb => (string)tb.Tag!);
            var newSet = new HashSet<string>(games);

            foreach (var (id, control) in existing)
            {
                if (newSet.Contains(id)) continue;
                var c = control;
                var anim = new DoubleAnimation(c.Opacity, 0, fadeDuration);
                anim.Completed += (_, _) => container.Children.Remove(c);
                c.BeginAnimation(UIElement.OpacityProperty, anim);
            }

            foreach (var (id, label) in existingLabels)
            {
                if (newSet.Contains(id)) continue;
                var l = label;
                var anim = new DoubleAnimation(l.Opacity, 0, fadeDuration);
                anim.Completed += (_, _) => container.Children.Remove(l);
                l.BeginAnimation(UIElement.OpacityProperty, anim);
            }

            for (int i = 0; i < games.Length; i++)
            {
                string id = games[i];
                double newX = _gridOffset + i * (GameInfoControl.ControlWidth + Gap);

                if (existing.TryGetValue(id, out var control))
                {
                    control.UpdateCompletedState();

                    var existingTT = control.RenderTransform as TranslateTransform;
                    double visualX = Canvas.GetLeft(control) + (existingTT?.X ?? 0);
                    Canvas.SetLeft(control, newX);
                    double deltaX = visualX - newX;
                    if (Math.Abs(deltaX) > 0.5)
                    {
                        var tt = new TranslateTransform(deltaX, 0);
                        control.RenderTransform = tt;
                        tt.BeginAnimation(TranslateTransform.XProperty,
                            new DoubleAnimation(deltaX, 0, moveDuration) { EasingFunction = easing });
                    }

                    if (existingLabels.TryGetValue(id, out var label))
                    {
                        label.Text = subLabelSelector(id) ?? string.Empty;
                        var existingLabelTT = label.RenderTransform as TranslateTransform;
                        double visualLabelX = Canvas.GetLeft(label) + (existingLabelTT?.X ?? 0);
                        Canvas.SetLeft(label, newX);
                        double deltaLabelX = visualLabelX - newX;
                        if (Math.Abs(deltaLabelX) > 0.5)
                        {
                            var tt = new TranslateTransform(deltaLabelX, 0);
                            label.RenderTransform = tt;
                            tt.BeginAnimation(TranslateTransform.XProperty,
                                new DoubleAnimation(deltaLabelX, 0, moveDuration) { EasingFunction = easing });
                        }
                    }
                }
                else
                {
                    var newControl = new GameInfoControl(id) { CacheMode = new BitmapCache(), Opacity = 0 };
                    container.Children.Add(newControl);
                    Canvas.SetLeft(newControl, newX);
                    Canvas.SetTop(newControl, GameInfoControl.ShadowBlurRadius);
                    newControl.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, fadeDuration));

                    var labelText = subLabelSelector(id);
                    if (!string.IsNullOrEmpty(labelText))
                    {
                        var subLabel = new TextBlock
                        {
                            Text = labelText,
                            Tag = id,
                            Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                            FontSize = 11,
                            FontStyle = FontStyles.Italic,
                            Width = GameInfoControl.ControlWidth,
                            TextAlignment = TextAlignment.Center,
                            TextTrimming = TextTrimming.CharacterEllipsis,
                            Opacity = 0,
                        };
                        container.Children.Add(subLabel);
                        Canvas.SetLeft(subLabel, newX);
                        Canvas.SetTop(subLabel, subLabelY);
                        subLabel.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, fadeDuration));
                    }
                }
            }
        }

        /// <summary>
        /// Incrementally updates the User Games grid: games that moved slide to their new
        /// position, new games fade in, and removed games fade out.
        /// </summary>
        void UpdateGridSection(Canvas container, string[] games)
        {
            bool isRatingSortActive = _userGamesSortMode == GamesSortMode.Rating;
            var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
            var moveDuration = new Duration(TimeSpan.FromMilliseconds(MoveDurationMs));
            var fadeDuration = new Duration(TimeSpan.FromMilliseconds(GamePlacementDurationMs));

            var existing = container.Children.OfType<GameInfoControl>().ToDictionary(c => c.Id);
            var newSet = new HashSet<string>(games);

            foreach (var (id, control) in existing)
            {
                if (newSet.Contains(id)) continue;
                var c = control;
                var anim = new DoubleAnimation(c.Opacity, 0, fadeDuration);
                anim.Completed += (_, _) => container.Children.Remove(c);
                c.BeginAnimation(UIElement.OpacityProperty, anim);
            }

            for (int i = 0; i < games.Length; i++)
            {
                string id = games[i];
                double newLeft = _gridOffset + (i % _controlsPerRow) * (GameInfoControl.ControlWidth + Gap);
                double newTop = GameInfoControl.ShadowBlurRadius + (i / _controlsPerRow) * (GameInfoControl.ControlHeight + Gap);

                if (existing.TryGetValue(id, out var control))
                {
                    control.UpdateCompletedState();

                    var existingTT = control.RenderTransform as TranslateTransform;
                    double visualX = Canvas.GetLeft(control) + (existingTT?.X ?? 0);
                    double visualY = Canvas.GetTop(control) + (existingTT?.Y ?? 0);
                    double deltaX = visualX - newLeft;
                    double deltaY = visualY - newTop;
                    Canvas.SetLeft(control, newLeft);
                    Canvas.SetTop(control, newTop);
                    if (Math.Abs(deltaX) > 0.5 || Math.Abs(deltaY) > 0.5)
                    {
                        var tt = new TranslateTransform(deltaX, deltaY);
                        control.RenderTransform = tt;
                        tt.BeginAnimation(TranslateTransform.XProperty,
                            new DoubleAnimation(deltaX, 0, moveDuration) { EasingFunction = easing });
                        tt.BeginAnimation(TranslateTransform.YProperty,
                            new DoubleAnimation(deltaY, 0, moveDuration) { EasingFunction = easing });
                    }
                }
                else
                {
                    var newControl = new GameInfoControl(id, isRatingSortActive) { CacheMode = new BitmapCache(), Opacity = 0 };
                    container.Children.Add(newControl);
                    Canvas.SetLeft(newControl, newLeft);
                    Canvas.SetTop(newControl, newTop);
                    newControl.BeginAnimation(UIElement.OpacityProperty,
                        new DoubleAnimation(0, 1, fadeDuration) { BeginTime = TimeSpan.FromMilliseconds(i * GamePlacementDelayMs) });
                }
            }
        }

        GameInfoControl[] FindGameControls(string id)
        {
            // Collect every occurrence of this game across all sections so the snake
            // runs simultaneously on all visible tiles (a game can appear in both a
            // horizontal strip and the user grid at the same time).
            return NewGamesContainer.Children.OfType<GameInfoControl>().Where(c => c.Id == id)
                .Concat(RecentGamesContainer.Children.OfType<GameInfoControl>().Where(c => c.Id == id))
                .Concat(UserGamesContainer.Children.OfType<GameInfoControl>()
                    .Where(c => c.Id == id && c.Visibility == Visibility.Visible))
                .ToArray();
        }

        internal void RefreshAllSections()
        {
            var newGames = GetNewGames();
            var recentGames = GetRecentGames();
            var userGames = GetUserGames();

            NewGamesOrderDirectionToggle.ToolTip = $"{newGames.Length} {(newGames.Length == 1 ? "game" : "games")}";
            RecentGamesOrderDirectionToggle.ToolTip = $"{recentGames.Length} {(recentGames.Length == 1 ? "game" : "games")}";
            UserGamesOrderDirectionToggle.ToolTip = $"{userGames.Length} {(userGames.Length == 1 ? "game" : "games")}";

            UpdateHorizontalSection(NewGamesContainer, newGames, id => $"added {TimeAgo(GameLibrary.Instance.Games[id].Added)}");

            if (newGames.Length > 0 && _newGamesCollapsed)
            {
                _newGamesCollapsed = false;
                ApplyNewGamesState();
            }
            else if (newGames.Length == 0 && !_newGamesCollapsed)
            {
                _newGamesCollapsed = true;
                ApplyNewGamesState();
            }

            UpdateHorizontalSection(RecentGamesContainer, recentGames, id => $"played {TimeAgo(GameLibrary.Instance.Games[id].LastPlayed!.Value)}");

            if (recentGames.Length > 0 && _recentGamesCollapsed)
            {
                _recentGamesCollapsed = false;
                ApplyRecentGamesState();
            }
            else if (recentGames.Length == 0 && !_recentGamesCollapsed)
            {
                _recentGamesCollapsed = true;
                ApplyRecentGamesState();
            }

            UpdateGridSection(UserGamesContainer, userGames);

            RootGrid.UpdateLayout();

            double screenWidth = RootGrid.ActualWidth;
            double HorizontalContentWidth(int n) => n > 0 ? 2 * _gridOffset + n * (GameInfoControl.ControlWidth + Gap) - Gap : 0;

            _newGamesMaxScrollX = Math.Max(0, HorizontalContentWidth(newGames.Length) - screenWidth);
            _recentGamesMaxScrollX = Math.Max(0, HorizontalContentWidth(recentGames.Length) - screenWidth);
            _newGamesOffsetX = 0; _newGamesVelocityX = 0; _newGamesTransform.X = 0;
            _lastPlayedOffsetX = 0; _lastPlayedVelocityX = 0; _lastPlayedTransform.X = 0;

            _userGamesMaxScrollY = Math.Max(0, GridContentHeight(userGames.Length) - UserGamesCanvas.ActualHeight + _gridOffset);
            _allGamesOffsetY = Math.Min(_allGamesOffsetY, _userGamesMaxScrollY);
            _allGamesVelocityY = 0;
            _allGamesTransform.Y = -_allGamesOffsetY;

            var newUserGamesSet = new HashSet<string>(userGames);
            _visibleControls = UserGamesContainer.Children.OfType<GameInfoControl>()
                .Where(c => newUserGamesSet.Contains(c.Id))
                .Select(c => (Control: c, LocalTop: Canvas.GetTop(c)))
                .ToArray();

            UpdateViewportCulling();
            UpdateScrollThumbs();
        }

        static T? FindAncestorOrSelf<T>(DependencyObject obj) where T : DependencyObject
        {
            while (obj is not null)
            {
                if (obj is T t) return t;
                obj = VisualTreeHelper.GetParent(obj);
            }
            return null;
        }

        void UserGamesLabel_Click(object sender, MouseButtonEventArgs e)
        {
            UpdateFilterOptionHighlight();
            ShowDropdown(UserGamesFilterPanel, UserGamesLabel);
        }

        static string GetGenreItemText(string genre, int count, bool selected) => (selected ? "\u2713  " : "    ") + $"{genre} ({count})";

        void UserGamesGenresLabel_Click(object sender, MouseButtonEventArgs e)
        {
            UserGamesGenresContainer.Children.Clear();

            var genres = GameLibrary.Instance.Genres.ToArray();
            if (genres.Length == 0)
            {
                ShowDropdown(UserGamesGenresPanel, UserGamesGenresLabel);
                return;
            }

            const int maxPerColumn = 10;
            var style = (Style)Resources["DropdownItemStyle"];

            var genresWithCounts = GameLibrary.Instance.GenresWithCounts.OrderByDescending(x => x.Value);

            // Build all items first so we can measure for a uniform column width
            var items = genresWithCounts.Select(genre =>
            {
                bool selected = _userGamesGenreFilter.Contains(genre.Key);
                var item = new TextBlock
                {
                    Text = GetGenreItemText(genre.Key, genre.Value, selected),
                    Tag = genre.Key,
                    Style = style,
                    Foreground = selected ? Brushes.LightSkyBlue : Brushes.White,
                };
                item.MouseLeftButtonUp += UserGamesGenre_Click;
                return item;
            }).ToArray();

            // Measure every item so all columns get the same width
            double columnWidth = 0;
            foreach (var item in items)
            {
                item.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                columnWidth = Math.Max(columnWidth, item.DesiredSize.Width);
            }

            // Arrange into evenly distributed columns (max maxPerColumn items each)
            int columnCount = (int)Math.Ceiling(genres.Length / (double)maxPerColumn);
            int itemsPerColumn = (int)Math.Ceiling(genres.Length / (double)columnCount);
            for (int col = 0; col < columnCount; col++)
            {
                var column = new StackPanel { Width = columnWidth };
                for (int row = 0; row < itemsPerColumn; row++)
                {
                    int idx = col * itemsPerColumn + row;
                    if (idx >= items.Length) break;
                    items[idx].Width = columnWidth;
                    column.Children.Add(items[idx]);
                }
                UserGamesGenresContainer.Children.Add(column);
            }

            ShowDropdown(UserGamesGenresPanel, UserGamesGenresLabel);
        }

        void UserGamesGenre_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is not TextBlock tb || tb.Tag is not string genre)
                return;

            if (!_userGamesGenreFilter.Remove(genre))
                _userGamesGenreFilter.Add(genre);

            bool selected = _userGamesGenreFilter.Contains(genre);
            tb.Text = GetGenreItemText(genre, GameLibrary.Instance.GenresWithCounts[genre], selected);
            tb.Foreground = selected ? Brushes.LightSkyBlue : Brushes.White;

            UpdateGenresLabel();
            RefreshUserGames();

            AppSettings.Instance.UserGamesGenreFilter = [.. _userGamesGenreFilter];
            e.Handled = true;
        }

        void UpdateGenresLabel()
        {
            UserGamesGenresLabel.Text = _userGamesGenreFilter.Count == 0 ? "All genres" : string.Join("  |  ", _userGamesGenreFilter);
        }

        /// <summary>
        /// Applies the selected filter mode from the dropdown and refreshes the User Games grid.
        /// </summary>
        void UserGamesFilter_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is TextBlock tb && tb.Tag is string tag && Enum.TryParse<UserGamesFilterMode>(tag, out var mode))
            {
                _userGamesFilterMode = mode;
                HideDropdowns();
                RefreshUserGames();

                AppSettings.Instance.UserGamesFilterMode = _userGamesFilterMode;
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
            FilterOptionCompleted.Foreground = _userGamesFilterMode == UserGamesFilterMode.Completed ? Brushes.LightSkyBlue : Brushes.White;
            FilterOptionMissingData.Foreground = _userGamesFilterMode == UserGamesFilterMode.MissingData ? Brushes.LightSkyBlue : Brushes.White;
            FilterOptionSteam.Foreground = _userGamesFilterMode == UserGamesFilterMode.Steam ? Brushes.LightSkyBlue : Brushes.White;
            FilterOptionIgdb.Foreground = _userGamesFilterMode == UserGamesFilterMode.Igdb ? Brushes.LightSkyBlue : Brushes.White;
            FilterOptionAll.Foreground = _userGamesFilterMode == UserGamesFilterMode.All ? Brushes.LightSkyBlue : Brushes.White;
        }

        void UpdateSortOptionHighlight()
        {
            SortOptionTitle.Foreground = _userGamesSortMode == GamesSortMode.Title ? Brushes.LightSkyBlue : Brushes.White;
            SortOptionAdded.Foreground = _userGamesSortMode == GamesSortMode.Added ? Brushes.LightSkyBlue : Brushes.White;
            SortOptionCompleted.Foreground = _userGamesSortMode == GamesSortMode.Completed ? Brushes.LightSkyBlue : Brushes.White;
            SortOptionPlayed.Foreground = _userGamesSortMode == GamesSortMode.Played ? Brushes.LightSkyBlue : Brushes.White;
            SortOptionRating.Foreground = _userGamesSortMode == GamesSortMode.Rating ? Brushes.LightSkyBlue : Brushes.White;
        }

        void UserGamesOrderLabel_Click(object sender, MouseButtonEventArgs e)
        {
            UpdateSortOptionHighlight();
            ShowDropdown(UserGamesOrderPanel, UserGamesOrderLabel);
        }

        void UserGamesOrder_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is TextBlock tb && tb.Tag is string tag && Enum.TryParse<GamesSortMode>(tag, out var mode))
            {
                _userGamesSortMode = mode;
                HideDropdowns();
                UserGamesOrderLabel.Text = _userGamesSortMode.ToString();
                RefreshUserGames();

                AppSettings.Instance.UserGamesSortMode = _userGamesSortMode;
            }
        }

        void UserGamesOrderDirectionToggle_Click(object sender, MouseButtonEventArgs e)
        {
            _userGamesSortDescending = !_userGamesSortDescending;
            UserGamesOrderDirectionToggle.Text = _userGamesSortDescending ? "\u25BC" : "\u25B2";
            RefreshUserGames();

            AppSettings.Instance.UserGamesSortDescending = _userGamesSortDescending;
        }

        void UserGamesTitleFilter_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(UserGamesTitleFilter.Text) && string.IsNullOrWhiteSpace(_userGamesTitleFilter))
                return;

            if (!string.IsNullOrWhiteSpace(UserGamesTitleFilter.Text) && !string.IsNullOrWhiteSpace(_userGamesTitleFilter))
            {
                var newFilter = UserGamesTitleFilter.Text.ToLower().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var oldFilter = _userGamesTitleFilter.ToLower().Split(' ', StringSplitOptions.RemoveEmptyEntries);

                // filter not changed if the same set of words is present in both the old and new filter, regardless of order or duplicates
                if (newFilter.Length == oldFilter.Length && newFilter.All(x => oldFilter.Contains(x)))
                    return;
            }

            _userGamesTitleFilter = UserGamesTitleFilter.Text;

            RefreshUserGames();

            if (UserGamesTitleFilter.Text.Length == 0)
                HideFilterTextBox();
        }

        /// <summary>
        /// Re-queries the game library using the active filter, repopulates the User Games grid,
        /// and resets scroll state and viewport culling.
        /// </summary>
        void RefreshUserGames()
        {
            var userGames = GetUserGames();
            UserGamesOrderDirectionToggle.ToolTip = $"{userGames.Length} {(userGames.Length == 1 ? "game" : "games")}";

            UserGamesContainer.Children.Clear();
            PopulateGridSection(UserGamesContainer, userGames);

            UserGamesLabel.Text = GetUserGamesLabelText(_userGamesFilterMode, userGames.Length);
            UpdateGenresLabel();

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

        static string GetUserGamesLabelText(UserGamesFilterMode userGamesFilterMode, int length) =>
            //userGamesFilterMode.ToString() + (length > 0 ? $" ({length})" : null);
            userGamesFilterMode.ToString();// + (length > 0 ? $" ({length})" : null);   

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
            bool show = !_newGamesCollapsed;
            NewGamesSection.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            NewGamesCanvas.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            NewGamesScrollThumb.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            CollapsedNewLabel.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
            UpdateUserGamesHeaderMargin();
            UpdateRecentGamesHeaderMargin();
        }

        void ApplyRecentGamesState()
        {
            bool show = !_recentGamesCollapsed;
            RecentGamesSection.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            RecentGamesCanvas.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            RecentGamesScrollThumb.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            CollapsedRecentLabel.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
            UpdateUserGamesHeaderMargin();
        }

        void UpdateUserGamesHeaderMargin()
        {
            double top = _newGamesCollapsed && _recentGamesCollapsed ? 32 : 16;
            UserGamesHeaderGrid.Margin = new Thickness(32, top, 32, 0);
        }

        void UpdateRecentGamesHeaderMargin()
        {
            double top = _newGamesCollapsed ? 32 : 16;
            RecentGamesHeaderGrid.Margin = new Thickness(32, top, 32, 0);
        }

        void UpdateUserGamesHeaderControls()
        {
            UserGamesLabel.IsHitTestVisible = true;
            UserGamesLabel.Opacity = 1.0;
            UserGamesOrderLabel.IsHitTestVisible = true;
            UserGamesOrderLabel.Opacity = 1.0;
            UserGamesOrderDirectionToggle.IsHitTestVisible = true;
            UserGamesOrderDirectionToggle.Opacity = 0.5;
            UserGamesGenresLabel.IsHitTestVisible = true;
            UserGamesGenresLabel.Opacity = 1.0;
            if (UserGamesTitleFilter.Text.Length > 0)
                ShowFilterTextBox();
        }

        void ShowFilterTextBox()
        {
            UserGamesTitleFilter.Visibility = Visibility.Visible;
            UserGamesTitleFilter.Focus();
        }

        void HideFilterTextBox()
        {
            UserGamesTitleFilter.Visibility = Visibility.Collapsed;
            Focus();
        }

        void Window_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            if (UserGamesTitleFilter.Visibility != Visibility.Visible
                && (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) == ModifierKeys.None
                && e.Text.All(c => !char.IsControl(c) && !char.IsWhiteSpace(c)))
            {
                UserGamesTitleFilter.Visibility = Visibility.Visible;
                UserGamesTitleFilter.Focus();
                UserGamesTitleFilter.Text = e.Text;
                UserGamesTitleFilter.CaretIndex = UserGamesTitleFilter.Text.Length;
                e.Handled = true;
            }
        }

        void ApplyUserGamesState()
        {
            UpdateUserGamesHeaderControls();
        }

        static DoubleAnimation FadeAnimation(double from, double to) =>
            new(from, to, new Duration(TimeSpan.FromMilliseconds(GroupFadeAnimationDuration)));

        /// <summary>
        /// Toggles a group section between expanded and collapsed states with a fade animation.
        /// </summary>
        /// <param name="getCollapsed">Returns the current collapsed state of the section.</param>
        /// <param name="setCollapsed">Sets the collapsed state of the section.</param>
        /// <param name="canvas">Content canvas to fade in or out.</param>
        /// <param name="scrollThumb">Scroll thumb indicator to show or hide.</param>
        /// <param name="divider">Divider shown when collapsed, hidden when expanded.</param>
        /// <param name="canvasRow">Optional grid row whose height is set to 0 / Star alongside visibility.</param>
        /// <param name="onExpand">Optional callback invoked immediately when the section is expanding.</param>
        /// <param name="onCollapse">Optional callback invoked immediately when the section is collapsing.</param>
        /// <param name="onViewportChange">Optional callback invoked after expand or after the collapse animation completes.</param>
        static void ToggleGroupSection(Func<bool> getCollapsed, Action<bool> setCollapsed, Canvas canvas, Rectangle scrollThumb,
            RowDefinition? canvasRow = null, Action? onExpand = null, Action? onCollapse = null, Action? onViewportChange = null)
        {
            bool collapsed = !getCollapsed();
            setCollapsed(collapsed);

            if (collapsed)
            {
                onCollapse?.Invoke();
                var anim = FadeAnimation(canvas.Opacity, 0);
                anim.Completed += (s, ea) =>
                {
                    if (!getCollapsed()) return;
                    canvas.BeginAnimation(UIElement.OpacityProperty, null);
                    canvas.Visibility = Visibility.Collapsed;
                    if (canvasRow != null) canvasRow.Height = new GridLength(0);
                    scrollThumb.Visibility = Visibility.Collapsed;
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
                scrollThumb.Visibility = Visibility.Visible;
                canvas.BeginAnimation(UIElement.OpacityProperty, FadeAnimation(fromOpacity, 1));
                onViewportChange?.Invoke();
            }
        }

        void ToggleNewGames()
        {
            ToggleGroupSection(() => _newGamesCollapsed, v => _newGamesCollapsed = v,
                NewGamesCanvas,
                NewGamesScrollThumb,
                onExpand: () =>
                {
                    _newGamesOffsetX = 0; _newGamesVelocityX = 0; _newGamesTransform.X = 0;
                    CollapsedNewLabel.Visibility = Visibility.Collapsed;
                    NewGamesSection.Visibility = Visibility.Visible;
                    UpdateUserGamesHeaderMargin();
                    UpdateRecentGamesHeaderMargin();
                },
                onViewportChange: () =>
                {
                    if (_newGamesCollapsed)
                    {
                        NewGamesSection.Visibility = Visibility.Collapsed;
                        CollapsedNewLabel.Visibility = Visibility.Visible;
                    }
                    UpdateUserGamesHeaderMargin();
                    UpdateRecentGamesHeaderMargin();
                    RefreshUserGamesViewport();
                });

            AppSettings.Instance.NewGamesCollapsed = _newGamesCollapsed;
        }

        void RefreshNewGames()
        {
            var newGames = GetNewGames();
            NewGamesOrderDirectionToggle.ToolTip = $"{newGames.Length} {(newGames.Length == 1 ? "game" : "games")}";
            NewGamesContainer.Children.Clear();
            PopulateHorizontalSection(NewGamesContainer, newGames,
                id => $"added {TimeAgo(GameLibrary.Instance.Games[id].Added)}");

            double screenWidth = RootGrid.ActualWidth;
            double HorizontalContentWidth(int n) => n > 0 ? 2 * _gridOffset + n * (GameInfoControl.ControlWidth + Gap) - Gap : 0;
            _newGamesMaxScrollX = Math.Max(0, HorizontalContentWidth(newGames.Length) - screenWidth);
            _newGamesOffsetX = 0; _newGamesVelocityX = 0; _newGamesTransform.X = 0;
            UpdateScrollThumbs();
        }

        void NewGamesOrderDirectionToggle_Click(object sender, MouseButtonEventArgs e)
        {
            _newGamesSortDescending = !_newGamesSortDescending;
            NewGamesOrderDirectionToggle.Text = _newGamesSortDescending ? "\u25BC" : "\u25B2";
            RefreshNewGames();

            AppSettings.Instance.NewGamesSortDescending = _newGamesSortDescending;
        }

        void ToggleRecentGames()
        {
            ToggleGroupSection(() => _recentGamesCollapsed, v => _recentGamesCollapsed = v,
                RecentGamesCanvas,
                RecentGamesScrollThumb,
                onExpand: () =>
                {
                    _lastPlayedOffsetX = 0; _lastPlayedVelocityX = 0; _lastPlayedTransform.X = 0;
                    CollapsedRecentLabel.Visibility = Visibility.Collapsed;
                    RecentGamesSection.Visibility = Visibility.Visible;
                    UpdateUserGamesHeaderMargin();
                },
                onViewportChange: () =>
                {
                    if (_recentGamesCollapsed)
                    {
                        RecentGamesSection.Visibility = Visibility.Collapsed;
                        CollapsedRecentLabel.Visibility = Visibility.Visible;
                    }
                    UpdateUserGamesHeaderMargin();
                    RefreshUserGamesViewport();
                });

            AppSettings.Instance.RecentGamesCollapsed = _recentGamesCollapsed;
        }

        void RefreshRecentGames()
        {
            var recentGames = GetRecentGames();
            RecentGamesOrderDirectionToggle.ToolTip = $"{recentGames.Length} {(recentGames.Length == 1 ? "game" : "games")}";
            RecentGamesContainer.Children.Clear();
            PopulateHorizontalSection(RecentGamesContainer, recentGames,
                id => $"played {TimeAgo(GameLibrary.Instance.Games[id].LastPlayed!.Value)}");

            double screenWidth = RootGrid.ActualWidth;
            double HorizontalContentWidth(int n) => n > 0 ? 2 * _gridOffset + n * (GameInfoControl.ControlWidth + Gap) - Gap : 0;
            _recentGamesMaxScrollX = Math.Max(0, HorizontalContentWidth(recentGames.Length) - screenWidth);
            _lastPlayedOffsetX = 0; _lastPlayedVelocityX = 0; _lastPlayedTransform.X = 0;
            UpdateScrollThumbs();
        }

        void RecentGamesInstalledOnlyToggle_Click(object sender, MouseButtonEventArgs e)
        {
            _recentGamesInstalledOnly = !_recentGamesInstalledOnly;
            UpdateRecentGamesInstalledOnlyToggle();
            RefreshRecentGames();

            AppSettings.Instance.RecentGamesInstalledOnly = _recentGamesInstalledOnly;
        }

        void UpdateRecentGamesInstalledOnlyToggle()
        {
            RecentGamesInstalledOnlyToggle.Tag = _recentGamesInstalledOnly ? null : "inactive";
        }

        void RecentGamesOrderDirectionToggle_Click(object sender, MouseButtonEventArgs e)
        {
            _recentGamesSortDescending = !_recentGamesSortDescending;
            RecentGamesOrderDirectionToggle.Text = _recentGamesSortDescending ? "\u25BC" : "\u25B2";
            RefreshRecentGames();

            AppSettings.Instance.RecentGamesSortDescending = _recentGamesSortDescending;
        }

        void NewGamesLabel_Click(object sender, MouseButtonEventArgs e) => ToggleNewGames();

        void RecentGamesLabel_Click(object sender, MouseButtonEventArgs e) => ToggleRecentGames();

        void CollapsedNewLabel_Click(object sender, MouseButtonEventArgs e) => ToggleNewGames();

        void CollapsedRecentLabel_Click(object sender, MouseButtonEventArgs e) => ToggleRecentGames();
    }
}
