using System.Globalization;
using System.Text.RegularExpressions;

namespace naLauncher2.Core
{
    public static partial class PlayTimeFormat
    {
        /// <summary>
        /// Short form for badges and lists: <c>45 min</c> under an hour, whole hours from there on
        /// (<c>12 h</c>, <c>1 234 h</c>).
        /// </summary>
        public static string Format(TimeSpan duration)
        {
            if (duration.TotalHours < 1)
                return $"{Math.Max(0, (int)duration.TotalMinutes)} min";

            var nfi = (NumberFormatInfo)CultureInfo.InvariantCulture.NumberFormat.Clone();
            nfi.NumberGroupSeparator = " "; // thin space
            return $"{((long)duration.TotalHours).ToString("#,0", nfi)} h";
        }

        /// <summary>
        /// Exact form for editing: <c>h:mm</c>.
        /// </summary>
        public static string FormatEditable(TimeSpan duration) => $"{(int)duration.TotalHours}:{duration.Minutes:00}";

        /// <summary>
        /// Parses a duration typed by the user: <c>1:30</c> (h:mm), <c>90</c> (minutes),
        /// <c>2h</c>, <c>45m</c>, <c>1h 30m</c>, <c>1.5h</c>.
        /// </summary>
        public static bool TryParse(string? text, out TimeSpan duration)
        {
            duration = TimeSpan.Zero;
            text = text?.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(text))
                return false;

            var hm = HoursMinutesRegex().Match(text);
            if (hm.Success)
            {
                duration = TimeSpan.FromHours(int.Parse(hm.Groups[1].Value)) + TimeSpan.FromMinutes(int.Parse(hm.Groups[2].Value));
                return int.Parse(hm.Groups[2].Value) < 60;
            }

            if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int minutes))
            {
                duration = TimeSpan.FromMinutes(minutes);
                return true;
            }

            var units = UnitsRegex().Match(text);
            if (units.Success && (units.Groups[1].Success || units.Groups[2].Success))
            {
                double hours = units.Groups[1].Success ? double.Parse(units.Groups[1].Value.Replace(',', '.'), CultureInfo.InvariantCulture) : 0;
                double mins = units.Groups[2].Success ? double.Parse(units.Groups[2].Value.Replace(',', '.'), CultureInfo.InvariantCulture) : 0;
                duration = TimeSpan.FromHours(hours) + TimeSpan.FromMinutes(mins);
                return true;
            }

            return false;
        }

        [GeneratedRegex(@"^(\d+):(\d{1,2})$")]
        private static partial Regex HoursMinutesRegex();

        [GeneratedRegex(@"^(?:(\d+(?:[.,]\d+)?)\s*h(?:ours?)?)?\s*(?:(\d+(?:[.,]\d+)?)\s*m(?:in(?:utes?)?)?)?$")]
        private static partial Regex UnitsRegex();
    }
}
