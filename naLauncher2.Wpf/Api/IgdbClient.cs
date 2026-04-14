using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using Ujeby.Extensions;
using Ujeby.Tools;

namespace naLauncher2.Wpf.Api
{
    public record class IgdbGameData(string Title)
    {
        public string? Id { get; init; }
        public string? Summary { get; init; }
        public int? Rating { get; init; }
        public string? Developer { get; init; }
        public string[]? Genres { get; init; }
        public string? ImagePath { get; init; }
        public string? Url { get; init; }
    }

#pragma warning disable IDE1006 // Naming Styles
    public record class NameId(
        int id,
        string name);

    public record class InvolvedCompany(
        int id,
        int company,
        bool developer);

    public record class GameImage(
        int id,
        int width,
        int height,
        string url);

    public record class GameDetail(
        int id,
        string name,
        int[]? artworks,
        int? cover,
        int[]? genres,
        double? total_rating,
        string? summary,
        int[]? involved_companies,
        string url) : NameId(id, name);
#pragma warning restore IDE1006 // Naming Styles

    /// <summary>
    /// https://www.igdb.com/api
    /// </summary>
    public class IgdbClient
    {
        readonly string _apiBaseUrl;

        readonly HttpClient _httpClient;

        const string _imagesDirectory = "IgdbCom";

        public IgdbClient(TwitchDevAuthz? twitchDevAuthz, string apiBaseUrl = "https://api.igdb.com/v4")
        {
            if (twitchDevAuthz == null)
                throw new ArgumentException($"TwitchDev authz is required for {nameof(IgdbClient)}.");

            _apiBaseUrl = apiBaseUrl;
            _httpClient = new HttpClient(twitchDevAuthz);
        }

        public static string GetGameSearchUrl(string gameTitle) => $"https://www.igdb.com/search?q={Uri.EscapeDataString(gameTitle)}&type=games";

        public async Task<IgdbGameData?> GetGameData(string gameTitle, string? gameId = null, bool getImage = true)
        {
            using var tb = new TimedBlock($"{nameof(IgdbClient)}.{nameof(GetGameData)}('{gameTitle}')", Log.WriteLine);

            await CacheEnums();

            try
            {
                gameId ??= await GetIdFromTitle(gameTitle);

                // get game by id
                var gameDetail = (await PostAsync<GameDetail[]>($"{_apiBaseUrl}/games",
                        $"fields name, artworks, cover, genres, total_rating, summary, involved_companies, url; where id = {gameId};"))?
                        .SingleOrDefault();

                if (gameDetail == null)
                    return null;

                var gameData = new IgdbGameData(gameTitle)
                {
                    Id = gameId,
                    Summary = gameDetail.summary,
                    Rating = gameDetail.total_rating.HasValue ? (int)gameDetail.total_rating : null,
                    Developer = await GetDeveloper(gameDetail.involved_companies),
                    Genres = gameDetail.genres?.Select(x => _genresCache[x]).ToArray(),
                    Url = gameDetail.url,
                    ImagePath = getImage ? await GetImage(gameTitle, gameDetail.cover) : null
                };

                return gameData;
            }
            catch (Exception ex)
            {
                Log.WriteLine($"{nameof(GetGameData)}('{gameTitle}') failed with: {ex}");
                return null;
            }
        }

        async Task<string?> GetIdFromTitle(string gameTitle)
        {
            // search by title
            var games = await PostAsync<NameId[]>($"{_apiBaseUrl}/games",
                $"fields name; search \"{gameTitle}\"; where version_parent = null; limit 100;");

            if (games == null || games.Length == 0)
                return null;

            var normalizedGameTitle = gameTitle.NormalizeCustom();

            var exactMatches = games
                .Where(x => string.Equals(x.name.NormalizeCustom(), normalizedGameTitle, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (exactMatches.Length == 1)
                return exactMatches[0].id.ToString();

            if (exactMatches.Length > 1)
                Log.WriteLine($"{exactMatches.Length} exact IGDB matches found for '{gameTitle}')");

            return null;
        }

        async Task<TResponse?> PostAsync<TResponse>(string url, string body)
        {
            var response = await _httpClient.PostAsync(url, new StringContent(body));
            if (!response.IsSuccessStatusCode)
                return default;

            return await response.Content.ReadFromJsonAsync<TResponse>();
        }

        Dictionary<int, string> _genresCache = [];

        async Task CacheEnums()
        {
            if (_genresCache.Count == 0)
                _genresCache = (await PostAsync<NameId[]>($"{_apiBaseUrl}/genres", $"fields name; limit 100;"))!
                    .ToDictionary(x => x.id, x => x.name);
        }

        readonly ConcurrentDictionary<int, string> _developersCache = [];

        async Task<string?> GetDeveloper(int[]? involvedCompanies)
        {
            if (involvedCompanies == null || involvedCompanies.Length == 0)
                return null;

            foreach (var involvedCompanyId in involvedCompanies)
            {
                if (_developersCache.TryGetValue(involvedCompanyId, out string? cached))
                    return cached;

                var involvedCompany = (await PostAsync<InvolvedCompany[]>($"{_apiBaseUrl}/involved_companies", $"fields company, developer; where id = {involvedCompanyId};"))?.SingleOrDefault();
                if (involvedCompany?.developer != true)
                    continue;

                var developer = (await PostAsync<NameId[]>($"{_apiBaseUrl}/companies", $"fields name; where id = {involvedCompany.company};"))?.SingleOrDefault();
                if (!involvedCompany.developer)
                    continue;

                if (developer?.name != null)
                    _developersCache.AddOrUpdate(involvedCompanyId, developer.name, (id, oldValue) => developer.name);

                return developer?.name;
            }

            return null;
        }

        async Task<string?> GetImage(string gameTitle, int? coverId)
        {
            if (!coverId.HasValue)
                return null;

            var gameImage = (await PostAsync<GameImage[]>($"{_apiBaseUrl}/covers", $"fields id, url, width, height; where id = {coverId};"))?
                .FirstOrDefault();

            if (gameImage == null)
                return null;

            var imageUrl = "https:" + gameImage.url.Replace("t_thumb", "t_original");

            if (AppSettings.Instance.ImageCachePath is null)
            {
                Log.WriteLine($"Image cache path is not set. Cannot download image for '{gameTitle}' from {imageUrl}");
                return null;
            }

            var imageDirectory = Path.Combine(AppSettings.Instance.ImageCachePath, _imagesDirectory);
            var existingImages = Directory.GetFiles(imageDirectory, $"{gameTitle}.*", SearchOption.AllDirectories);

            if (existingImages.Length != 0)
            {
                Log.WriteLine($"Image/s for '{gameTitle}' already exists at '{existingImages[0]}' - skipping download.");
                return existingImages[0];
            }

            // download & save image
            var (image, iamgeFormat) = await Tools.DownloadImage(imageUrl);
            if (image != null)
                return await Tools.SaveGameImage(gameTitle, image, iamgeFormat, imageDirectory);

            return null;
        }
    }
}