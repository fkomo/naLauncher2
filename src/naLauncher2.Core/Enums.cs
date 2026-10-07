namespace naLauncher2.Core
{
    public enum UserGamesFilterMode 
    { 
        Installed, 
        Removed, 
        Completed,
        MissingData,
        Steam,
        Igdb,
        All,
        Starred
    }

    public enum GamesSortMode
    {
        Title, // GameLibrary.Games[Key]
        Added, // GameInfo.Added
        Completed, // GameInfo.Completed
        Played, // GameInfo.Played.Count
        Rating, // GameInfo.Rating
        Released, // GameInfo.ReleaseDate
        PlayTime // GameInfo.TotalPlayTime
    }
}