using System.IO;
using System.Net;
using Ujeby.Extensions;

namespace naLauncher2.Wpf.Api
{
    public record class SteamGameData(string Title)
    {
        public string? Id { get; init; }
        public int? MetacriticScore { get; init; }
        public string? ImagePath { get; init; }
        public string? Description { get; init; }
    }

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

            // https://store.steampowered.com/app/4109130/...

            var normalizedGameTitle = gameTitle.NormalizeCustom().Replace("demo", string.Empty);

            var exactMatches = allAppUrls
                .Select(url => 
                    new
                    {
                        AppId = url.Split('/')[4],
                        Title = url.Split('/')[5].Replace('_', ' ')
                    })
                .Where(x => x.Title.NormalizeCustom().Replace("demo", string.Empty).Equals(normalizedGameTitle, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            exactMatches = exactMatches
                .GroupBy(x => x.AppId)
                .Select(g => g.First())
                .ToArray();

            if (exactMatches.Length == 1)
                return exactMatches[0].AppId;

            if (exactMatches.Length > 1)
                Log.WriteLine($"{exactMatches.Length} exact Steam matches found for '{gameTitle}'");

            return null;
        }

        internal static async Task<SteamGameData?> GetGameData(string gameTitle, string? steamAppId = null, bool getImage = false)
        {
            steamAppId ??= await GetAppId(gameTitle);
            if (steamAppId == null)
                return null;

            var storeUrl = GetStoreUrl(steamAppId);

            var storePageHtml = await GetStorePageHtml(storeUrl);
            if (storePageHtml == null)
                return null;

            return new(gameTitle)
            {
                Id = steamAppId,
                MetacriticScore = await GetMetacriticScore(storePageHtml),
                ImagePath = getImage ? await GetImage(gameTitle, storePageHtml) : null,
                Description = WebScraper.ExtractValue(storePageHtml, @"<div[^>]*\bclass=""[^""]*\bgame_description_snippet\b[^""]*""[^>]*>\s*(.+?)\s*</div>"),
            };
        }

        static async Task<string?> GetStorePageHtml(string storeUrl) => await WebScraper.RenderAsync(storeUrl);

        static async Task<int?> GetMetacriticScore(string storePageHtml)
        {
            // look for div with class containing "score" and extract the number inside
            var metaScore = WebScraper.ExtractValue(storePageHtml, @"<div[^>]*\bclass=""[^""]*\bscore\b[^""]*""[^>]*>\s*(\d+)\s*</div>");
            if (metaScore == null)
                return null;

            if (!int.TryParse(metaScore, out var score))
                return null;

            return score;
        }

        static async Task<string?> GetImage(string gameTitle, string storePageHtml)
        {
            if (AppSettings.Instance.ImageCachePath is null)
            {
                Log.WriteLine($"Image cache path is not set. Cannot download image for '{gameTitle}'");
                return null;
            }

            var imageDirectory = Path.Combine(AppSettings.Instance.ImageCachePath, _imagesDirectory);
            var existingImages = Directory.GetFiles(imageDirectory, $"{gameTitle}.*", SearchOption.AllDirectories);

            if (existingImages.Length != 0)
            {
                Log.WriteLine($"Image/s for '{gameTitle}' already exists at '{existingImages[0]}' - skipping download.");
                return existingImages[0];
            }

            var images = WebScraper.ExtractAllAttributeValues(storePageHtml, "src", "img", "game_header_image_full");
            if (images.Length == 0)
                return null;

            // download & save image
            var (image, iamgeFormat) = await Tools.DownloadImage(images.First());
            if (image != null)
                return await Tools.SaveGameImage(gameTitle, image, iamgeFormat, imageDirectory);

            return null;
        }
    }
}
