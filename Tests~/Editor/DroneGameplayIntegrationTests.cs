#if UNITY_INCLUDE_TESTS && ENABLE_DRONE_CODE
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.Users;
using UnityEngine.TestTools;
using TowerDefense;

public sealed class DroneGameplayIntegrationTests
{
    private Keyboard keyboard;
    private InputSettings.EditorInputBehaviorInPlayMode originalEditorInputBehavior;
    private InputSettings.BackgroundBehavior originalBackgroundBehavior;
    private InputSettings.UpdateMode originalUpdateMode;
    private bool originalRunInBackground;
    private bool changedInputSettings;

    [Test]
    public void ResourcesPrefabContainsFlightAndCameraRig()
    {
        GameObject prefab = Resources.Load<GameObject>("QuadcopterDrone");

        Assert.That(prefab, Is.Not.Null, "Resources.Load must find the runtime drone prefab.");
        Assert.That(AssetDatabase.GetAssetPath(prefab), Is.EqualTo(QuadcopterDroneAuthoring.PrefabPath));
        Assert.That(prefab.GetComponent<Rigidbody>(), Is.Not.Null);
        Assert.That(prefab.GetComponent<QuadcopterFlightController>(), Is.Not.Null);
        Assert.That(prefab.GetComponent<DroneCameraController>(), Is.Not.Null);
        Assert.That(prefab.GetComponentInChildren<Camera>(true), Is.Not.Null);
        Assert.That(prefab.GetComponentInChildren<AudioListener>(true), Is.Not.Null);
    }

    [UnityTest]
    public IEnumerator VKeyTogglesDroneInTheMainGameplayScene()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/THREE_ROUTE_MERGE_MAP.unity", OpenSceneMode.Single);
        yield return new EnterPlayMode();
        for (int i = 0; i < 5; i++) yield return null;

        originalEditorInputBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
        originalBackgroundBehavior = InputSystem.settings.backgroundBehavior;
        originalUpdateMode = InputSystem.settings.updateMode;
        originalRunInBackground = Application.runInBackground;
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        Application.runInBackground = true;
        changedInputSettings = true;

        keyboard = InputSystem.AddDevice<Keyboard>();
        keyboard.MakeCurrent();

