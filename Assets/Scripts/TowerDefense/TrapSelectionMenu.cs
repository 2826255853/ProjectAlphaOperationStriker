using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Runtime state and input owner for the editor-authored trap selection Canvas.</summary>
public sealed class TrapSelectionMenu : MonoBehaviour
{
    private const string ViewPrefabPath = "UI/TrapSelectionMenu";
    [SerializeField] private KeyCode toggleKey = KeyCode.N;
    [SerializeField] private string resourcesFolder = "";
    private readonly List<TrapDefinition> available = new List<TrapDefinition>();
    private readonly List<TrapDefinition> filtered = new List<TrapDefinition>();
    private static readonly string[] MountFilters = { "全部", "地面", "墙面", "天花板" };
    private TrapPlacementController placement;
    private TrapSelectionMenuView view;
    private int mountFilter;
    private int currentPage;
    private bool open;
    private CursorLockMode previousLock;
    private bool previousVisible;
    private readonly List<InputAction> suppressedWeaponActions = new List<InputAction>();
    private bool restoreWeaponInputPending;
    private static int cursorReleasedFrame = -1;
    private readonly EconomyUIState economy = new EconomyUIState();

    public string BalanceText => economy.BalanceText;
    public static bool IsOpen { get; private set; }
    /// <summary>True while the menu owns the cursor, including its closing frame.</summary>
    public static bool CursorOwned => IsOpen || cursorReleasedFrame == Time.frameCount;
    internal IReadOnlyList<TrapDefinition> VisibleTraps => filtered;
    internal int FilterIndex => mountFilter;
    internal int CurrentPage => currentPage;
    internal TrapPlacementController Placement => GetPlacement();
    internal TrapDefinition SelectedTrap
    {
        get
        {
            TrapPlacementController target = GetPlacement();
            return target != null ? target.GetTrapSlot(target.SelectedSlot) : null;
        }
    }

    public static bool MatchesMountFilter(TrapDefinition definition, int filter)
        => definition != null && (filter == 0 || (int)definition.MountType == filter - 1);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureMenu()
    {
        if (FindAnyObjectByType<TrapSelectionMenu>() != null) return;
        var host = new GameObject("Trap Selection Menu");
        DontDestroyOnLoad(host);
        host.AddComponent<TrapSelectionMenu>();
    }

    private void Awake()
    {
        GameObject prefab = Resources.Load<GameObject>(ViewPrefabPath);
        if (prefab == null)
        {
            Debug.LogError($"Trap selection Canvas prefab missing: Assets/Resources/{ViewPrefabPath}.prefab", this);
            enabled = false;
            return;
        }
        GameObject instance = Instantiate(prefab, transform);
        instance.name = "Trap Selection Menu Canvas";
        view = instance.GetComponent<TrapSelectionMenuView>();
        if (view == null)
        {
            Debug.LogError($"Trap selection Canvas prefab has no {nameof(TrapSelectionMenuView)} component.", prefab);
            Destroy(instance);
            enabled = false;
            return;
        }
        view.Initialize(this);
        view.SetVisible(false);
        Refresh();
    }

    private void OnEnable()
    {
        economy.Enable();
        economy.Changed += RefreshPresentation;
    }

    private void OnDisable()
    {
        economy.Changed -= RefreshPresentation;
        economy.Dispose();
        if (open) SetOpen(false);
        restoreWeaponInputPending = false;
        SetWeaponInputSuppressed(false);
        IsOpen = false;
    }

    private void OnDestroy()
    {
        economy.Changed -= RefreshPresentation;
        economy.Dispose();
    }

    private void Update()
    {
        economy.Refresh();
        UpdatePendingWeaponInputRestore();
        if (GameStartMenu.CursorOwned || Keyboard.current == null) return;
        if (toggleKey == KeyCode.N && Keyboard.current.nKey.wasPressedThisFrame) SetOpen(!open);
        if (open && Keyboard.current.escapeKey.wasPressedThisFrame && !view.BackFromUpgrade()) SetOpen(false);
    }

    private void Refresh()
    {
        available.Clear();
        foreach (TrapDefinition definition in Resources.LoadAll<TrapDefinition>(resourcesFolder))
        {
            // These Resources assets are runtime support data, not standalone menu entries:
            // the ceiling fixture is editor-only and Phalanx is created by the sentry's
            // level-four branch rather than deployed as an independent trap.
            if (definition == null ||
                string.Equals(definition.TrapId, "ceiling_test", System.StringComparison.Ordinal) ||
                string.Equals(definition.TrapId, SentryPhalanxBranchAdapter.BranchId, System.StringComparison.Ordinal)) continue;
            if (!available.Contains(definition)) available.Add(definition);
        }
        available.Sort((a, b) =>
        {
            // Group by Ground / Wall / Ceiling, then by the level-one deployment cost.
            int mountOrder = a.MountType.CompareTo(b.MountType);
            if (mountOrder != 0) return mountOrder;
            int costOrder = a.Cost.CompareTo(b.Cost);
            if (costOrder != 0) return costOrder;
            return string.Compare(a.DisplayName, b.DisplayName, System.StringComparison.Ordinal);
        });
        RebuildFiltered();
    }

