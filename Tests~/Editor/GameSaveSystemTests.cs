using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using Object = UnityEngine.Object;

public sealed class GameSaveSystemTests
{
    private const string TotalGamesKey = "AlphaStriker.GameStats.TotalGamesPlayed";
    private const string GamesWonKey = "AlphaStriker.GameStats.GamesWon";
    private const string GamesLostKey = "AlphaStriker.GameStats.GamesLost";
    private const string StrategicResourcesKey = "AlphaStriker.GameStats.StrategicResources";

    private bool hadTotal;
    private bool hadWins;
    private bool hadLosses;
    private int previousTotal;
    private int previousWins;
    private int previousLosses;
    private bool hadStrategicResources;
    private string previousStrategicResources;
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
        hadStrategicResources = PlayerPrefs.HasKey(StrategicResourcesKey);
        previousStrategicResources = PlayerPrefs.GetString(StrategicResourcesKey);
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
        if (hadStrategicResources) PlayerPrefs.SetString(StrategicResourcesKey, previousStrategicResources);
        else PlayerPrefs.DeleteKey(StrategicResourcesKey);
        PlayerPrefs.Save();
        ReloadSave();
    }

    [Test]
    public void NewSaveStartsAtZero()
    {
        AssertStatistics(0, 0, 0, 0);
    }

    [Test]
    public void VictoryAndDefeatPersistAndReload()
    {
        RecordCompletedGame(true);
        AssertStatistics(1, 1, 0, 2000);
        RecordCompletedGame(false);
        ReloadSave();

        AssertStatistics(2, 1, 1, 3000);
    }

    [Test]
    public void ExistingStatisticsLoadWithZeroStrategicResources()
    {
        PlayerPrefs.SetInt(TotalGamesKey, 8);
        PlayerPrefs.SetInt(GamesWonKey, 5);
        PlayerPrefs.SetInt(GamesLostKey, 3);
        ReloadSave();

        AssertStatistics(8, 5, 3, 0);
        RecordCompletedGame(false);
        ReloadSave();
        AssertStatistics(9, 5, 4, 1000);
    }

    [Test]
    public void RepeatedResultNotificationOnlyRecordsOnce()
    {
        managerObject = new GameObject("Game save result test");
        GameFlowManager manager = managerObject.AddComponent<GameFlowManager>();
        MethodInfo setResult = typeof(GameFlowManager).GetMethod("SetResult", BindingFlags.Instance | BindingFlags.NonPublic);

        setResult.Invoke(manager, new object[] { GameFlowManager.Result.Victory });
        setResult.Invoke(manager, new object[] { GameFlowManager.Result.Defeat });

        AssertStatistics(1, 1, 0, 2000);
    }

    [Test]
    public void MainMenuRefreshesTheSavedBalance()
    {
        GameObject prefab = Resources.Load<GameObject>("UI/GameStartMenu");
        GameObject menu = Object.Instantiate(prefab);
        try
        {
            var view = menu.GetComponent<GameStartMenuView>();
            var balance = menu.transform.Find("Statistics Panel/Strategic Resources Value")
                .GetComponent<TextMeshProUGUI>();

            view.SetState(false, false, 1f, false);
            Assert.That(balance.text, Is.EqualTo("0"));
            view.SetState(true, false, 1f, false);
            Assert.That(balance.gameObject.activeInHierarchy, Is.False);

            RecordCompletedGame(true);
            ReloadSave();
            view.SetState(false, false, 1f, false);
            Assert.That(balance.gameObject.activeInHierarchy, Is.True);
            Assert.That(balance.text, Is.EqualTo(2000.ToString("N0")));
        }
        finally
        {
            Object.DestroyImmediate(menu);
        }
    }

    [Test]
    public void InvalidStatisticsAreResetAndPersisted()
    {
        PlayerPrefs.SetInt(TotalGamesKey, 3);
        PlayerPrefs.SetInt(GamesWonKey, 2);
        PlayerPrefs.SetInt(GamesLostKey, 0);
        PlayerPrefs.SetString(StrategicResourcesKey, "4500");
        ReloadSave();

        AssertStatistics(0, 0, 0, 4500);
        Assert.That(PlayerPrefs.GetInt(TotalGamesKey), Is.Zero);
        Assert.That(PlayerPrefs.GetInt(GamesWonKey), Is.Zero);
        Assert.That(PlayerPrefs.GetInt(GamesLostKey), Is.Zero);
    }

    private static void AssertStatistics(int total, int wins, int losses, long strategicResources)
    {
        GameSaveSystem.Statistics stats = GameSaveSystem.Current;
        Assert.That(stats.TotalGamesPlayed, Is.EqualTo(total));
        Assert.That(stats.GamesWon, Is.EqualTo(wins));
        Assert.That(stats.GamesLost, Is.EqualTo(losses));
        Assert.That(stats.StrategicResources, Is.EqualTo(strategicResources));
    }

    private static void ClearSave()
    {
        PlayerPrefs.DeleteKey(TotalGamesKey);
        PlayerPrefs.DeleteKey(GamesWonKey);
        PlayerPrefs.DeleteKey(GamesLostKey);
        PlayerPrefs.DeleteKey(StrategicResourcesKey);
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
