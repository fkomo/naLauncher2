namespace naLauncher2.Wpf
{
    internal enum UserGamesFilterMode 
    { 
        Installed, 
        Removed, 
        Finished, 
        Unfinished, 
        All
    }

    internal enum GamesSortMode
    {
        Title, // GameLibrary.Games[Key]
        Added, // GameInfo.Added
        Finished, // GameInfo.Completed
        Played, // GameInfo.Played.Count
        Rating, // GameInfo.Rating 
    }
}