    private void RebuildFiltered()
    {
        filtered.Clear();
        foreach (TrapDefinition definition in available)
            if (MatchesMountFilter(definition, mountFilter)) filtered.Add(definition);
        currentPage = Mathf.Clamp(currentPage, 0, Mathf.Max(0, Mathf.CeilToInt(filtered.Count / 54f) - 1));
    }

    private void SetOpen(bool value)
    {
        if (open == value) return;
        if (view == null) return;
        open = value;
        IsOpen = value;
        if (open)
        {
            Refresh();
            previousLock = Cursor.lockState;
            previousVisible = Cursor.visible;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            restoreWeaponInputPending = false;
            SetWeaponInputSuppressed(true);
            view.Refresh();
            view.SetVisible(true);
        }
        else
        {
            // Do not re-enable a weapon action while the menu-picking mouse click is still held.
            restoreWeaponInputPending = true;
            UpdatePendingWeaponInputRestore();
            cursorReleasedFrame = Time.frameCount;
            Cursor.lockState = previousLock;
            Cursor.visible = previousVisible;
            view.SetVisible(false);
        }
    }

    private void UpdatePendingWeaponInputRestore()
    {
        if (!restoreWeaponInputPending) return;
        if (Mouse.current != null && Mouse.current.leftButton.isPressed) return;
        restoreWeaponInputPending = false;
        SetWeaponInputSuppressed(false);
    }

    private void SetWeaponInputSuppressed(bool suppressed)
    {
        if (!suppressed)
        {
            for (int i = 0; i < suppressedWeaponActions.Count; i++)
                if (suppressedWeaponActions[i] != null) suppressedWeaponActions[i].Enable();
            suppressedWeaponActions.Clear();
            return;
        }
        foreach (PlayerInput input in FindObjectsByType<PlayerInput>())
        {
            if (input.actions == null) continue;
            foreach (InputAction action in input.actions)
            {
                string name = action.name.ToLowerInvariant();
                if (!name.Contains("attack") && !name.Contains("fire") && !name.Contains("shoot")
                    && !name.Contains("aim") && !name.Contains("reload") && !name.Contains("look")) continue;
                if (!action.enabled) continue;
                action.Disable();
                suppressedWeaponActions.Add(action);
            }
        }
    }

    internal void SelectSlot(int slot)
    {
        if (view != null && view.UpgradeDialogOpen) return;
        TrapPlacementController target = GetPlacement();
        if (target == null) return;
        target.SelectTrapSlot(slot);
        RefreshPresentation();
    }

    internal void SelectFilter(int filter)
    {
        if (view != null && view.UpgradeDialogOpen) return;
        view?.ResetHoveredTrap();
        mountFilter = Mathf.Clamp(filter, 0, MountFilters.Length - 1);
        currentPage = 0;
        RebuildFiltered();
        RefreshPresentation();
    }

    internal void ChangePage(int delta)
    {
        if (view != null && view.UpgradeDialogOpen) return;
        view?.ResetHoveredTrap();
        int pages = Mathf.Max(1, Mathf.CeilToInt(filtered.Count / 54f));
        currentPage = Mathf.Clamp(currentPage + delta, 0, pages - 1);
        RefreshPresentation();
    }

    internal void ClampPage(int pageCount) => currentPage = Mathf.Clamp(currentPage, 0, Mathf.Max(0, pageCount - 1));

    internal void AssignTrap(TrapDefinition trap)
    {
        if (view != null && view.UpgradeDialogOpen) return;
        TrapPlacementController target = GetPlacement();
        if (target == null || trap == null) return;
        int slot = target.SelectedSlot;
        if (!target.AssignTrapToSlot(slot, trap)) return;
        target.SelectTrapSlot(slot);
        SetOpen(false);
    }

    internal void ClearSelectedSlot()
    {
        if (view != null && view.UpgradeDialogOpen) return;
        TrapPlacementController target = GetPlacement();
        if (target == null) return;
        target.AssignTrapToSlot(target.SelectedSlot, null);
        SetOpen(false);
    }

    internal void CloseMenu() => SetOpen(false);
    internal bool CanAfford(TrapDefinition trap) => economy.CanAfford(trap);
    internal string StatusFor(TrapDefinition trap)
    {
        if (trap.Cost < 0) return "价格无效";
        if (!economy.IsReady) return "金币未就绪";
        return economy.CanAfford(trap) ? string.Empty : "金币不足";
    }
    internal string DetailHintFor(TrapDefinition trap)
    {
        string hint = economy.PurchaseHint(trap);
        return string.IsNullOrEmpty(hint) ? "已就绪 · 选择不扣款，放置时支付" : hint;
    }

    private void RefreshPresentation() => view?.Refresh();

    private TrapPlacementController GetPlacement()
    {
        if (placement == null) placement = FindAnyObjectByType<TrapPlacementController>();
        return placement;
    }
}
