#if UNITY_INCLUDE_TESTS
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using KINEMATION.FPSAnimationPack.Scripts.Player;
using KINEMATION.FPSAnimationPack.Scripts.Weapon;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

public sealed class PlayerWeaponCategoryTests
{
    [TestCase("M1911", PlayerWeaponCategory.Secondary)]
    [TestCase("Kolibri", PlayerWeaponCategory.Secondary)]
    [TestCase("X18", PlayerWeaponCategory.Secondary)]
    [TestCase("Viper-357", PlayerWeaponCategory.Secondary)]
    [TestCase("RPG", PlayerWeaponCategory.Launcher)]
    [TestCase("DGL50", PlayerWeaponCategory.Launcher)]
    [TestCase("AK", PlayerWeaponCategory.Primary)]
    [TestCase("Mk14EBR", PlayerWeaponCategory.Primary)]
    [TestCase("MGX5", PlayerWeaponCategory.Primary)]
    [TestCase("PDW90", PlayerWeaponCategory.Primary)]
    [TestCase("Striker-V", PlayerWeaponCategory.Primary)]
    [TestCase("FutureCustomWeapon", PlayerWeaponCategory.Primary)]
    public void WeaponFamiliesSelectEquipmentCategories(string name, PlayerWeaponCategory expected)
    {
        Assert.That(PlayerWeaponCategories.FromClass(WeaponDamageStats.DefaultsFor(name).WeaponClass), Is.EqualTo(expected));
    }

    private Keyboard keyboard;
    private Mouse mouse;
    private string[] previousPreferences;
    private InputSettings.EditorInputBehaviorInPlayMode originalEditorInputBehavior;
    private bool changedEditorInputBehavior;
    private InputSettings.BackgroundBehavior originalBackgroundBehavior;
    private bool originalRunInBackground;

