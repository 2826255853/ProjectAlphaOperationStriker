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

    [Test]
    public void MissingCategoryLeavesSelectionUnchanged()
    {
        Assert.That(PlayerWeaponCategorySwitcher.FindWeaponIndex(new FPSWeapon[0], 0, PlayerWeaponCategory.Primary), Is.EqualTo(-1));
        Assert.That(PlayerWeaponCategorySwitcher.FindWeaponIndex(new FPSWeapon[] { null }, 0, PlayerWeaponCategory.Secondary), Is.EqualTo(-1));
    }

    private Keyboard keyboard;
    private InputSettings.EditorInputBehaviorInPlayMode originalEditorInputBehavior;
    private bool changedEditorInputBehavior;
    private InputSettings.BackgroundBehavior originalBackgroundBehavior;
    private bool originalRunInBackground;

    [UnityTearDown]
    public IEnumerator Cleanup()
    {
        if (!Application.isPlaying) yield break;
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
    public IEnumerator LivePlayerKeysCycleRememberAndRespectMenusAndPlacement()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/THREE_ROUTE_MERGE_MAP.unity", OpenSceneMode.Single);
        yield return new EnterPlayMode();
        for (int i = 0; i < 5; i++) yield return null;
        FPSPlayer player = Object.FindAnyObjectByType<FPSPlayer>();
        Assert.That(player, Is.Not.Null);
        var switcher = player.transform.root.GetComponent<PlayerWeaponCategorySwitcher>();
        Assert.That(switcher, Is.Not.Null, "Bootstrap must install the category bridge.");
        Transform playerRoot = player.transform.root;
        var playerInput = playerRoot.GetComponentInChildren<PlayerInput>(true);
        Assert.That(playerInput, Is.Not.Null, "The real player must expose its PlayerInput before switching.");
        GameStartMenu menu = Object.FindAnyObjectByType<GameStartMenu>();
        Assert.That(switcher.TrySelectCategory(PlayerWeaponCategory.Launcher), Is.False, "Start menu blocks category switches.");
        Call(menu, "Close");
        yield return null;
        yield return null;
        var weapons = (List<FPSWeapon>)typeof(FPSPlayer).GetField("_weapons", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(player);
        Assert.That(weapons.Count, Is.EqualTo(20));
        var counts = new Dictionary<PlayerWeaponCategory, int>();
        foreach (FPSWeapon weapon in weapons)
        {
            PlayerWeaponCategory category = PlayerWeaponCategories.Resolve(weapon);
            if (!counts.ContainsKey(category)) counts[category] = 0;
            counts[category]++;
        }
        Assert.That(counts[PlayerWeaponCategory.Primary], Is.EqualTo(14));
        Assert.That(counts[PlayerWeaponCategory.Secondary], Is.EqualTo(4));
        Assert.That(counts[PlayerWeaponCategory.Launcher], Is.EqualTo(2));
        Assert.That(switcher.TrySelectCategory(PlayerWeaponCategory.Primary), Is.True, "Direct category selection must equip through the package API.");
        AssertCategory(player, PlayerWeaponCategory.Primary);
        Assert.That(switcher.TrySelectCategory(PlayerWeaponCategory.Secondary), Is.True);

        Assert.That(player != null, Is.True, "Switching must preserve the native player object.");
        Assert.That(playerRoot != null, Is.True, "Switching must preserve the native player root.");
        Assert.That(playerInput, Is.Not.Null, "The player must keep its input component when switching weapons.");
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
        var actions = (InputActionMap)typeof(PlayerWeaponCategorySwitcher).GetField("categoryActions", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(switcher);
        foreach (InputAction action in actions)
        {
            Assert.That(action.enabled, Is.True);
            action.performed += TraceCategoryAction;
            Assert.That(action.controls.Count, Is.GreaterThanOrEqualTo(2), "Both keyboard and numpad bindings must resolve: " + action.name);
        }
        Assert.That(InputControlPath.Matches(actions.FindAction("Primary").bindings[0].effectivePath, keyboard.digit1Key), Is.True, "Primary binding must match the digit1 control at " + keyboard.digit1Key.path);
        yield return Press(Key.Digit1);
        AssertCategory(player, PlayerWeaponCategory.Primary);
        FPSWeapon primary = player.GetActiveWeapon();
        yield return Press(Key.Digit2);
        AssertCategory(player, PlayerWeaponCategory.Secondary);
        yield return Press(Key.Digit3);
        AssertCategory(player, PlayerWeaponCategory.Launcher);
        Assert.That(switcher.TrySelectCategory(PlayerWeaponCategory.Primary), Is.True);
        Assert.That(player.GetActiveWeapon(), Is.SameAs(primary), "Returning to a category remembers its previous weapon.");

        foreach (PlayerWeaponCategory category in new[] { PlayerWeaponCategory.Primary, PlayerWeaponCategory.Secondary, PlayerWeaponCategory.Launcher })
        {
            if (PlayerWeaponCategories.Resolve(player.GetActiveWeapon()) != category) switcher.TrySelectCategory(category);
            FPSWeapon first = player.GetActiveWeapon();
            var seen = new HashSet<FPSWeapon> { first };
            for (int i = 0; i < counts[category]; i++)
            {
                Assert.That(switcher.TrySelectCategory(category), Is.True);
                AssertCategory(player, category);
                seen.Add(player.GetActiveWeapon());
            }
            Assert.That(seen.Count, Is.EqualTo(counts[category]), "Every weapon in the category must be reachable.");
            Assert.That(player.GetActiveWeapon(), Is.SameAs(first), "Category cycle wraps.");
        }

        TrapPlacementController placement = Object.FindAnyObjectByType<TrapPlacementController>();
        Assert.That(placement, Is.Not.Null);
        placement.EnterPlacementMode();
        Assert.That(placement.PlacementMode, Is.True);
        yield return Press(Key.Numpad2);
        Assert.That(placement.PlacementMode, Is.False);
        AssertCategory(player, PlayerWeaponCategory.Secondary);
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
        Assert.That(InputControlPath.Matches(actions.FindAction("Primary").bindings[0].effectivePath, keyboard.digit1Key), Is.True, "Primary binding must match the digit1 control at " + keyboard.digit1Key.path);
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

    private static void TraceCategoryAction(InputAction.CallbackContext context)
    {
        FPSPlayer player = Object.FindAnyObjectByType<FPSPlayer>();
        PlayerInput input = Object.FindAnyObjectByType<PlayerInput>();
        PlayerHealth health = Object.FindAnyObjectByType<PlayerHealth>();
        PlayerObserverMode observer = Object.FindAnyObjectByType<PlayerObserverMode>();
        Debug.Log("CATEGORY_EVENT: " + context.action.name + " input=" + (input != null && input.inputIsActive)
            + " player=" + (player != null && player.isActiveAndEnabled) + " dead=" + (health != null && health.IsDead)
            + " observer=" + (observer != null && observer.IsActive) + " menu=" + GameStartMenu.BlocksPlayerCursor
            + " trap=" + TrapSelectionMenu.CursorOwned + " time=" + Time.timeScale);
    }
    private static void AssertCategory(FPSPlayer player, PlayerWeaponCategory category)
    {
        Assert.That(PlayerWeaponCategories.Resolve(player.GetActiveWeapon()), Is.EqualTo(category));
        Assert.That(player.GetActiveWeapon().gameObject.activeSelf, Is.True);
    }

    private static void Call(object target, string method, params object[] args)
    {
        Assert.That(target, Is.Not.Null);
        target.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(target, args);
    }
}
#endif
