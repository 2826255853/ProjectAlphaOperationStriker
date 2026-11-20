using UnityEngine;

/// <summary>Stores cumulative completed-game statistics on this device.</summary>
public static class GameSaveSystem
{
    private const string KeyPrefix = "AlphaStriker.GameStats.";
    private const string TotalGamesKey = KeyPrefix + "TotalGamesPlayed";
    private const string GamesWonKey = KeyPrefix + "GamesWon";
    private const string GamesLostKey = KeyPrefix + "GamesLost";

    public readonly struct Statistics
    {
        public int TotalGamesPlayed { get; }
        public int GamesWon { get; }
        public int GamesLost { get; }

        internal Statistics(int totalGamesPlayed, int gamesWon, int gamesLost)
        {
            TotalGamesPlayed = totalGamesPlayed;
            GamesWon = gamesWon;
            GamesLost = gamesLost;
        }
    }

    private static Statistics statistics;
    private static bool isLoaded;

    public static Statistics Current
    {
        get
        {
            EnsureLoaded();
            return statistics;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void LoadAtStartup()
    {
        isLoaded = false;
        EnsureLoaded();
    }

    internal static void RecordCompletedGame(bool won)
    {
        EnsureLoaded();
        int total = statistics.TotalGamesPlayed;
        int wins = statistics.GamesWon;
        int losses = statistics.GamesLost;

        if (total == int.MaxValue || (won && wins == int.MaxValue) || (!won && losses == int.MaxValue))
            return;

        total++;
        if (won) wins++;
        else losses++;

        statistics = new Statistics(total, wins, losses);
        PlayerPrefs.SetInt(TotalGamesKey, total);
        PlayerPrefs.SetInt(GamesWonKey, wins);
        PlayerPrefs.SetInt(GamesLostKey, losses);
        PlayerPrefs.Save();
    }

    private static void EnsureLoaded()
    {
        if (isLoaded) return;
        isLoaded = true;

        int total = PlayerPrefs.GetInt(TotalGamesKey, 0);
        int wins = PlayerPrefs.GetInt(GamesWonKey, 0);
        int losses = PlayerPrefs.GetInt(GamesLostKey, 0);
        if (total >= 0 && wins >= 0 && losses >= 0 && (long)wins + losses == total)
        {
            statistics = new Statistics(total, wins, losses);
            return;
        }

        statistics = new Statistics(0, 0, 0);
        PlayerPrefs.SetInt(TotalGamesKey, 0);
        PlayerPrefs.SetInt(GamesWonKey, 0);
        PlayerPrefs.SetInt(GamesLostKey, 0);
        PlayerPrefs.Save();
    }
}
