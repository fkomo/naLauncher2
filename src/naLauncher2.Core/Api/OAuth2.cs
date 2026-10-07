namespace naLauncher2.Core.Api
{
    internal record class TokenResponse(string? access_token, string? refresh_token, long? expires_in);
}