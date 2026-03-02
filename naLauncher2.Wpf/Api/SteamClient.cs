namespace naLauncher2.Wpf.Api
{
    public class SteamClient
    {
        const string _storeUrl = "https://store.steampowered.com";

        const string _imagesDirectory = "SteamDbInfo";

        public static string GetStoreUrl(string appId) => $"{_storeUrl}/app/{appId}";

    }
}
