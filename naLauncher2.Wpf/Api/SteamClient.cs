using System.Globalization;
using System.IO;
using System.Net;
using Ujeby.Extensions;

namespace naLauncher2.Wpf.Api
{
    public record class SteamGameData() : IGameData
    {
        public string? Title { get; init; }
        public string? Id { get; init; }
        public string? ImagePath { get; init; }

        public int? MetacriticScore { get; init; }
        public string? Description { get; init; }
        public DateTime? ReleaseDate { get; init; }
    }

    public class SteamClient : IGameDataProvider<SteamGameData>
    {
        const string _storeUrl = "https://store.steampowered.com";

        const string _searchUrl = "https://store.steampowered.com/search?sort_by=_ASC&term=";

        const string _imagesDirectory = "SteamDbInfo";

        public static string GetStoreUrl(string appId) => $"{_storeUrl}/app/{appId}";

        public SteamClient()
        {
        }

        public async Task<string?> GetAppId(string gameTitle)
        {
            var html = await WebScraper.RenderAsync(_searchUrl + WebUtility.UrlEncode(gameTitle.ToLower()));
            if (html == null)
                return null;

            var allAppUrls = WebScraper.ExtractAllAttributeValues(html, "href", "https://store.steampowered.com/app/");

            // https://store.steampowered.com/app/4109130/...

            var normalizedGameTitle = gameTitle.NormalizeCustom().Replace("demo", string.Empty);

            var exactMatches = allAppUrls
                .Where(url => url.Contains("/app/"))
                .Select(url => 
                    new
                    {
                        AppId = url.Split('/')[4],
                        Title = url.Split('/')[5].Replace('_', ' ')
                    })
                .Where(x => x.Title.NormalizeCustom().Replace("demo", string.Empty).Equals(normalizedGameTitle, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (exactMatches.Length > 1)
                exactMatches = exactMatches.Where(x => !x.Title.ToLower().Contains("demo")).ToArray();

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

        public async Task<SteamGameData?> GetGameData(string gameTitle, string? id = null, bool getImage = false)
        {
            id ??= await GetAppId(gameTitle);
            if (id == null)
                return null;

            var storeUrl = GetStoreUrl(id);

            var storePageHtml = await GetStorePageHtml(storeUrl);
            if (storePageHtml == null)
                return null;

            return new SteamGameData
            {
                Title = gameTitle,
                Id = id,
                MetacriticScore = await GetMetacriticScore(storePageHtml),
                ImagePath = getImage ? await GetImage(gameTitle, storePageHtml) : null,
                Description = WebScraper.ExtractValue(storePageHtml, @"<div[^>]*\bclass=""[^""]*\bgame_description_snippet\b[^""]*""[^>]*>\s*(.+?)\s*</div>"),
                ReleaseDate = GetReleaseDate(storePageHtml),
            };
        }

        static DateTime? GetReleaseDate(string storePageHtml)
        {
            var releaseDateParent = WebScraper.ExtractValues(storePageHtml, "div", "release_date");
            if (releaseDateParent.Length == 0)
                return null;

            var releaseDateString = WebScraper.ExtractValues(releaseDateParent.First(), "div", "date");
            if (releaseDateString.Length == 0)
                return null;

            return DateTime.TryParseExact(releaseDateString.First().Trim(), "d MMM, yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate)
                ? parsedDate
                : null;
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

            // sample: <img class="game_header_image_full" alt="" src="https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/1939970/header.jpg?t=1771496637">
            var images = WebScraper.ExtractAllAttributeValuesFiltered(storePageHtml, "img", "src", 
                ("class", "game_header_image_full"));

            if (images.Length == 0)
            {
                // sample: <meta property="og:image" content="https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/1939970/capsule_616x353.jpg?t=1771496637">
                images = WebScraper.ExtractAllAttributeValuesFiltered(storePageHtml, "meta", "content", 
                    ("property", "og:image"));
            }

            if (images.Length == 0)
            {
                Log.WriteLine($"SteamDb image for '{gameTitle}' not found.");
                return null;
            }

            // download & save image
            var (image, iamgeFormat) = await Tools.DownloadImage(images.First());
            if (image != null)
                return await Tools.SaveGameImage(gameTitle, image, iamgeFormat, imageDirectory);

            return null;
        }
    }
}
