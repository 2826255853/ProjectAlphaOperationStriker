using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

public sealed class GameSaveSystemTests
{
    private const string TotalGamesKey = "AlphaStriker.GameStats.TotalGamesPlayed";
    private const string GamesWonKey = "AlphaStriker.GameStats.GamesWon";
    private const string GamesLostKey = "AlphaStriker.GameStats.GamesLost";

    private bool hadTotal;
    private bool hadWins;
    private bool hadLosses;
    private int previousTotal;
    private int previousWins;
    private int previousLosses;
    private GameObject managerObject;

    [SetUp]
    public void SetUp()
    {
        hadTotal = PlayerPrefs.HasKey(TotalGamesKey);
        hadWins = PlayerPrefs.HasKey(GamesWonKey);
        hadLosses = PlayerPrefs.HasKey(GamesLostKey);
        previousTotal = PlayerPrefs.GetInt(TotalGamesKey);
        previousWins = PlayerPrefs.GetInt(GamesWonKey);
        previousLosses = PlayerPrefs.GetInt(GamesLostKey);
        ClearSave();
        ReloadSave();
    }

    [TearDown]
    public void TearDown()
    {
        if (managerObject != null) Object.DestroyImmediate(managerObject);
        RestoreKey(TotalGamesKey, hadTotal, previousTotal);
        RestoreKey(GamesWonKey, hadWins, previousWins);
        RestoreKey(GamesLostKey, hadLosses, previousLosses);
        PlayerPrefs.Save();
        ReloadSave();
    }

    [Test]
    public void NewSaveStartsAtZero()
    {
        AssertStatistics(0, 0, 0);
    }

    [Test]
    public void VictoryAndDefeatPersistAndReload()
    {
        RecordCompletedGame(true);
        RecordCompletedGame(false);
        ReloadSave();

        AssertStatistics(2, 1, 1);
    }

    [Test]
    public void RepeatedResultNotificationOnlyRecordsOnce()
    {
        managerObject = new GameObject("Game save result test");
        GameFlowManager manager = managerObject.AddComponent<GameFlowManager>();
        MethodInfo setResult = typeof(GameFlowManager).GetMethod("SetResult", BindingFlags.Instance | BindingFlags.NonPublic);

        setResult.Invoke(manager, new object[] { GameFlowManager.Result.Victory });
        setResult.Invoke(manager, new object[] { GameFlowManager.Result.Victory });

        AssertStatistics(1, 1, 0);
    }

    [Test]
    public void InvalidStatisticsAreResetAndPersisted()
    {
        PlayerPrefs.SetInt(TotalGamesKey, 3);
        PlayerPrefs.SetInt(GamesWonKey, 2);
        PlayerPrefs.SetInt(GamesLostKey, 0);
        ReloadSave();

        AssertStatistics(0, 0, 0);
        Assert.That(PlayerPrefs.GetInt(TotalGamesKey), Is.Zero);
        Assert.That(PlayerPrefs.GetInt(GamesWonKey), Is.Zero);
        Assert.That(PlayerPrefs.GetInt(GamesLostKey), Is.Zero);
    }

    private static void AssertStatistics(int total, int wins, int losses)
    {
        GameSaveSystem.Statistics stats = GameSaveSystem.Current;
        Assert.That(stats.TotalGamesPlayed, Is.EqualTo(total));
        Assert.That(stats.GamesWon, Is.EqualTo(wins));
        Assert.That(stats.GamesLost, Is.EqualTo(losses));
    }

    private static void ClearSave()
    {
        PlayerPrefs.DeleteKey(TotalGamesKey);
        PlayerPrefs.DeleteKey(GamesWonKey);
        PlayerPrefs.DeleteKey(GamesLostKey);
        PlayerPrefs.Save();
    }

    private static void RestoreKey(string key, bool existed, int value)
    {
        if (existed) PlayerPrefs.SetInt(key, value);
        else PlayerPrefs.DeleteKey(key);
    }

    private static void ReloadSave()
    {
        typeof(GameSaveSystem).GetMethod("LoadAtStartup", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
    }

    private static void RecordCompletedGame(bool won)
    {
        typeof(GameSaveSystem).GetMethod("RecordCompletedGame", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, new object[] { won });
    }
}
