using System.Net;
using Ujeby.Extensions;

namespace naLauncher2.Wpf.Api
{
    public class SteamClient
    {
        const string _storeUrl = "https://store.steampowered.com";

        const string _imagesDirectory = "SteamDbInfo";

        public static string GetStoreUrl(string appId) => $"{_storeUrl}/app/{appId}";

        const string _searchUrl = "https://store.steampowered.com/search?sort_by=_ASC&term=";

        public static async Task<string?> GetAppId(string gameTitle)
        {
            var html = await WebScraper.RenderAsync(_searchUrl + WebUtility.UrlEncode(gameTitle.ToLower()));
            if (html == null)
                return null;

            var allAppUrls = WebScraper.ExtractAllAttributeValues(html, "href", "https://store.steampowered.com/app/");

            // "https://store.steampowered.com/app/4109130/Phonopolis_Demo/?snr=1_7_7_151_150_1"

            var normalizedGameTitle = gameTitle.NormalizeCustom();

            var exactMatches = allAppUrls
                .Select(url => 
                    new
                    {
                        AppId = url.Split('/')[4],
                        Title = url.Split('/')[5].Replace('_', ' ')
                    })
                .Where(x => x.Title.NormalizeCustom().Equals(normalizedGameTitle, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (exactMatches.Length == 1)
                return exactMatches[0].AppId;

            if (exactMatches.Length > 1)
                Log.WriteLine($"{exactMatches.Length} exact Steam matches found for '{gameTitle}'");

            return null;
        }
    }
}
