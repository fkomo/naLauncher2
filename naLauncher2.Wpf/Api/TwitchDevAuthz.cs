using naLauncher2.Wpf.Common;
using System.Net.Http;
using System.Net.Http.Json;

namespace naLauncher2.Wpf.Api
{
    /// <summary>
    /// https://dev.twitch.tv/docs/api/
    /// </summary>
    public class TwitchDevAuthz : DelegatingHandler
    {
        readonly string _clientId;
        readonly string _clientSecret;

        readonly HttpClient _httpClient;

        const string TokenEndpoint = "https://id.twitch.tv/oauth2/token";

        string? _accessToken;
        DateTime? _expiresIn;

        public TwitchDevAuthz(string clientId, string clientSecret)
            : base(new HttpClientHandler())
        {
            _clientId = clientId;
            _clientSecret = clientSecret;

            _httpClient = new HttpClient();
        }

        async Task GetAccessToken()
        {
            using var tb = new TimedBlock($"{nameof(TwitchDevAuthz)}.{nameof(GetAccessToken)}");

            var tokenResponse = await _httpClient.PostAsync($"{TokenEndpoint}?client_id={_clientId}&client_secret={_clientSecret}&grant_type=client_credentials", null);
            if (tokenResponse.IsSuccessStatusCode)
            {
                var json = await tokenResponse.Content.ReadAsStringAsync();

                var token = await tokenResponse.Content.ReadFromJsonAsync<TokenResponse>();

                _accessToken = token?.access_token
                    ?? throw new Exception("Failed to obtain access token from Twitch API.");

                _expiresIn = null;
                if (token.expires_in.HasValue)
                {
                    _expiresIn = DateTime.UtcNow.AddSeconds(token.expires_in.Value);
                    Log.WriteLine($"Obtained new access token {_accessToken} for Twitch API. Expires at: {_expiresIn.Value.ToLocalTime()}");
                }
                else
                    Log.WriteLine($"Obtained new access token {_accessToken} for Twitch API. No expiration time provided.");
            }
            else
                throw new Exception($"{TokenEndpoint} responded with {tokenResponse.StatusCode}");
        }

        public async Task<Dictionary<string, string>> GetAuthzHeaders()
        {
            if (_accessToken == null || (_expiresIn.HasValue && DateTime.UtcNow >= _expiresIn.Value))
                await GetAccessToken();

            return new Dictionary<string, string>()
            {
                { "Client-ID", _clientId },
                { "Authorization", $"Bearer {_accessToken}" }
            };
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var authzHeaders = await GetAuthzHeaders();

            foreach (var header in authzHeaders)
            {
                if (!request.Headers.Contains(header.Key))
                    request.Headers.Add(header.Key, header.Value);
            }

            return await base.SendAsync(request, cancellationToken);
        }
    }
}