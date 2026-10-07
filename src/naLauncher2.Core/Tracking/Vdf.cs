using System.Text;

namespace naLauncher2.Core.Tracking
{
    /// <summary>
    /// Minimal reader for Valve's text KeyValues format (<c>.vdf</c>/<c>.acf</c>):
    /// <c>"key" "value"</c> pairs and <c>"key" { ... }</c> blocks. Keys are case-insensitive,
    /// as Steam isn't consistent about their casing.
    /// </summary>
    internal class VdfNode
    {
        public string? Value { get; }
        public Dictionary<string, VdfNode> Children { get; } = new(StringComparer.OrdinalIgnoreCase);

        VdfNode(string? value = null) => Value = value;

        public VdfNode? this[string key] => Children.TryGetValue(key, out var child) ? child : null;

        /// <summary>
        /// Follows a path of keys, e.g. <c>Get("UserLocalConfigStore", "Software", "Valve")</c>.
        /// </summary>
        public VdfNode? Get(params string[] path)
        {
            VdfNode? node = this;
            foreach (var key in path)
                node = node?[key];
            return node;
        }

        public static VdfNode Parse(string text)
        {
            int pos = 0;
            var root = new VdfNode();
            ParseBlock(text, ref pos, root);
            return root;
        }

        static void ParseBlock(string text, ref int pos, VdfNode block)
        {
            while (true)
            {
                var key = NextToken(text, ref pos);
                if (key is null || key == "}")
                    return;

                var next = NextToken(text, ref pos);
                if (next is null)
                    return;

                if (next == "{")
                {
                    var child = new VdfNode();
                    ParseBlock(text, ref pos, child);
                    block.Children[key] = child;
                }
                else
                    block.Children[key] = new VdfNode(next);
            }
        }

        /// <summary>
        /// Returns the next quoted string, unquoted word, or a brace; null at the end of the text.
        /// </summary>
        static string? NextToken(string text, ref int pos)
        {
            while (pos < text.Length)
            {
                char c = text[pos];

                if (char.IsWhiteSpace(c))
                {
                    pos++;
                    continue;
                }

                if (c == '/' && pos + 1 < text.Length && text[pos + 1] == '/')
                {
                    while (pos < text.Length && text[pos] != '\n')
                        pos++;
                    continue;
                }

                if (c == '{' || c == '}')
                {
                    pos++;
                    return c.ToString();
                }

                if (c == '"')
                {
                    pos++;
                    var sb = new StringBuilder();
                    while (pos < text.Length && text[pos] != '"')
                    {
                        if (text[pos] == '\\' && pos + 1 < text.Length)
                        {
                            pos++;
                            sb.Append(text[pos] switch { 'n' => '\n', 't' => '\t', _ => text[pos] });
                        }
                        else
                            sb.Append(text[pos]);
                        pos++;
                    }
                    pos++;
                    return sb.ToString();
                }

                int start = pos;
                while (pos < text.Length && !char.IsWhiteSpace(text[pos]) && text[pos] != '"' && text[pos] != '{' && text[pos] != '}')
                    pos++;
                return text[start..pos];
            }

            return null;
        }
    }
}
