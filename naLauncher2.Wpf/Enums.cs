namespace naLauncher2.Wpf
{
    internal enum UserGamesFilterMode 
    { 
        Installed, 
        Removed, 
        Completed, 
        All
    }

    internal enum GamesSortMode
    {
        Title, // GameLibrary.Games[Key]
        Added, // GameInfo.Added
        Completed, // GameInfo.Completed
        Played, // GameInfo.Played.Count
        Rating, // GameInfo.Rating 
    }
}