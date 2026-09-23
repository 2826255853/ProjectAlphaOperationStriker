using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Runtime trap picker. Press N to assign available traps to the four hotbar slots.
/// Traps are listed in a fixed 6 rows x 9 columns grid of square buttons.</summary>
public sealed class TrapSelectionMenu : MonoBehaviour
{
    [SerializeField] private KeyCode toggleKey = KeyCode.N;
    [SerializeField] private string resourcesFolder = "";
    private readonly List<TrapDefinition> available = new List<TrapDefinition>();
    private TrapPlacementController placement;
    private bool open;
    private CursorLockMode previousLock;
    private bool previousVisible;
    private readonly List<InputAction> suppressedWeaponActions = new List<InputAction>();
    private bool restoreWeaponInputPending;
    private static int cursorReleasedFrame = -1;
    private readonly EconomyUIState economy = new EconomyUIState();
    public string BalanceText => economy.BalanceText;

    public static bool IsOpen { get; private set; }

    /// <summary>True while the menu owns the cursor. It also stays true for the
    /// frame the menu closes, so the player controller cannot re-lock the view
    /// with the very Escape press that closed this menu.</summary>
    public static bool CursorOwned => IsOpen || cursorReleasedFrame == Time.frameCount;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureMenu()
    {
        if (FindAnyObjectByType<TrapSelectionMenu>() != null) return;
        var host = new GameObject("Trap Selection Menu");
        DontDestroyOnLoad(host);
        host.AddComponent<TrapSelectionMenu>();
    }

    private void Awake() { Refresh(); }
    private void OnEnable() => economy.Enable();
    private void OnDestroy() => economy.Dispose();

    private void Update()
    {
        economy.Refresh();
        UpdatePendingWeaponInputRestore();
        if (Keyboard.current == null) return;
        if (toggleKey == KeyCode.N && Keyboard.current.nKey.wasPressedThisFrame) SetOpen(!open);
        if (open && Keyboard.current.escapeKey.wasPressedThisFrame) SetOpen(false);
    }