        DroneControlSession session = Object.FindAnyObjectByType<DroneControlSession>();
        Assert.That(session, Is.Not.Null, "Player bootstrap must create and bind a drone session.");
        MethodInfo ensurePlayer = typeof(FirstPersonPlayerBootstrap).GetMethod("EnsurePlayerForCurrentScene", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.That(ensurePlayer, Is.Not.Null);
        int dronesBeforeRepeatSetup = Object.FindObjectsByType<QuadcopterFlightController>(FindObjectsSortMode.None).Length;
        ensurePlayer.Invoke(null, null);
        yield return null;
        Assert.That(Object.FindObjectsByType<QuadcopterFlightController>(FindObjectsSortMode.None).Length,
            Is.EqualTo(dronesBeforeRepeatSetup), "Repeated bootstrap must not create another drone.");
        Assert.That(session.GetComponentsInChildren<DroneHUD>(true).Length, Is.EqualTo(1),
            "Repeated bootstrap must not duplicate the session HUD.");

        PlayerInput playerInput = session.GetComponentInChildren<PlayerInput>(true);
        Assert.That(playerInput, Is.Not.Null, "The gameplay player must expose PlayerInput.");
        if (playerInput.user.valid) InputUser.PerformPairingWithDevice(keyboard, playerInput.user);

        GameStartMenu startMenu = Object.FindAnyObjectByType<GameStartMenu>();
        Assert.That(startMenu, Is.Not.Null);
        startMenu.GetType().GetMethod("Close", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(startMenu, null);
        yield return null;
        yield return null;

        GameObject drone = GameObject.Find("Quadcopter Drone");
        Assert.That(drone, Is.Not.Null);
        Camera playerCamera = session.GetComponentInChildren<Camera>(true);
        Camera droneCamera = drone.GetComponentInChildren<Camera>(true);
        AudioListener playerAudio = session.GetComponentInChildren<AudioListener>(true);
        AudioListener droneAudio = drone.GetComponentInChildren<AudioListener>(true);
        PlayerWeaponCategorySwitcher weaponSwitcher = session.GetComponent<PlayerWeaponCategorySwitcher>();
        Assert.That(playerCamera, Is.Not.Null);
        Assert.That(droneCamera, Is.Not.Null);

        yield return Press(Key.V);

        Assert.That(session.State, Is.EqualTo(DroneControlState.Drone));
        Assert.That(playerInput.enabled, Is.False);
        Assert.That(playerCamera.enabled, Is.False);
        Assert.That(droneCamera.enabled, Is.True);
        Assert.That(playerAudio != null && playerAudio.enabled, Is.False);
        Assert.That(droneAudio != null && droneAudio.enabled, Is.True);
        Assert.That(weaponSwitcher != null && weaponSwitcher.enabled, Is.False);

        Call(startMenu, "Open");
        yield return null;
        Assert.That(drone.GetComponent<QuadcopterFlightController>().enabled, Is.False,
            "Opening a menu must pause drone flight input.");
        yield return Press(Key.V);
        Assert.That(session.State, Is.EqualTo(DroneControlState.Drone),
            "V cannot change modes while the menu owns the cursor.");
        Call(startMenu, "Close");
        yield return null;
        yield return null;

        yield return Press(Key.V);

        Assert.That(session.State, Is.EqualTo(DroneControlState.Player));
        Assert.That(playerInput.enabled, Is.True);
        Assert.That(playerCamera.enabled, Is.True);
        Assert.That(droneCamera.enabled, Is.False);
        Assert.That(playerAudio == null || playerAudio.enabled, Is.True);
        Assert.That(droneAudio == null || !droneAudio.enabled, Is.True);
        Assert.That(weaponSwitcher == null || weaponSwitcher.enabled, Is.True);
        Assert.That(drone.GetComponent<Rigidbody>().isKinematic, Is.True);

        PlayerHealth health = session.GetComponent<PlayerHealth>();
        yield return Press(Key.V);
        Assert.That(session.State, Is.EqualTo(DroneControlState.Drone));
        health.TakeDamage(health.CurrentHealth);
        Assert.That(session.State, Is.EqualTo(DroneControlState.Player),
            "Player death must return control to the player camera.");
        Assert.That(playerCamera.enabled, Is.True);
        health.ResetHealth();

        yield return Press(Key.V);
        Assert.That(session.State, Is.EqualTo(DroneControlState.Drone));
        Object.Destroy(drone);
        yield return null;
        yield return null;
        Assert.That(session.State, Is.EqualTo(DroneControlState.Player),
            "Drone destruction must restore player control.");
        Assert.That(playerCamera.enabled, Is.True);
    }

    [UnityTearDown]
    public IEnumerator Cleanup()
    {
        if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
        if (changedInputSettings)
        {
            InputSystem.settings.editorInputBehaviorInPlayMode = originalEditorInputBehavior;
            InputSystem.settings.backgroundBehavior = originalBackgroundBehavior;
            InputSystem.settings.updateMode = originalUpdateMode;
            Application.runInBackground = originalRunInBackground;
            changedInputSettings = false;
        }
        if (Application.isPlaying) yield return new ExitPlayMode();
    }

    private IEnumerator Press(Key key)
    {
        SendKeyboard(new KeyboardState(key));
        yield return null;
        yield return null;
        SendKeyboard(new KeyboardState());
        yield return null;
        yield return null;
    }

    private void SendKeyboard(KeyboardState state)
    {
        InputSettings.UpdateMode original = InputSystem.settings.updateMode;
        try
        {
            InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsManually;
            InputSystem.QueueStateEvent(keyboard, state);
            InputSystem.Update();
        }
        finally
        {
            InputSystem.settings.updateMode = original;
        }
    }

    private static void Call(object target, string method)
    {
        target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null);
    }
}
#endif
