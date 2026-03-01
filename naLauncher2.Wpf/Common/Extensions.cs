namespace naLauncher2.Wpf.Common
{
    internal static class Extensions
    {
        public static string Normalize(this string s)
        {
            //var after = before.ToLower()
            //	.Replace("’s ", "s ").Replace("’n ", "n ").Replace("’t ", "t ")
            //	.Replace(" ’", " ").Replace("’ ", " ").Replace("’", "")
            //	.Replace("'s ", "s ").Replace("'n ", "n ").Replace("'t ", "t ")
            //	.Replace(" '", " ").Replace("' ", " ").Replace("'", "")
            //	.Replace("²", " ").Replace("®", " ").Replace("™", " ")
            //	.Replace("\"", " ").Replace("“", " ").Replace("”", " ")
            //	.Replace("/", " ").Replace("\\", " ")
            //	.Replace("=", " ").Replace("?", " ").Replace("!", " ").Replace("&", " ").Replace(":", " ").Replace(" and ", " ").Replace("-", " ").Replace(".", " ").Replace(",", "").Replace("_", " ").Replace("(", " ").Replace(")", " ").Replace("[", " ").Replace("]", " ")
            //	.Replace("    ", " ").Replace("   ", " ").Replace("  ", " ")
            //	.Replace(" ", "")
            //	.Trim();

            var after = string.Empty;
            foreach (var ch in s.ToLower().Replace("&", string.Empty).Replace(" and ", string.Empty))
                if (char.IsLetterOrDigit(ch))
                    after += ch;

            after = after.Replace('ü', 'u');

            return after;
        }

        /// <summary>
        /// https://gist.github.com/wickedshimmy/449595/cb33c2d0369551d1aa5b6ff5e6a802e21ba4ad5c
        /// </summary>
        /// <param name="original"></param>
        /// <param name="modified"></param>
        /// <returns></returns>
        public static int DamerauLevenshteinEditDistance(string original, string modified)
        {
            var len_orig = original.Length;
            var len_diff = modified.Length;

            var matrix = new int[len_orig + 1, len_diff + 1];
            for (int i = 0; i <= len_orig; i++)
                matrix[i, 0] = i;
            for (int j = 0; j <= len_diff; j++)
                matrix[0, j] = j;

            for (int i = 1; i <= len_orig; i++)
            {
                for (int j = 1; j <= len_diff; j++)
                {
                    int cost = modified[j - 1] == original[i - 1] ? 0 : 1;
                    var vals = new int[] {
                        matrix[i - 1, j] + 1,
                        matrix[i, j - 1] + 1,
                        matrix[i - 1, j - 1] + cost
                    };
                    matrix[i, j] = vals.Min();
                    if (i > 1 && j > 1 && original[i - 1] == modified[j - 2] && original[i - 2] == modified[j - 1])
                        matrix[i, j] = Math.Min(matrix[i, j], matrix[i - 2, j - 2] + cost);
                }
            }

            return matrix[len_orig, len_diff];
        }
    }
}
