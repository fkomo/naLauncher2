namespace naLauncher2.Wpf.Api
{
    internal record class TokenResponse(string? access_token, string? refresh_token, long? expires_in);
}