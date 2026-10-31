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

        Get<Button>(view, "mapButton").onClick.Invoke();
        Assert.That(Get<GameObject>(view, "mapPanel").activeSelf, Is.True);
        Assert.That(Get<GameObject>(view, "mainPanel").activeSelf, Is.False);
        Button[] mapOptions = view.GetComponentsInChildren<Button>(true);
        bool hasFirstMap = false;
        foreach (Button mapOption in mapOptions)
            hasFirstMap |= mapOption != null && mapOption.name == "Map Option 0";
        Assert.That(hasFirstMap, Is.True);
        Get<Button>(view, "mapBackButton").onClick.Invoke();
        Assert.That(Get<GameObject>(view, "mapPanel").activeSelf, Is.False);
        Assert.That(Get<GameObject>(view, "mainPanel").activeSelf, Is.True);

        Get<Button>(view, "primaryButton").onClick.Invoke();
        yield return null;
        Assert.That(GameStartMenu.CursorOwned, Is.False);
        Assert.That(view.gameObject.activeSelf, Is.False);
        Assert.That(Time.timeScale, Is.EqualTo(1f));
        bool restored = false;
        foreach (InputAction action in player.actions) restored |= action.enabled;
        Assert.That(restored, Is.True, "Starting must restore the player's enabled actions.");

        // Further input-event and cursor regression cases are added alongside fixes.
    }

    private static T Get<T>(object target, string name)
    {
        FieldInfo field = target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.That(field, Is.Not.Null, "Missing field: " + name);
        return (T)field.GetValue(target);
    }
}
#endif
