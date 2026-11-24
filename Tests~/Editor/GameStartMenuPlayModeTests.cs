#if UNITY_INCLUDE_TESTS
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

// Discovered as EditMode tests; EnterPlayMode runs the actual project scene.
public sealed class GameStartMenuPlayModeTests
{
    private float originalVolume;

    [UnitySetUp]
    public IEnumerator EnterMainScene()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/DOUBLE_LAYER_FACILITY.unity", OpenSceneMode.Single);
        yield return new EnterPlayMode();
        originalVolume = AudioListener.volume;
        for (int i = 0; i < 5; i++) yield return null;
        Assert.That(Application.isPlaying, Is.True);
    }

    [UnityTearDown]
    public IEnumerator LeaveMainScene()
    {
        if (!Application.isPlaying) yield break;
        AudioListener.volume = originalVolume;
        yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator StartSettingsPauseAndResumeUseLiveScene()
    {
        GameStartMenu menu = Object.FindAnyObjectByType<GameStartMenu>();
        Assert.That(menu, Is.Not.Null);
        GameStartMenuView view = Object.FindAnyObjectByType<GameStartMenuView>();
        Assert.That(view, Is.Not.Null);
        Assert.That(GameStartMenu.CursorOwned, Is.True);
        Assert.That(Time.timeScale, Is.Zero);
        Assert.That(Cursor.visible, Is.True);
        Assert.That(Cursor.lockState, Is.EqualTo(CursorLockMode.None));
        Assert.That(EventSystem.current, Is.Not.Null);
        Assert.That(EventSystem.current.GetComponent<InputSystemUIInputModule>(), Is.Not.Null);
        Assert.That(Get<TextMeshProUGUI>(view, "primaryLabel").text, Is.EqualTo("开始游戏"));
        Assert.That(Get<GameObject>(view, "statisticsPanel").activeSelf, Is.True);
        GameSaveSystem.Statistics statistics = GameSaveSystem.Current;
        double winRate = statistics.TotalGamesPlayed == 0 ? 0d
            : (double)statistics.GamesWon / statistics.TotalGamesPlayed * 100d;
        TextMeshProUGUI[] totals = Get<TextMeshProUGUI[]>(view, "statisticValues");
        Assert.That(totals.Length, Is.EqualTo(4));
        Assert.That(totals[0].text, Is.EqualTo(statistics.TotalGamesPlayed.ToString("N0")));
        Assert.That(totals[1].text, Is.EqualTo(statistics.GamesWon.ToString("N0")));
        Assert.That(totals[2].text, Is.EqualTo(statistics.GamesLost.ToString("N0")));
        Assert.That(totals[3].text, Is.EqualTo($"{winRate:F1}%"));
        Assert.That(Get<TextMeshProUGUI>(view, "strategicResourcesValue").text,
            Is.EqualTo(statistics.StrategicResources.ToString("N0")));
        PlayerInput player = Object.FindAnyObjectByType<PlayerInput>();
        Assert.That(player, Is.Not.Null, "The real scene must bootstrap its player.");
        Assert.That(player.actions, Is.Not.Null);
        foreach (InputAction action in player.actions)
            Assert.That(action.enabled, Is.False, "Menu must disable player action " + action.name);

        Get<Button>(view, "settingsButton").onClick.Invoke();
        Assert.That(Get<GameObject>(view, "settingsPanel").activeSelf, Is.True);
        Assert.That(Get<GameObject>(view, "mainPanel").activeSelf, Is.False);
        Assert.That(Get<GameObject>(view, "statisticsPanel").activeSelf, Is.False);
        Get<Slider>(view, "volumeSlider").value = .37f;
        Assert.That(AudioListener.volume, Is.EqualTo(.37f).Within(.001f));
        Assert.That(Get<TextMeshProUGUI>(view, "volumeValue").text, Is.EqualTo("37%"));
        Get<Button>(view, "backButton").onClick.Invoke();
        Assert.That(Get<GameObject>(view, "mainPanel").activeSelf, Is.True);
        Assert.That(Time.timeScale, Is.Zero);

        Assert.That(view.transform.Find("Main Panel/Map Selection"), Is.Null);
        Get<Button>(view, "primaryButton").onClick.Invoke();
        Assert.That(Get<GameObject>(view, "mapPanel").activeSelf, Is.True);
        Assert.That(Get<GameObject>(view, "mainPanel").activeSelf, Is.False);
        Assert.That(Time.timeScale, Is.Zero);
        Get<Button>(view, "mapBackButton").onClick.Invoke();
        Assert.That(Get<GameObject>(view, "mapPanel").activeSelf, Is.False);
        Assert.That(Get<GameObject>(view, "mainPanel").activeSelf, Is.True);

        Scene initialScene = SceneManager.GetActiveScene();
        string initialScenePath = initialScene.path;
        int initialBuildIndex = initialScene.buildIndex;
        Get<Button>(view, "primaryButton").onClick.Invoke();
        yield return null;
        FindMapButton(view, initialScene.buildIndex).onClick.Invoke();
        yield return null;
        Assert.That(SceneManager.GetActiveScene(), Is.EqualTo(initialScene));
        Assert.That(GameStartMenu.CursorOwned, Is.False);
        Assert.That(view.gameObject.activeSelf, Is.False);
        Assert.That(Time.timeScale, Is.EqualTo(1f));
        bool restored = false;
        foreach (InputAction action in player.actions) restored |= action.enabled;
        Assert.That(restored, Is.True, "Starting must restore the player's enabled actions.");

        typeof(GameStartMenu).GetMethod("Open", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(menu, null);
        AssertPausedView(view);
        Get<Button>(view, "settingsButton").onClick.Invoke();
        Assert.That(Get<TextMeshProUGUI>(view, "settingsTitle").text, Does.Contain(GameStartMenuView.PauseTitle));
        Assert.That(Get<Image>(view, "backdrop").gameObject.activeSelf, Is.False);
        Get<Button>(view, "backButton").onClick.Invoke();
        Get<Button>(view, "quitButton").onClick.Invoke();
        Assert.That(Get<Button>(view, "quitButton").interactable, Is.False);
        float deadline = Time.realtimeSinceStartup + 45f;
        while (menu != null && Time.realtimeSinceStartup < deadline) yield return null;
        for (int i = 0; i < 5; i++) yield return null;
        Assert.That(Application.isPlaying, Is.True);
        Assert.That(SceneManager.GetActiveScene().path, Is.EqualTo(initialScenePath));
        GameStartMenuView returnedView = Object.FindAnyObjectByType<GameStartMenuView>();
        Assert.That(returnedView, Is.Not.Null);
        Assert.That(Get<TextMeshProUGUI>(returnedView, "primaryLabel").text, Is.EqualTo("开始游戏"));
        Assert.That(Get<GameObject>(returnedView, "statisticsPanel").activeSelf, Is.True);
        Assert.That(GameSaveSystem.Current.TotalGamesPlayed, Is.EqualTo(statistics.TotalGamesPlayed));
        Assert.That(GameSaveSystem.Current.GamesWon, Is.EqualTo(statistics.GamesWon));
        Assert.That(GameSaveSystem.Current.GamesLost, Is.EqualTo(statistics.GamesLost));
        Assert.That(GameSaveSystem.Current.StrategicResources, Is.EqualTo(statistics.StrategicResources));
        Assert.That(Get<TextMeshProUGUI>(returnedView, "strategicResourcesValue").text,
            Is.EqualTo(statistics.StrategicResources.ToString("N0")));
        Assert.That(GameStartMenu.CursorOwned, Is.True);
        Assert.That(Time.timeScale, Is.Zero);
        Assert.That(GameFlowManager.Instance.CurrentResult, Is.EqualTo(GameFlowManager.Result.Playing));
        Get<Button>(returnedView, "primaryButton").onClick.Invoke();
        FindMapButton(returnedView, initialBuildIndex).onClick.Invoke();
        yield return null;
        Assert.That(Time.timeScale, Is.EqualTo(1f));
        Assert.That(GameStartMenu.CursorOwned, Is.False);
    }

    [UnityTest]
    public IEnumerator SelectingAnotherMapLoadsItAndAllowsPauseAndResume()
    {
        yield return new ExitPlayMode();
        EditorSceneManager.OpenScene("Assets/Scenes/EditorWorkspace.unity", OpenSceneMode.Single);
        yield return new EnterPlayMode();
        for (int i = 0; i < 5; i++) yield return null;
        Scene initialScene = SceneManager.GetActiveScene();
        string initialScenePath = initialScene.path;
        GameStartMenuView view = Object.FindAnyObjectByType<GameStartMenuView>();
        Assert.That(view, Is.Not.Null);
        Assert.That(MapSceneCatalog.TryGet(0, out var facility), Is.True);
        Assert.That(facility.Path, Does.EndWith("/DOUBLE_LAYER_FACILITY.unity"));

        Get<Button>(view, "primaryButton").onClick.Invoke();
        Assert.That(SceneManager.GetActiveScene(), Is.EqualTo(initialScene));
        Assert.That(GameStartMenu.CursorOwned, Is.True);
        FindMapButton(view, facility.BuildIndex).onClick.Invoke();
        Assert.That(Get<Button>(view, "mapBackButton").interactable, Is.False);

        float deadline = Time.realtimeSinceStartup + 45f;
        while (SceneManager.GetActiveScene().buildIndex != facility.BuildIndex
            && Time.realtimeSinceStartup < deadline)
            yield return null;
        for (int i = 0; i < 5; i++) yield return null;

        Assert.That(SceneManager.GetActiveScene().path, Is.EqualTo(facility.Path));
        Assert.That(GameStartMenu.CursorOwned, Is.False);
        Assert.That(Time.timeScale, Is.EqualTo(1f));
        Assert.That(Object.FindAnyObjectByType<PlayerInput>(), Is.Not.Null);
        Assert.That(GameFlowManager.Instance, Is.Not.Null);
        Assert.That(WaveRewardService.Instance, Is.Not.Null);
        Assert.That(Object.FindAnyObjectByType<PlayerDamageDirectionUI>(), Is.Not.Null);
        Assert.That(EventSystem.current, Is.Not.Null);
        Assert.That(EventSystem.current.GetComponent<InputSystemUIInputModule>(), Is.Not.Null);

        GameStartMenu pauseMenu = Object.FindAnyObjectByType<GameStartMenu>();
        Assert.That(pauseMenu, Is.Not.Null, "The destination must have its own pause menu.");
        // Exercise the pause entry point because headless tests lack game-view keyboard focus.
        typeof(GameStartMenu).GetMethod("Open", BindingFlags.NonPublic | BindingFlags.Instance)
            .Invoke(pauseMenu, null);
        GameStartMenuView pauseView = Object.FindAnyObjectByType<GameStartMenuView>();
        Assert.That(pauseView, Is.Not.Null);
        Assert.That(GameStartMenu.CursorOwned, Is.True);
        Assert.That(Time.timeScale, Is.Zero);
        Assert.That(Get<TextMeshProUGUI>(pauseView, "primaryLabel").text, Is.EqualTo("继续游戏"));
        AssertPausedView(pauseView);
        Get<Button>(pauseView, "primaryButton").onClick.Invoke();
        yield return null;
        Assert.That(SceneManager.GetActiveScene().path, Is.EqualTo(facility.Path));
        Assert.That(GameStartMenu.CursorOwned, Is.False);
        Assert.That(Time.timeScale, Is.EqualTo(1f));
        typeof(GameStartMenu).GetMethod("Open", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(pauseMenu, null);
        Get<Button>(pauseView, "quitButton").onClick.Invoke();
        deadline = Time.realtimeSinceStartup + 45f;
        while (SceneManager.GetActiveScene().path != initialScenePath && Time.realtimeSinceStartup < deadline)
            yield return null;
        for (int i = 0; i < 5; i++) yield return null;
        Assert.That(Application.isPlaying, Is.True);
        Assert.That(SceneManager.GetActiveScene().path, Is.EqualTo(initialScenePath));
        GameStartMenuView returnedView = Object.FindAnyObjectByType<GameStartMenuView>();
        Assert.That(returnedView, Is.Not.Null);
        Assert.That(Get<TextMeshProUGUI>(returnedView, "primaryLabel").text, Is.EqualTo("开始游戏"));
        Assert.That(Get<GameObject>(returnedView, "statisticsPanel").activeSelf, Is.True);
        Assert.That(Time.timeScale, Is.Zero);
        Assert.That(GameStartMenu.CursorOwned, Is.True);
    }

    private static void AssertPausedView(GameStartMenuView view)
    {
        Assert.That(Get<TextMeshProUGUI>(view, "mainTitle").text, Is.EqualTo(GameStartMenuView.PauseTitle));
        Assert.That(Get<Button>(view, "quitButton").GetComponentInChildren<TextMeshProUGUI>().text,
            Is.EqualTo(GameStartMenuView.PauseQuitLabel));
        Assert.That(Get<GameObject>(view, "statisticsPanel").activeSelf, Is.False);
        Assert.That(Get<Image>(view, "backdrop").gameObject.activeSelf, Is.False);
        Assert.That(Get<GameObject>(view, "mainPanel").GetComponent<Image>().color.a, Is.InRange(.1f, .8f));
        Assert.That(Time.timeScale, Is.Zero);
    }

    private static Button FindMapButton(GameStartMenuView view, int buildIndex)
    {
        Transform option = Get<Transform>(view, "mapListContainer").Find("Map Option " + buildIndex);
        Assert.That(option, Is.Not.Null);
        return option.GetComponent<Button>();
    }

    private static T Get<T>(object target, string name)
    {
        FieldInfo field = target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.That(field, Is.Not.Null, "Missing field: " + name);
        return (T)field.GetValue(target);
    }
}
#endif