    [UnityTearDown]
    public IEnumerator Cleanup()
    {
        if (!Application.isPlaying) yield break;
        if (previousPreferences != null)
        {
            for (int i = 0; i < previousPreferences.Length; i++)
            {
                string key = "PlayerWeaponLoadout.Slot" + i;
                if (previousPreferences[i] == null) PlayerPrefs.DeleteKey(key);
                else PlayerPrefs.SetString(key, previousPreferences[i]);
            }
            PlayerPrefs.Save();
        }
        if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
        if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
        if (changedEditorInputBehavior)
        {
            InputSystem.settings.editorInputBehaviorInPlayMode = originalEditorInputBehavior;
            InputSystem.settings.backgroundBehavior = originalBackgroundBehavior;
            Application.runInBackground = originalRunInBackground;
        }
        yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator ConfigureAndEnterCurrentMap() => CheckLoadout(false);

    [UnityTest]
    public IEnumerator ConfigureAndEnterAnotherMap() => CheckLoadout(true);

    private IEnumerator CheckLoadout(bool changeMap)
    {
        EditorSceneManager.OpenScene("Assets/Scenes/THREE_ROUTE_MERGE_MAP.unity", OpenSceneMode.Single);
        yield return new EnterPlayMode();
        for (int i = 0; i < 5; i++) yield return null;
        var switcher = Object.FindAnyObjectByType<PlayerWeaponCategorySwitcher>();
        Assert.That(switcher.Loadout, Is.Not.Null);
        var menu = Object.FindAnyObjectByType<GameStartMenu>();
        var view = Object.FindAnyObjectByType<GameStartMenuView>();
        Assert.That(switcher.TrySelectSlot(1), Is.False);
        previousPreferences = new string[3];
        for (int i = 0; i < 3; i++)
        {
            string key = "PlayerWeaponLoadout.Slot" + i;
            previousPreferences[i] = PlayerPrefs.HasKey(key) ? PlayerPrefs.GetString(key) : null;
        }
        Click(view, "Main Panel/Weapon Loadout");
        var previewed = new HashSet<string>();
        for (int slot = 0; slot < 3; slot++)
        {
            string initial = switcher.Loadout[slot];
            var seenInSlot = new HashSet<string>();
            do
            {
                string id = switcher.Loadout[slot];
                Assert.That(seenInSlot.Add(id), Is.True, "Weapon selection must wrap to its starting point.");
                previewed.Add(id);
                var portrait = view.transform.Find("Weapon Loadout Panel/Weapon Portrait " + (slot + 1))
                    .GetComponent<UnityEngine.UI.Image>();
                Assert.That(portrait.isActiveAndEnabled && portrait.sprite != null, Is.True, id);
                Assert.That(portrait.sprite.name, Is.EqualTo(id));
                Click(view, "Weapon Loadout Panel/Next " + (slot + 1));
            } while (switcher.Loadout[slot] != initial);
        }
        Assert.That(previewed.Count, Is.EqualTo(18));
        string[] chosen = new string[3];
        for (int slot = 0; slot < 3; slot++)
        {
            string original = switcher.Loadout[slot];
            Click(view, "Weapon Loadout Panel/Next " + (slot + 1));
            chosen[slot] = switcher.Loadout[slot];
            Assert.That(chosen[slot], Is.Not.Null.And.Not.EqualTo(original));
            Assert.That(PlayerPrefs.GetString("PlayerWeaponLoadout.Slot" + slot), Is.EqualTo(chosen[slot]));
        }
        Assert.That(chosen[1], Is.Not.EqualTo(chosen[2]));
        Assert.That(switcher.Loadout.TryChoose(1, chosen[2]), Is.False);
        Assert.That(switcher.Loadout.TryChoose(0, chosen[1]), Is.False);
        Click(view, "Weapon Loadout Panel/Loadout Back");
        Click(view, "Main Panel/Primary Action");
        int destination = MapSceneCatalog.GetCurrentBuildIndex();
        if (changeMap)
        {
            foreach (var map in MapSceneCatalog.GetMaps())
            {
                if (map.BuildIndex == destination) continue;
                destination = map.BuildIndex;
                break;
            }
            Assert.That(destination, Is.Not.EqualTo(MapSceneCatalog.GetCurrentBuildIndex()));
        }
        menu.SelectMap(destination);
        for (int i = 0; i < 600 && UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex != destination; i++) yield return null;
        for (int i = 0; i < 5; i++) yield return null;
        Assert.That(UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex, Is.EqualTo(destination));
        switcher = Object.FindAnyObjectByType<PlayerWeaponCategorySwitcher>();
        var player = Object.FindAnyObjectByType<FPSPlayer>();
        var playerInput = player.transform.root.GetComponentInChildren<PlayerInput>(true);
        var weapons = (List<FPSWeapon>)typeof(FPSPlayer).GetField("_weapons", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(player);
        Assert.That(weapons.Count, Is.EqualTo(3));
        Assert.That(player.GetActiveWeapon(), Is.SameAs(weapons[0]));
        for (int i = 0; i < 3; i++)
        {
            Assert.That(weapons[i].name.Replace("(Clone)", "").Trim(), Is.EqualTo(chosen[i]));
            Assert.That(PlayerWeaponCategories.Resolve(weapons[i]), Is.EqualTo(i == 0 ? PlayerWeaponCategory.Secondary : PlayerWeaponCategory.Primary));
        }
        if (changeMap) yield break;
        originalEditorInputBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
        originalBackgroundBehavior = InputSystem.settings.backgroundBehavior;
        originalRunInBackground = Application.runInBackground;
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        Application.runInBackground = true;
        InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        changedEditorInputBehavior = true;
        keyboard = InputSystem.AddDevice<Keyboard>();
        keyboard.MakeCurrent();
        if (playerInput.user.valid)
            UnityEngine.InputSystem.Users.InputUser.PerformPairingWithDevice(keyboard, playerInput.user);
        yield return Press(Key.Digit2);
        Assert.That(player.GetActiveWeapon(), Is.SameAs(weapons[1]));
        yield return Press(Key.Digit3);
        yield return Press(Key.Digit3);
        Assert.That(player.GetActiveWeapon(), Is.SameAs(weapons[2]));
        yield return Press(Key.Digit1);
        Assert.That(player.GetActiveWeapon(), Is.SameAs(weapons[0]));
        mouse = InputSystem.AddDevice<Mouse>();
        mouse.MakeCurrent();
        if (playerInput.user.valid)
            UnityEngine.InputSystem.Users.InputUser.PerformPairingWithDevice(mouse, playerInput.user);
        var reached = new HashSet<FPSWeapon>();
        for (int i = 0; i < 4; i++)
        {
            InputSystem.QueueDeltaStateEvent(mouse.scroll, new Vector2(0f, 120f));
            yield return null;
            yield return new WaitForSeconds(1f);
            Assert.That(weapons.Contains(player.GetActiveWeapon()), Is.True);
            reached.Add(player.GetActiveWeapon());
        }
        Assert.That(reached.Count, Is.EqualTo(3), "Wheel cycles only the three carried weapons.");
        TrapPlacementController placement = Object.FindAnyObjectByType<TrapPlacementController>();
        Assert.That(placement, Is.Not.Null);
        placement.EnterPlacementMode();
        Assert.That(placement.PlacementMode, Is.True);
        yield return Press(Key.Numpad2);
        Assert.That(placement.PlacementMode, Is.False);
        Assert.That(player.GetActiveWeapon(), Is.SameAs(weapons[1]));
        FPSWeapon before = player.GetActiveWeapon();
        yield return Press(Key.F);
        yield return new WaitForSeconds(.6f);
        Assert.That(player.GetActiveWeapon(), Is.SameAs(before), "Old F binding must no longer cycle all weapons.");

        TrapSelectionMenu trapMenu = Object.FindAnyObjectByType<TrapSelectionMenu>();
        Call(trapMenu, "SetOpen", true);
        yield return Press(Key.Digit3);
        Assert.That(player.GetActiveWeapon(), Is.SameAs(before));
        Call(trapMenu, "SetOpen", false);
        yield return null;
        Call(menu, "Open");
        Assert.That(view.transform.Find("Main Panel/Weapon Loadout").gameObject.activeInHierarchy, Is.False);
        yield return Press(Key.Digit1);
        Assert.That(player.GetActiveWeapon(), Is.SameAs(before));
        Call(menu, "Close");
        yield return null;
        switcher.enabled = false;
        Assert.That(playerInput.actions.FindAction("ChangeWeapon").bindings[0].effectivePath, Is.EqualTo("<Keyboard>/f"));
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
        // EditMode's EnterPlayMode runner does not reliably process queued game
        // keyboard events in batch mode. Actions can be tested synchronously;
        // unlike frame-edge polling, they do not depend on a later Update tick.
        var original = InputSystem.settings.updateMode;
        try
        {
            InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsManually;
            InputSystem.QueueStateEvent(keyboard, state);
            InputSystem.Update();
        }
        finally { InputSystem.settings.updateMode = original; }
    }

    private static void Click(GameStartMenuView view, string path)
    {
        var button = view.transform.Find(path).GetComponent<UnityEngine.UI.Button>();
        Assert.That(button.isActiveAndEnabled && button.interactable, Is.True, path);
        button.onClick.Invoke();
    }

    private static void Call(object target, string method, params object[] args)
    {
        Assert.That(target, Is.Not.Null);
        target.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(target, args);
    }
}
#endif