    private void Refresh()
    {
        available.Clear();
        foreach (var definition in Resources.LoadAll<TrapDefinition>(resourcesFolder))
            if (definition != null && !available.Contains(definition)) available.Add(definition);
        available.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, System.StringComparison.Ordinal));
        styledCellSize = -1f;
    }

    private void SetOpen(bool value)
    {
        if (open == value) return;
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
        }
        else
        {
            // A click that picks a trap is still held down on this frame. Re-enabling
            // the weapon actions right now makes the input system treat that held
            // click as a fresh trigger, so the player fires the instant the menu
            // closes. Defer the restore until the button is actually released.
            restoreWeaponInputPending = true;
            UpdatePendingWeaponInputRestore();
            cursorReleasedFrame = Time.frameCount;
            Cursor.lockState = previousLock;
            Cursor.visible = previousVisible;
        }
    }

    /// <summary>Re-enables the weapon actions once the pick-up click has been released.</summary>
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
                if (!name.Contains("attack") && !name.Contains("fire") &&
                    !name.Contains("shoot") && !name.Contains("aim") &&
                    !name.Contains("reload") && !name.Contains("look")) continue;
                if (action.enabled)
                {
                    action.Disable();
                    suppressedWeaponActions.Add(action);
                }
            }
        }
    }

    private void OnDisable()
    {
        economy.Dispose();
        if (open) SetOpen(false);
        restoreWeaponInputPending = false;
        SetWeaponInputSuppressed(false);
        IsOpen = false;
    }

    private const int GridColumns = 9;
    private const int GridRows = 6;

    private GUIStyle cellStyle;
    private GUIStyle emptyCellStyle;
    private float styledCellSize = -1f;

    private void EnsureCellStyles(float cellSize)
    {
        if (cellStyle != null && Mathf.Approximately(styledCellSize, cellSize)) return;
        styledCellSize = cellSize;

        // Trap names can be long, so the font is shrunk until the wrapped name
        // plus the cost line is guaranteed to fit inside the square cell. The
        // 4px padding on every side is subtracted before measuring.
        const float cellPadding = 4f;
        float textWidth = Mathf.Max(1f, cellSize - cellPadding * 2f);
        float textHeight = Mathf.Max(1f, cellSize - cellPadding * 2f);
        int longestName = 1;
        for (int i = 0; i < available.Count; i++)
            if (available[i] != null) longestName = Mathf.Max(longestName, available[i].DisplayName.Length);

        int fontSize = Mathf.Clamp(Mathf.RoundToInt(cellSize * 0.18f), 8, 22);
        while (fontSize > 8)
        {
            int charsPerLine = Mathf.Max(1, Mathf.FloorToInt(textWidth / fontSize));
            int nameLines = Mathf.CeilToInt(longestName / (float)charsPerLine);
            float needed = (nameLines + 2) * fontSize * 1.2f;
            if (needed <= textHeight) break;
            fontSize--;
        }
        cellStyle = new GUIStyle(GUI.skin.button)
        {
            wordWrap = true,
            alignment = TextAnchor.MiddleCenter,
            fontSize = fontSize,
            padding = new RectOffset((int)cellPadding, (int)cellPadding, (int)cellPadding, (int)cellPadding)
        };
        emptyCellStyle = new GUIStyle(cellStyle)
        {
            normal = { textColor = new Color(1f, 1f, 1f, 0.18f) }
        };
    }

    private void OnGUI()
    {
        if (!open) return;
        economy.Refresh();
        TrapPlacementController target = GetPlacement();
        int targetSlot = target != null ? target.SelectedSlot : 0;
        TrapDefinition currentSlotTrap = target != null ? target.GetTrapSlot(targetSlot) : null;

        // The trap list is laid out as a fixed 6 rows x 9 columns grid whose
        // cells are always square. The cell size is derived from the screen so
        // the whole panel keeps fitting, then clamped to a readable range.
        const float pad = 24f, gap = 8f, headerHeight = 136f, footerHeight = 112f;
        float cell = Mathf.Floor(Mathf.Min(
            (Screen.width * 0.92f - pad * 2f - gap * (GridColumns - 1)) / GridColumns,
            (Screen.height * 0.9f - headerHeight - footerHeight - gap * (GridRows - 1)) / GridRows));
        cell = Mathf.Clamp(cell, 44f, 150f);

        float gridWidth = GridColumns * cell + gap * (GridColumns - 1);
        float panelWidth = Mathf.Max(gridWidth + pad * 2f, 600f);
        float panelHeight = headerHeight + GridRows * cell + gap * (GridRows - 1) + footerHeight;
        Rect panel = new Rect((Screen.width - panelWidth) * .5f, (Screen.height - panelHeight) * .5f, panelWidth, panelHeight);
        GUI.Box(panel, "陷阱选择");
        GUI.Label(new Rect(panel.x + pad, panel.y + 30f, panelWidth - pad * 2f, 24f), "先选择目标槽位，再点击陷阱分配（N 或 Esc 关闭）");
        GUI.Label(new Rect(panel.x + pad, panel.y + 62f, 80f, 28f), "目标槽位");
        for (int i = 0; i < 4; i++)
        {
            TrapDefinition slotTrap = target != null ? target.GetTrapSlot(i) : null;
            string label = $"{i + 4}: {(slotTrap != null ? slotTrap.DisplayName : "空")}";
            GUI.backgroundColor = i == targetSlot ? new Color(0.55f, 0.9f, 1f) : Color.white;
            if (GUI.Button(new Rect(panel.x + pad + 84f + i * 116f, panel.y + 58f, 110f, 34f), label))
            {
                targetSlot = i;
                currentSlotTrap = slotTrap;
                if (target != null) target.SelectTrapSlot(i);
            }
        }
        GUI.backgroundColor = Color.white;

        GUI.Label(new Rect(panel.x + pad, panel.y + 100f, panelWidth - pad * 2f, 26f),
            BalanceText + "    选择不扣款，确认放置时支付");

        EnsureCellStyles(cell);
        float gridX = panel.x + (panelWidth - gridWidth) * .5f;
        float gridTop = panel.y + headerHeight;
        Vector2 mouse = Event.current != null ? Event.current.mousePosition : new Vector2(-1f, -1f);
        TrapDefinition hoveredTrap = null;
        int cellCount = GridColumns * GridRows;
        for (int i = 0; i < cellCount; i++)
        {
            int col = i % GridColumns;
            int row = i / GridColumns;
            Rect rect = new Rect(gridX + col * (cell + gap), gridTop + row * (cell + gap), cell, cell);
            if (i >= available.Count)
            {
                // Filler cells keep the requested 6x9 shape visible without
                // pretending they are clickable.
                GUI.enabled = false;
                GUI.Button(rect, GUIContent.none, emptyCellStyle);
                GUI.enabled = true;
                continue;
            }

            TrapDefinition trap = available[i];
            bool isCurrent = currentSlotTrap == trap;
            bool affordable = economy.CanAfford(trap);
            if (rect.Contains(mouse)) hoveredTrap = trap;
            GUI.backgroundColor = !affordable ? new Color(1f, 0.58f, 0.52f)
                : isCurrent ? new Color(0.55f, 0.9f, 1f) : Color.white;
            string mountLabel = trap.MountType == TrapMountType.Wall ? "墙面" : "地面";
            string status = affordable ? string.Empty : trap.Cost < 0 ? "价格无效"
                : economy.IsReady ? "金币不足" : "金币暂不可用";
            // Unaffordable traps remain selectable for inspection and later placement.
            if (GUI.Button(rect, trap.DisplayName + "\n" + mountLabel + "  花费 " + trap.Cost + "\n" + status, cellStyle))
            {
                if (target != null)
                {
                    target.AssignTrapToSlot(targetSlot, trap);
                    target.SelectTrapSlot(targetSlot);
                }
                GUI.backgroundColor = Color.white;
                SetOpen(false);
                return;
            }
        }
        GUI.backgroundColor = Color.white;

        // Cell labels are short, so the full name and footprint of the hovered
        // trap (or of the selected slot) are spelled out here.
        TrapDefinition described = hoveredTrap != null ? hoveredTrap : currentSlotTrap;
        string description = described != null
            ? $"{described.DisplayName}   {(described.MountType == TrapMountType.Wall ? "墙面" : "地面")}   花费 {described.Cost}   {(described.MountType == TrapMountType.Wall ? "占格" : "占地")} {described.Footprint.x}x{described.Footprint.y}"
            : "将鼠标移到陷阱上查看详情";
        GUI.Label(new Rect(panel.x + pad, panel.y + panelHeight - footerHeight + 8f, panelWidth - pad * 2f, 24f), description);
        GUI.Label(new Rect(panel.x + pad, panel.y + panelHeight - footerHeight + 32f, panelWidth - pad * 2f, 24f),
            economy.PurchaseHint(described));

        if (GUI.Button(new Rect(panel.x + pad, panel.y + panelHeight - footerHeight + 60f, panelWidth - pad * 2f, 38f), "取消已选陷阱"))
        {
            if (target != null) target.AssignTrapToSlot(targetSlot, null);
            SetOpen(false);
        }
    }

    private TrapPlacementController GetPlacement()
    {
        if (placement == null) placement = FindAnyObjectByType<TrapPlacementController>();
        return placement;
    }
}
