using PuppeteerSharp;
using System.Text.RegularExpressions;

namespace naLauncher2.Wpf.Api
{
	/// <summary>
	/// Simple web scraper with client-side rendering support via headless Chromium (PuppeteerSharp).
	/// </summary>
	public class WebScraper
	{
		static readonly SemaphoreSlim _initLock = new(1, 1);
		static bool _browserDownloaded;

		/// <summary>
		/// Downloads the Chromium browser revision if not already present.
		/// </summary>
		static async Task EnsureBrowserAsync()
		{
			if (_browserDownloaded)
				return;

			await _initLock.WaitAsync();
			try
			{
				if (!_browserDownloaded)
				{
					await new BrowserFetcher().DownloadAsync();
					_browserDownloaded = true;
				}
			}
			finally
			{
				_initLock.Release();
			}
		}

		/// <summary>
		/// Navigates to <paramref name="url"/> using a headless browser, waits for client-side
		/// rendering to settle (network idle), and returns the final HTML of the page.
		/// </summary>
		/// <param name="url">The fully-qualified URL to render.</param>
		/// <returns>The rendered HTML, or <see langword="null"/> on failure.</returns>
		public static async Task<string?> RenderAsync(string url)
		{
			await EnsureBrowserAsync();

			await using var browser = await Puppeteer.LaunchAsync(new LaunchOptions { Headless = true });
			await using var page = await browser.NewPageAsync();
			await page.GoToAsync(url, new NavigationOptions
			{
				WaitUntil = [WaitUntilNavigation.Networkidle0],
			});

			return await page.GetContentAsync();
		}

		/// <summary>
		/// Searches <paramref name="html"/> with the given regex <paramref name="pattern"/> and
		/// returns the value of the first capture group, or the full match when no groups are defined.
		/// Returns <see langword="null"/> when the pattern does not match.
		/// </summary>
		/// <param name="html">The HTML to search.</param>
		/// <param name="pattern">A .NET regular expression. Use a capture group to isolate the desired value.</param>
		public static string? ExtractValue(string html, string pattern)
		{
			var match = Regex.Match(html, pattern, RegexOptions.Singleline);
			if (!match.Success)
				return null;

			return match.Groups.Count > 1 ? match.Groups[1].Value : match.Value;
		}

		/// <summary>
		/// Extracts the inner HTML of all elements with a given tag name and optional class names.
		/// </summary>
		/// <param name="html">The HTML markup to search.</param>
		/// <param name="elementName">The tag name of the HTML elements to search for (e.g., "span", "div").</param>
		/// <param name="classList">An optional list of class names. Only elements containing all specified classes will be matched. If no classes are
		/// provided, all elements with the specified tag name are considered.</param>
		/// <returns>An array of inner HTML strings from all matching elements. The array is empty if no matches are found.</returns>
		public static string[] ExtractValues(string? html, string elementName, params string[] classList)
		{
			if (string.IsNullOrWhiteSpace(html))
				return [];

			var escaped = Regex.Escape(elementName);
			var classLookaheads = string.Join("", classList.Select(c => $@"(?=[^>]*\bclass=""[^""]*\b{Regex.Escape(c)}\b)"));
			// Balancing groups track nested same-name elements so the closing tag is matched
			// at the correct depth, not at the first </elementName> encountered.
			var pattern = $@"<{escaped}\b{classLookaheads}[^>]*>" +
				$@"((?:[^<]|<(?!/?{escaped}\b)|(?<open><{escaped}\b[^>]*>)|(?<-open></{escaped}>))*(?(open)(?!)))" +
				$@"</{escaped}>";
			return [.. Regex.Matches(html, pattern, RegexOptions.Singleline)
				.Select(m => m.Groups[1].Value)];
		}

        /// <summary>
        /// Applies each pattern in <paramref name="patterns"/> to <paramref name="html"/> via
        /// <see cref="ExtractValue"/> and returns the results in the same order.
        /// </summary>
        /// <param name="html">The HTML to search.</param>
        /// <param name="patterns">.NET regular expressions to apply. Use capture groups to isolate desired values.</param>
        /// <returns>An array where each element is the extracted value, or <see langword="null"/> if the pattern did not match.</returns>
        public static string?[] ExtractValues(string html, params string[] patterns)
			=> [.. patterns.Select(p => ExtractValue(html, p))];

		/// <summary>
		/// Returns all values of the specified XML/HTML attribute whose value starts with
		/// <paramref name="valueStartsWith"/>.
		/// </summary>
		/// <param name="html">The HTML to search.</param>
		/// <param name="attributeName">The attribute name to look for (e.g. <c>data-ds-appid</c>).</param>
		/// <param name="valueStartsWith">Only values that begin with this string are returned. Pass an empty string to return all values.</param>
		/// <returns>An array of matched attribute values.</returns>
		public static string[] ExtractAllAttributeValues(string html, string attributeName, string valueStartsWith = "")
		{
			var pattern = $@"{Regex.Escape(attributeName)}=""({Regex.Escape(valueStartsWith)}[^""]*)""";
			return [.. Regex.Matches(html, pattern, RegexOptions.Singleline)
				.Select(m => m.Groups[1].Value)];
		}

		/// <summary>
		/// Extracts the values of a specified attribute from all HTML elements with a given tag name and optional class
		/// names.
		/// </summary>
		/// <remarks>The search is case-sensitive and does not parse malformed HTML. Use this method for simple
		/// extraction scenarios where performance and strict HTML compliance are not critical.</remarks>
		/// <param name="html">The HTML markup to search for matching elements.</param>
		/// <param name="attributeName">The name of the attribute whose values are to be extracted.</param>
		/// <param name="elementName">The tag name of the HTML elements to search for (e.g., "a", "div").</param>
		/// <param name="classList">An optional list of class names. Only elements containing all specified classes will be matched. If no classes are
		/// provided, all elements with the specified tag name are considered.</param>
		/// <returns>An array of strings containing the values of the specified attribute from all matching elements. The array is
		/// empty if no matches are found.</returns>
		public static string[] ExtractAllAttributeValues(string html, string attributeName, string elementName, params string[] classList)
		{
			var classLookaheads = string.Join("", classList.Select(c => $@"(?=[^>]*\bclass=""[^""]*\b{Regex.Escape(c)}\b)"));
			var pattern = $@"<{Regex.Escape(elementName)}\b{classLookaheads}[^>]*\b{Regex.Escape(attributeName)}=""([^""]*)""[^>]*>";
			return [.. Regex.Matches(html, pattern, RegexOptions.Singleline)
				.Select(m => m.Groups[1].Value)];
		}
	}
}
