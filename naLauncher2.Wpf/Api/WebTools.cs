using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net.Http;
using Ujeby.Tools;

namespace naLauncher2.Wpf.Api
{
    internal class Tools
    {
        readonly static HttpClient _imageHttpClient = new();

        public static async Task<(Image, ImageFormat)> DownloadImage(string imageUrl)
        {
            using var tb = new TimedBlock($"{nameof(IgdbClient)}.{nameof(DownloadImage)}({imageUrl})", Log.WriteLine);

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

        public async static Task<string?> SaveGameImage(string gameTitle, Image image, ImageFormat imageFormat, string directory)
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
    }
}
