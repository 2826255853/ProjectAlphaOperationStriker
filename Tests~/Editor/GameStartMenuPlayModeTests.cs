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
        EditorSceneManager.OpenScene("Assets/Scenes/THREE_ROUTE_MERGE_MAP.unity", OpenSceneMode.Single);
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
        PlayerInput player = Object.FindAnyObjectByType<PlayerInput>();
        Assert.That(player, Is.Not.Null, "The real scene must bootstrap its player.");
        Assert.That(player.actions, Is.Not.Null);
        foreach (InputAction action in player.actions)
            Assert.That(action.enabled, Is.False, "Menu must disable player action " + action.name);

        Get<Button>(view, "settingsButton").onClick.Invoke();
        Assert.That(Get<GameObject>(view, "settingsPanel").activeSelf, Is.True);
        Assert.That(Get<GameObject>(view, "mainPanel").activeSelf, Is.False);
        Get<Slider>(view, "volumeSlider").value = .37f;
        Assert.That(AudioListener.volume, Is.EqualTo(.37f).Within(.001f));
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
    }

    [UnityTest]
    public IEnumerator SelectingAnotherMapLoadsItAndAllowsPauseAndResume()
    {
        Scene initialScene = SceneManager.GetActiveScene();
        GameStartMenuView view = Object.FindAnyObjectByType<GameStartMenuView>();
        Assert.That(view, Is.Not.Null);
        Assert.That(MapSceneCatalog.TryGet(1, out var facility), Is.True);
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
        Get<Button>(pauseView, "primaryButton").onClick.Invoke();
        yield return null;
        Assert.That(SceneManager.GetActiveScene().path, Is.EqualTo(facility.Path));
        Assert.That(GameStartMenu.CursorOwned, Is.False);
        Assert.That(Time.timeScale, Is.EqualTo(1f));
    }

    private static Button FindMapButton(GameStartMenuView view, int buildIndex)
    {
        Transform option = view.transform.Find("Map Panel/Map List/Map Option " + buildIndex);
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
