namespace naLauncher2.Wpf.Tools
{
    internal static class StringExtensions
    {
        public static string NormalizeCustom(this string s)
        {
            var after = string.Empty;
            foreach (var ch in s.ToLower().Replace("&", string.Empty).Replace(" and ", string.Empty))
                if (char.IsLetterOrDigit(ch))
                    after += ch;

            after = after.Replace('ü', 'u');

            return after;
        }
    }
}
