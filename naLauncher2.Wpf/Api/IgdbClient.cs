using naLauncher2.Wpf.Common;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;

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
        int[]? involved_companies) : NameId(id, name);
#pragma warning restore IDE1006 // Naming Styles

    /// <summary>
    /// https://www.igdb.com/api
    /// </summary>
    internal class IgdbClient
    {
        readonly string _apiBaseUrl;
        readonly HttpClient _httpClient;
        static readonly HttpClient _imageHttpClient = new();

        const string _igdbImagesDirectory = "IgdbCom";

        public IgdbClient(AppSettings.TwitchDevSettings twitchDev, string apiBaseUrl = "https://api.igdb.com/v4")
        {
            if (twitchDev == null || string.IsNullOrEmpty(twitchDev.ClientId) || string.IsNullOrEmpty(twitchDev.ClientSecret))
                throw new ArgumentException($"TwitchDev settings are required for {nameof(IgdbClient)}.");

            _apiBaseUrl = apiBaseUrl;
            _httpClient = new HttpClient(new TwitchDevAuthz(twitchDev.ClientId, twitchDev.ClientSecret));
        }

        public async Task<IgdbGameData?> GetGameData(string gameTitle)
        {
            using var tb = new TimedBlock($"{nameof(IgdbClient)}.{nameof(GetGameData)}('{gameTitle}')");

            await CacheEnums();

            try
            {
                var normalizedGameTitle = gameTitle.Normalize();

                // search by title
                var games = await PostAsync<NameId[]>($"{_apiBaseUrl}/games",
                    $"fields name; search \"{gameTitle.ToLower()}\"; where version_parent = null;");

                if (games == null || games.Length == 0)
                    return null;

                var possibleMatches = games.Select(g =>
                    new KeyValuePair<string, KeyValuePair<int, string>>(
                        g.id.ToString(),
                        new KeyValuePair<int, string>(
                            Extensions.DamerauLevenshteinEditDistance(normalizedGameTitle, g.name.Normalize()),
                            g.name)))
                    .Where(x => x.Value.Key < normalizedGameTitle.Length)
                    .OrderBy(x => x.Value.Key)
                    .ToArray();

                var bestMatch = possibleMatches.FirstOrDefault();

                // get best matching game by id
                var gameDetail = (await PostAsync<GameDetail[]>($"{_apiBaseUrl}/games",
                        $"fields name, artworks, cover, genres, total_rating, summary, involved_companies; where id = {bestMatch.Key};"))?
                        .SingleOrDefault();

                if (gameDetail != null)
                {
                    var image = await GetImage(gameTitle, gameDetail.cover);

                    var gameData = new IgdbGameData(gameTitle)
                    {
                        Id = bestMatch.Key.ToString(),
                        Summary = gameDetail.summary,
                        Rating = !gameDetail.total_rating.HasValue ? null as int? : (int)gameDetail.total_rating,
                        Developer = await GetDeveloper(gameDetail.involved_companies),
                        Genres = gameDetail.genres?.Select(x => _genresCache[x]).ToArray(),
                        //ImagePath
                    };

                    return gameData;
                }
            }
            catch (Exception ex)
            {
                Log.WriteLine($"{nameof(GetGameData)}('{gameTitle}') failed with: {ex}");
            }

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

        ConcurrentDictionary<int, string> _developersCache = [];

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

            var imageDirectory = Path.Combine(AppSettings.Instance.ImageCachePath, _igdbImagesDirectory);
            var existingImages = Directory.GetFiles(imageDirectory, $"{gameTitle}.*", SearchOption.AllDirectories);

            if (existingImages.Length != 0)
            {
                Log.WriteLine($"Image/s for '{gameTitle}' already exists at '{existingImages[0]}' - skipping download.");
                return null;
            }

            // download & save image
            var (image, iamgeFormat) = await DownloadImage(imageUrl);
            if (image != null)
                return await SaveGameImage(gameTitle, image, iamgeFormat, imageDirectory);

            return null;
        }

        async static Task<string?> SaveGameImage(string gameTitle, Image image, ImageFormat imageFormat, string directory)
        {
            Directory.CreateDirectory(directory);

            var invalidChars = Path.GetInvalidFileNameChars();
            var safeTitle = string.Concat(gameTitle.Select(c => invalidChars.Contains(c) ? '_' : c));

            var extension = imageFormat.Equals(ImageFormat.Png) ? "png"
                : imageFormat.Equals(ImageFormat.Gif) ? "gif"
                : imageFormat.Equals(ImageFormat.Bmp) ? "bmp"
                : "jpg";

            var filePath = Path.Combine(directory, $"{safeTitle}.{extension}");
            if (File.Exists(filePath))
            {
                Log.WriteLine($"Image for '{gameTitle}' already exists at {filePath}. Skipping download.");
                return filePath;
            }

            await Task.Run(() => image.Save(filePath, imageFormat));

            return filePath;
        }

        async static Task<(Image, ImageFormat)> DownloadImage(string imageUrl)
        {
            using var tb = new TimedBlock($"{nameof(IgdbClient)}.{nameof(DownloadImage)}({imageUrl})");

            using var response = await _imageHttpClient.GetAsync(imageUrl);
            if (!response.IsSuccessStatusCode)
                return default!;

            var imageFormat = response.Content.Headers.ContentType?.MediaType switch
            {
                "image/png" => ImageFormat.Png,
                "image/gif" => ImageFormat.Gif,
                "image/bmp" => ImageFormat.Bmp,
                _ => ImageFormat.Jpeg
            };

            using var ms = new MemoryStream();
            await response.Content.CopyToAsync(ms);
            ms.Position = 0;

            // copy into a new Bitmap so it no longer depends on the stream
            return (new Bitmap(Image.FromStream(ms)), imageFormat);
        }
    }
}