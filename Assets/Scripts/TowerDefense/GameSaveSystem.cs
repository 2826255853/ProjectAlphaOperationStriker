using System.Globalization;
using UnityEngine;

/// <summary>Stores completed-game statistics and strategic resources on this device.</summary>
public static class GameSaveSystem
{
    private const string KeyPrefix = "AlphaStriker.GameStats.";
    private const string TotalGamesKey = KeyPrefix + "TotalGamesPlayed";
    private const string GamesWonKey = KeyPrefix + "GamesWon";
    private const string GamesLostKey = KeyPrefix + "GamesLost";
    private const string StrategicResourcesKey = KeyPrefix + "StrategicResources";
    private const int VictoryReward = 2000;
    private const int DefeatReward = 1000;

    public readonly struct Statistics
    {
        public int TotalGamesPlayed { get; }
        public int GamesWon { get; }
        public int GamesLost { get; }
        public long StrategicResources { get; }

        internal Statistics(int totalGamesPlayed, int gamesWon, int gamesLost, long strategicResources)
        {
            TotalGamesPlayed = totalGamesPlayed;
            GamesWon = gamesWon;
            GamesLost = gamesLost;
            StrategicResources = strategicResources;
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

        int reward = won ? VictoryReward : DefeatReward;
        long strategicResources = System.Math.Min(statistics.StrategicResources, long.MaxValue - reward) + reward;
        statistics = new Statistics(total, wins, losses, strategicResources);
        PlayerPrefs.SetInt(TotalGamesKey, total);
        PlayerPrefs.SetInt(GamesWonKey, wins);
        PlayerPrefs.SetInt(GamesLostKey, losses);
        PlayerPrefs.SetString(StrategicResourcesKey, strategicResources.ToString(CultureInfo.InvariantCulture));
        PlayerPrefs.Save();
    }

    private static void EnsureLoaded()
    {
        if (isLoaded) return;
        isLoaded = true;

        int total = PlayerPrefs.GetInt(TotalGamesKey, 0);
        int wins = PlayerPrefs.GetInt(GamesWonKey, 0);
        int losses = PlayerPrefs.GetInt(GamesLostKey, 0);
        if (!long.TryParse(PlayerPrefs.GetString(StrategicResourcesKey, "0"), NumberStyles.None,
            CultureInfo.InvariantCulture, out long strategicResources))
            strategicResources = 0;

        if (total >= 0 && wins >= 0 && losses >= 0 && (long)wins + losses == total)
        {
            statistics = new Statistics(total, wins, losses, strategicResources);
            return;
        }

        statistics = new Statistics(0, 0, 0, strategicResources);
        PlayerPrefs.SetInt(TotalGamesKey, 0);
        PlayerPrefs.SetInt(GamesWonKey, 0);
        PlayerPrefs.SetInt(GamesLostKey, 0);
        PlayerPrefs.Save();
    }
}
