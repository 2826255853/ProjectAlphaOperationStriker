using System.Collections.Generic;
using System.Reflection;
using KINEMATION.FPSAnimationPack.Scripts.Player;
using KINEMATION.FPSAnimationPack.Scripts.Weapon;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Selects one of the three weapons carried into the match.</summary>
[DefaultExecutionOrder(100)]
[DisallowMultipleComponent]
public sealed class PlayerWeaponCategorySwitcher : MonoBehaviour
{
    // The package is ignored by Git and exposes no indexed equip API. Keep the
    // adapter here, and use precisely the same immediate equip path as its wheel.
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly FieldInfo WeaponsField = typeof(FPSPlayer).GetField("_weapons", Flags);
    private static readonly FieldInfo IndexField = typeof(FPSPlayer).GetField("_activeWeaponIndex", Flags);
    private static readonly MethodInfo EquipMethod = typeof(FPSPlayer).GetMethod("EquipWeapon", Flags, null, new[] { typeof(bool), typeof(bool) }, null);
    private readonly Dictionary<string, FPSWeapon> arsenal = new Dictionary<string, FPSWeapon>();
    public PlayerWeaponLoadout Loadout { get; private set; }
    private readonly List<int> overriddenBindings = new List<int>();
    private readonly List<string> previousOverrides = new List<string>();
    private FPSPlayer player;
    private PlayerInput playerInput;
    private InputAction changeWeapon;
    private TrapPlacementController placement;
    private PlayerHealth health;
    private PlayerObserverMode observer;
    private InputActionMap slotActions;

    private void Awake()
    {
        player = GetComponentInChildren<FPSPlayer>(true);
        playerInput = GetComponentInChildren<PlayerInput>(true);
        health = GetComponent<PlayerHealth>();
        observer = GetComponent<PlayerObserverMode>();
        slotActions = new InputActionMap("Weapon Slots");
        for (int i = 0; i < PlayerWeaponLoadout.SlotCount; i++)
        {
            int slot = i;
            InputAction action = slotActions.AddAction("Slot" + (slot + 1), InputActionType.Button, "<Keyboard>/" + (slot + 1));
            action.AddBinding("<Keyboard>/numpad" + (slot + 1));
            action.performed += _ => TrySelectSlot(slot);
        }
    }

    private void OnEnable()
    {
        OverrideLegacyKeyboardBinding();
        slotActions?.Enable();
    }

    private void Start()
    {
        OverrideLegacyKeyboardBinding();
        if (player == null || IndexField == null || EquipMethod == null ||
            !(WeaponsField?.GetValue(player) is List<FPSWeapon> weapons) || weapons.Count == 0)
        {
            Debug.LogError("Player weapon inventory is unavailable.", this);
            return;
        }
        Loadout = new PlayerWeaponLoadout();
        foreach (FPSWeapon weapon in weapons)
        {
            if (weapon == null) continue;
            string id = weapon.name.Replace("(Clone)", string.Empty).Trim();
            if (!arsenal.TryAdd(id, weapon)) continue;
            Loadout.AddWeapon(id, PlayerWeaponCategories.Resolve(weapon));
        }
        Loadout.Restore();
        ApplyLoadout();
    }

    public void ApplyLoadout()
    {
        if (Loadout == null) return;
        var weapons = (List<FPSWeapon>)WeaponsField.GetValue(player);
        var carried = new List<FPSWeapon>(PlayerWeaponLoadout.SlotCount);
        for (int slot = 0; slot < PlayerWeaponLoadout.SlotCount; slot++)
        {
            if (Loadout[slot] == null)
            {
                Debug.LogError($"Missing weapon for loadout slot {slot + 1}.", this);
                return;
            }
            carried.Add(arsenal[Loadout[slot]]);
        }
        var outgoing = player.GetActiveWeapon();
        weapons.Clear();
        weapons.AddRange(carried);
        Equip(weapons, outgoing, 0);
    }

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
        slotActions?.Disable();
        for (int i = 0; i < overriddenBindings.Count; i++)
        {
            if (previousOverrides[i] == null) changeWeapon.RemoveBindingOverride(overriddenBindings[i]);
            else changeWeapon.ApplyBindingOverride(overriddenBindings[i], previousOverrides[i]);
        }
        overriddenBindings.Clear();
        previousOverrides.Clear();
        changeWeapon = null;
    }

    private void OnDestroy() => slotActions?.Dispose();

    public bool TrySelectSlot(int slot)
    {
        if (!isActiveAndEnabled || Loadout == null || player == null || !player.isActiveAndEnabled
            || (playerInput != null && !playerInput.inputIsActive)
            || (health != null && health.IsDead) || (observer != null && observer.IsActive)
            || GameStartMenu.BlocksPlayerCursor || TrapSelectionMenu.CursorOwned || Time.timeScale <= 0f) return false;
        var weapons = (List<FPSWeapon>)WeaponsField.GetValue(player);
        if (weapons.Count != PlayerWeaponLoadout.SlotCount || slot < 0 || slot >= weapons.Count) return false;
        if (placement == null) placement = FindAnyObjectByType<TrapPlacementController>();
        if (placement != null && placement.PlacementMode) placement.ExitPlacementMode();
        if (slot != (int)IndexField.GetValue(player) || player.IsInvoking("EquipWeapon_Incremental"))
            Equip(weapons, player.GetActiveWeapon(), slot);
        return true;
    }

    private void Equip(List<FPSWeapon> weapons, FPSWeapon outgoing, int slot)
    {
        player.CancelInvoke("EquipWeapon_Incremental");
        player.CancelInvoke("SetWeaponVisible");
        if (outgoing != null && outgoing != weapons[slot]) outgoing.gameObject.SetActive(false);
        IndexField.SetValue(player, slot);
        weapons[slot].gameObject.SetActive(true);
        EquipMethod.Invoke(player, new object[] { false, true });
    }
}
