using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace naLauncher2.Wpf
{
    /// <summary>
    /// Working copy of one session in the list. <see cref="Original"/> is null for sessions added by hand.
    /// A record so the sessions dialog can edit copies and drop them on Cancel.
    /// </summary>
    internal sealed record SessionRow
    {
        public Session? Original { get; init; }
        public DateTime Start { get; init; }
        public TimeSpan? Duration { get; set; }
        public bool Deleted { get; set; }
        public bool Invalid { get; set; }
        public bool Running { get; init; }
    }

    /// <summary>
    /// Lists and edits a game's sessions. Edits stay in <see cref="Rows"/>; the properties dialog applies them on its Save.
    /// </summary>
    public partial class SessionsDialog : Window
    {
        internal List<SessionRow> Rows { get; }
        readonly bool _readOnly;

        internal SessionsDialog(string title, IEnumerable<SessionRow> rows, bool readOnly = false)
        {
            Rows = rows.Select(r => r with { }).ToList();
            _readOnly = readOnly;

            InitializeComponent();

            MaxHeight = SystemParameters.WorkArea.Height - 40;
            TitleText.Text = $"Sessions – {title}";
            NewSessionStartBox.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm");

            if (readOnly)
            {
                AddRow.Visibility = Visibility.Collapsed;
                CancelButton.Text = "Close";
            }

            BuildSessionRows();

            Loaded += (_, _) => WindowDimHelper.Dim(Owner);
            Closed += (_, _) => WindowDimHelper.Undim(Owner);
        }

        void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();

        void UpdateSummary()
        {
            var visible = Rows.Where(r => !r.Deleted).ToArray();
            var measured = visible.Where(r => r.Duration.HasValue).ToArray();
            int unmeasured = visible.Count(r => !r.Duration.HasValue && !r.Running);

            var parts = new List<string> { $"{visible.Length} session{(visible.Length != 1 ? "s" : "")}" };
            if (measured.Length > 0)
                parts.Add($"{PlayTimeFormat.Format(TimeSpan.FromTicks(measured.Sum(r => r.Duration!.Value.Ticks)))} measured in {measured.Length}");
            if (unmeasured > 0)
                parts.Add($"{unmeasured} unmeasured");
            if (visible.Length > 0)
                parts.Add($"{visible.Min(r => r.Start):yyyy-MM-dd} – {visible.Max(r => r.Start):yyyy-MM-dd}");

            SummaryText.Text = string.Join("  ·  ", parts);
            // a session duration that doesn't parse blocks OK rather than being silently dropped
            OkButton.Visibility = _readOnly || visible.Any(r => r.Invalid) ? Visibility.Collapsed : Visibility.Visible;
        }

        /// <summary>
        /// Builds one row per session, newest first: when it was played, its duration (editable;
        /// empty means unmeasured) and a delete button. A session being tracked right now is read-only.
        /// </summary>
        void BuildSessionRows()
        {
            SessionsList.Children.Clear();

            foreach (var row in Rows.Where(r => !r.Deleted).OrderByDescending(r => r.Start))
            {
                var grid = new Grid { Margin = new Thickness(0, 0, 0, 2) };
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var end = row.Duration.HasValue ? row.Original?.End ?? row.Start + row.Duration.Value : (DateTime?)null;
                var via = row.Original?.Via is LaunchVia v ? $"  ·  {v}" : string.Empty;
                var when = new TextBlock
                {
                    Text = $"{row.Start:yyyy-MM-dd HH:mm}{(end.HasValue ? $" – {end.Value:HH:mm}" : string.Empty)}{via}",
                    Foreground = Brushes.White,
                    Opacity = 0.7,
                    FontSize = 12,
                    Padding = new Thickness(0, 2, 0, 2),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                grid.Children.Add(when);

                if (row.Running || _readOnly)
                {
                    var running = new TextBlock
                    {
                        Text = row.Running ? "playing…" : row.Duration.HasValue ? PlayTimeFormat.FormatEditable(row.Duration.Value) : string.Empty,
                        Foreground = row.Running ? Brushes.LightSkyBlue : Brushes.White,
                        FontSize = 12,
                        Padding = new Thickness(4, 2, 4, 2),
                        VerticalAlignment = VerticalAlignment.Center,
                    };
                    Grid.SetColumn(running, 1);
                    grid.Children.Add(running);
                }
                else
                {
                    var duration = new TextBox
                    {
                        Text = row.Duration.HasValue ? PlayTimeFormat.FormatEditable(row.Duration.Value) : string.Empty,
                        ToolTip = "Duration: 1:30, 90 (minutes), 2h, 45m. Empty = unmeasured.",
                        Foreground = row.Invalid ? new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x73)) : Brushes.White,
                        FontSize = 12,
                        Background = Brushes.Transparent,
                        BorderThickness = new Thickness(0, 0, 0, 1),
                        BorderBrush = new SolidColorBrush(Color.FromArgb(0x44, 0xFF, 0xFF, 0xFF)),
                        CaretBrush = Brushes.White,
                        SelectionBrush = Brushes.LightSkyBlue,
                        Padding = new Thickness(4, 2, 4, 2),
                        Margin = new Thickness(4, 0, 0, 0),
                    };
                    duration.TextChanged += (_, _) =>
                    {
                        if (string.IsNullOrWhiteSpace(duration.Text))
                        {
                            row.Duration = null;
                            row.Invalid = false;
                        }
                        else if (PlayTimeFormat.TryParse(duration.Text, out var parsed))
                        {
                            row.Duration = parsed;
                            row.Invalid = false;
                        }
                        else
                            row.Invalid = true;

                        duration.Foreground = row.Invalid ? new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x73)) : Brushes.White;
                        UpdateSummary();
                    };
                    Grid.SetColumn(duration, 1);
                    grid.Children.Add(duration);

                    var delete = new TextBlock
                    {
                        Text = "✕",
                        ToolTip = "Delete this session",
                        Foreground = Brushes.White,
                        Opacity = 0.4,
                        FontSize = 11,
                        Padding = new Thickness(8, 2, 0, 2),
                        Cursor = Cursors.Hand,
                        VerticalAlignment = VerticalAlignment.Center,
                    };
                    delete.MouseEnter += (_, _) => delete.Opacity = 1;
                    delete.MouseLeave += (_, _) => delete.Opacity = 0.4;
                    delete.MouseLeftButtonUp += (_, _) =>
                    {
                        row.Deleted = true;
                        BuildSessionRows();
                    };
                    Grid.SetColumn(delete, 2);
                    grid.Children.Add(delete);
                }

                SessionsList.Children.Add(grid);
            }

            UpdateSummary();
        }

        void AddSession_Click(object sender, MouseButtonEventArgs e) => AddSession();

        void AddSession()
        {
            if (!DateTime.TryParse(NewSessionStartBox.Text.Trim(), out var start) || !PlayTimeFormat.TryParse(NewSessionDurationBox.Text, out var duration))
            {
                new MessageDialog("Error", "Enter a start (yyyy-MM-dd HH:mm) and a duration (e.g. 1:30, 90, 2h, 45m).") { Owner = this }.ShowDialog();
                return;
            }

            Rows.Add(new SessionRow { Start = start, Duration = duration });
            NewSessionDurationBox.Clear();
            BuildSessionRows();
        }

        void Ok_Click(object sender, MouseButtonEventArgs e) => DialogResult = true;

        void Cancel_Click(object sender, MouseButtonEventArgs e) => DialogResult = false;

        void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
                DialogResult = false;
            else if (e.Key == Key.Enter && FocusManager.GetFocusedElement(this) is TextBox tb
                     && (tb == NewSessionStartBox || tb == NewSessionDurationBox))
            {
                AddSession();
                e.Handled = true;
            }
        }
    }
}
