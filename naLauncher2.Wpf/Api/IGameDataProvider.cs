namespace naLauncher2.Wpf.Api
{
    public interface IGameData
    {
        string? Title { get; }
        string? Id { get; }
        string? ImagePath { get; }
    }

    public interface IGameDataProvider<TGameData> where TGameData : IGameData, new()
    {
        Task<TGameData?> GetGameData(string gameTitle, string? id = null, bool getImage = true);
    }
}