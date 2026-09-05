namespace naLauncher2.Wpf
{
    internal enum UserGamesFilterMode 
    { 
        Installed, 
        Removed, 
        Completed,
        MissingData,
        Steam,
        Igdb,
        All
    }

    /// <summary>
    /// How the User Games grid is split up when games are ordered by title.
    /// </summary>
    internal enum TitleGroupMode
    {
        None, // one continuous grid
        Divider, // a labelled line above the first row of each letter
        Tile, // a clickable letter tile at the start of each letter
    }

    internal enum GamesSortMode
    {
        Title, // GameLibrary.Games[Key]
        Added, // GameInfo.Added
        Completed, // GameInfo.Completed
        Played, // GameInfo.Played.Count
        Rating, // GameInfo.Rating
        Released // GameInfo.ReleaseDate
    }
}