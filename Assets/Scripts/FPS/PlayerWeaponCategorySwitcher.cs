using System.Collections.Generic;
using System.Reflection;
using KINEMATION.FPSAnimationPack.Scripts.Player;
using KINEMATION.FPSAnimationPack.Scripts.Weapon;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>1 = primary, 2 = pistols/revolvers, 3 = launchers. Repeat a key to cycle its category.</summary>
[DisallowMultipleComponent]
public sealed class PlayerWeaponCategorySwitcher : MonoBehaviour
{
    // The package is ignored by Git and exposes no indexed equip API. Keep the
    // adapter here, and use precisely the same immediate equip path as its wheel.
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly FieldInfo WeaponsField = typeof(FPSPlayer).GetField("_weapons", Flags);
    private static readonly FieldInfo IndexField = typeof(FPSPlayer).GetField("_activeWeaponIndex", Flags);
    private static readonly MethodInfo EquipMethod = typeof(FPSPlayer).GetMethod("EquipWeapon", Flags, null, new[] { typeof(bool), typeof(bool) }, null);
    private readonly int[] remembered = { -1, -1, -1, -1 };
    private readonly List<int> overriddenBindings = new List<int>();
    private readonly List<string> previousOverrides = new List<string>();
    private FPSPlayer player;
    private PlayerInput playerInput;
    private InputAction changeWeapon;
    private TrapPlacementController placement;
    private PlayerHealth health;
    private PlayerObserverMode observer;
    private bool warned;
    private InputActionMap categoryActions;

    private void Awake()
    {
        player = GetComponentInChildren<FPSPlayer>(true);
        playerInput = GetComponentInChildren<PlayerInput>(true);
        health = GetComponent<PlayerHealth>();
        observer = GetComponent<PlayerObserverMode>();
        categoryActions = new InputActionMap("Weapon Categories");
        AddCategoryAction("Primary", "1", "numpad1", PlayerWeaponCategory.Primary);
        AddCategoryAction("Secondary", "2", "numpad2", PlayerWeaponCategory.Secondary);
        AddCategoryAction("Launcher", "3", "numpad3", PlayerWeaponCategory.Launcher);
    }

    private void AddCategoryAction(string name, string digit, string numpad, PlayerWeaponCategory category)
    {
        InputAction action = categoryActions.AddAction(name, InputActionType.Button, "<Keyboard>/" + digit);
        action.AddBinding("<Keyboard>/" + numpad);
        action.performed += _ => TrySelectCategory(category);
    }

    private void OnEnable()
    {
        OverrideLegacyKeyboardBinding();
        categoryActions?.Enable();
    }

    private void Start() => OverrideLegacyKeyboardBinding();

    private void OverrideLegacyKeyboardBinding()
    {
        if (changeWeapon != null || playerInput == null || playerInput.actions == null) return;
        changeWeapon = playerInput.actions.FindAction("ChangeWeapon", false);
        if (changeWeapon == null) return;
        for (int i = 0; i < changeWeapon.bindings.Count; i++)
        {
            if (!changeWeapon.bindings[i].effectivePath.StartsWith("<Keyboard>/", System.StringComparison.OrdinalIgnoreCase)) continue;
            overriddenBindings.Add(i);
            previousOverrides.Add(changeWeapon.bindings[i].overridePath);
            changeWeapon.ApplyBindingOverride(i, string.Empty);
        }
    }

    private void OnDisable()
    {
        categoryActions?.Disable();
        for (int i = 0; i < overriddenBindings.Count; i++)
        {
            if (previousOverrides[i] == null) changeWeapon.RemoveBindingOverride(overriddenBindings[i]);
            else changeWeapon.ApplyBindingOverride(overriddenBindings[i], previousOverrides[i]);
        }
        overriddenBindings.Clear();
        previousOverrides.Clear();
        changeWeapon = null;
    }

    private void OnDestroy() => categoryActions?.Dispose();

    public bool TrySelectCategory(PlayerWeaponCategory category)
    {
        if (!isActiveAndEnabled || player == null || !player.isActiveAndEnabled
            || (playerInput != null && !playerInput.inputIsActive)
            || (health != null && health.IsDead) || (observer != null && observer.IsActive)
            || GameStartMenu.BlocksPlayerCursor || TrapSelectionMenu.CursorOwned || Time.timeScale <= 0f) return false;
        if (WeaponsField == null || IndexField == null || EquipMethod == null)
        {
            if (!warned) Debug.LogError("[PlayerWeaponCategorySwitcher] 当前 FPSPlayer 缺少索引切枪接口，请更新桥接适配。", this);
            warned = true;
            return false;
        }
        var weapons = WeaponsField.GetValue(player) as List<FPSWeapon>;
        int current = (int)IndexField.GetValue(player);
        if (weapons == null || current < 0 || current >= weapons.Count) return false;
        if (category < PlayerWeaponCategory.Primary || category > PlayerWeaponCategory.Launcher) return false;
        int target = FindWeaponIndex(weapons, current, category, remembered[(int)category]);
        if (target < 0) return false;
        if (placement == null) placement = FindAnyObjectByType<TrapPlacementController>();
        // Exit before hiding/equipping so the projection controller restores the
        // outgoing renderers even when its Update runs after this component.
        if (placement != null && placement.PlacementMode) placement.ExitPlacementMode();
        if (target == current) return true;
        remembered[(int)PlayerWeaponCategories.Resolve(weapons[current])] = current;
        remembered[(int)category] = target;
        player.CancelInvoke("EquipWeapon_Incremental");
        player.CancelInvoke("SetWeaponVisible");
        if (weapons[current] != null) weapons[current].gameObject.SetActive(false);
        IndexField.SetValue(player, target);
        weapons[target].gameObject.SetActive(true);
        EquipMethod.Invoke(player, new object[] { false, true });
        return true;
    }

    public static int FindWeaponIndex(IReadOnlyList<FPSWeapon> weapons, int current, PlayerWeaponCategory category, int rememberedIndex = -1)
    {
        if (weapons == null || weapons.Count == 0) return -1;
        bool inCategory = current >= 0 && current < weapons.Count && weapons[current] != null
            && PlayerWeaponCategories.Resolve(weapons[current]) == category;
        if (!inCategory && rememberedIndex >= 0 && rememberedIndex < weapons.Count
            && weapons[rememberedIndex] != null && PlayerWeaponCategories.Resolve(weapons[rememberedIndex]) == category)
            return rememberedIndex;
        int start = inCategory ? current + 1 : 0;
        for (int offset = 0; offset < weapons.Count; offset++)
        {
            int index = (start + offset) % weapons.Count;
            if (weapons[index] != null && PlayerWeaponCategories.Resolve(weapons[index]) == category) return index;
        }
        return -1;
    }
}
