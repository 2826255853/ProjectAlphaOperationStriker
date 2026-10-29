using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>Serialized Canvas view for TrapSelectionMenu; layout is authored in its prefab.</summary>
public sealed partial class TrapSelectionMenuView : MonoBehaviour
{
    private const int PageCapacity = 54;
    [SerializeField] private TrapSelectionMenuCardView[] cards = Array.Empty<TrapSelectionMenuCardView>();
    [SerializeField] private Button[] slotButtons = Array.Empty<Button>();
    [SerializeField] private TextMeshProUGUI[] slotLabels = Array.Empty<TextMeshProUGUI>();
    [SerializeField] private Image[] slotHighlights = Array.Empty<Image>();
    [SerializeField] private Button[] filterButtons = Array.Empty<Button>();
    [SerializeField] private TextMeshProUGUI[] filterLabels = Array.Empty<TextMeshProUGUI>();
    [SerializeField] private TextMeshProUGUI balanceLabel;
    [SerializeField] private TextMeshProUGUI instructionLabel;
    [SerializeField] private Image detailIcon;
    [SerializeField] private TextMeshProUGUI detailFallbackIcon;
    [SerializeField] private TextMeshProUGUI detailName;
    [SerializeField] private TextMeshProUGUI detailMount;
    [SerializeField] private TextMeshProUGUI detailFootprint;
    [SerializeField] private TextMeshProUGUI detailCost;
    [SerializeField] private TextMeshProUGUI purchaseHint;
    [SerializeField] private Button clearButton;
    [SerializeField] private Button closeButton;
    [SerializeField] private Button previousPageButton;
    [SerializeField] private Button nextPageButton;
    [SerializeField] private TextMeshProUGUI pageLabel;
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private Color selectedFilterColor = new Color(.22f, .92f, 1f);
    [SerializeField] private Color idleFilterColor = new Color(.72f, .82f, .87f);
    private TrapSelectionMenu owner;
    private TrapDefinition hovered;
    private string previewHint;
    private bool initialized;

    public void Initialize(TrapSelectionMenu menu)
    {
        owner = menu;
        if (initialized) return;
        initialized = true;
        if (cards == null || cards.Length != PageCapacity)
            cards = GetComponentsInChildren<TrapSelectionMenuCardView>(true);
        for (int i = 0; i < cards.Length; i++)
            if (cards[i] != null)
            {
                cards[i].Configure(HandleTrapClick, HandleHover);
                cards[i].ConfigureUpgrade(OpenUpgrade);
            }
        InitializeUpgrades();
        for (int i = 0; i < slotButtons.Length; i++)
        {
            int slot = i;
            slotButtons[i].onClick.AddListener(() => owner.SelectSlot(slot));
        }
        for (int i = 0; i < filterButtons.Length; i++)
        {
            int filter = i;
            filterButtons[i].onClick.AddListener(() => owner.SelectFilter(filter));
        }
        if (clearButton != null) clearButton.onClick.AddListener(owner.ClearSelectedSlot);
        if (closeButton != null) closeButton.onClick.AddListener(owner.CloseMenu);
        if (previousPageButton != null) previousPageButton.onClick.AddListener(() => owner.ChangePage(-1));
        if (nextPageButton != null) nextPageButton.onClick.AddListener(() => owner.ChangePage(1));
        EnsureEventSystem();
        Refresh();
    }

    public void SetVisible(bool visible)
    {
        if (!visible) CancelUpgrade();
        if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup == null) return;
        canvasGroup.alpha = visible ? 1f : 0f;
        canvasGroup.interactable = visible;
        canvasGroup.blocksRaycasts = visible;
    }

    public void Refresh()
    {
        if (owner == null) return;
        BindUpgradeService();
        IReadOnlyList<TrapDefinition> items = owner.VisibleTraps;
        int pageCount = Mathf.Max(1, Mathf.CeilToInt(items.Count / (float)PageCapacity));
        owner.ClampPage(pageCount);
        int start = owner.CurrentPage * PageCapacity;
        TrapPlacementController placement = owner.Placement;
        int selectedSlot = placement != null ? placement.SelectedSlot : 0;
        TrapDefinition current = placement != null ? placement.GetTrapSlot(selectedSlot) : null;
        for (int i = 0; i < cards.Length; i++)
        {
            TrapDefinition trap = start + i < items.Count ? items[start + i] : null;
            bool affordable = trap == null || owner.CanAfford(trap);
            string status = trap == null ? string.Empty : owner.StatusFor(trap);
            cards[i]?.SetCard(trap, trap == current, affordable, status);
            int level = trap != null && TrapUpgradeCatalog.IsValidId(trap.TrapId)
                ? upgradeService.GetState(trap.TrapId).Level : 1;
            cards[i]?.SetUpgradeState(upgradePage, level);
        }
        for (int i = 0; i < slotLabels.Length; i++)
        {
            if (i < slotButtons.Length && slotButtons[i] != null) slotButtons[i].interactable = placement != null;
            TrapDefinition trap = placement != null ? placement.GetTrapSlot(i) : null;
            if (slotLabels[i] != null) slotLabels[i].text = $"{i + 4}  ·  {(trap != null ? trap.DisplayName : "空槽")}";
            if (i < slotHighlights.Length && slotHighlights[i] != null)
                slotHighlights[i].enabled = i == selectedSlot;
        }
        if (balanceLabel != null) balanceLabel.text = owner.BalanceText;
        if (instructionLabel != null)
            instructionLabel.text = placement == null
                ? "未检测到陷阱放置控制器，当前无法分配"
                : upgradePage ? "点击卡片右下角 ↑ 查看升级 · 升级影响本局同类陷阱 · 战斗继续运行"
                : "选择目标槽位，再点选陷阱分配 · 选择陷阱不扣款，放置时支付 · N / Esc 关闭";
        for (int i = 0; i < filterLabels.Length; i++)
            if (filterLabels[i] != null) filterLabels[i].color = owner.FilterIndex == i
                ? selectedFilterColor : idleFilterColor;
        if (pageLabel != null) pageLabel.text = $"{owner.CurrentPage + 1} / {pageCount}";
        if (previousPageButton != null) previousPageButton.interactable = owner.CurrentPage > 0;
        if (nextPageButton != null) nextPageButton.interactable = owner.CurrentPage + 1 < pageCount;
        if (clearButton != null) clearButton.interactable = placement != null;
        ShowDetails(hovered != null ? hovered : current);
        RefreshUpgradeDialog();
    }

    public void ResetHoveredTrap() => hovered = null;

    public void SetPreview(IReadOnlyList<TrapDefinition> traps, int selectedIndex, TrapDefinition detail, string balance, string hint)
    {
        previewHint = hint;
        for (int i = 0; i < cards.Length; i++)
        {
            TrapDefinition trap = i < traps.Count ? traps[i] : null;
            cards[i]?.SetCard(trap, trap != null && i == selectedIndex, hint == null || i != selectedIndex, hint != null && i == selectedIndex ? "金币不足" : string.Empty);
        }
        if (balanceLabel != null) balanceLabel.text = balance;
        if (instructionLabel != null) instructionLabel.text = "选择目标槽位，再点选陷阱分配 · 选择陷阱不扣款，放置时支付 · N / Esc 关闭";
        for (int i = 0; i < slotLabels.Length; i++)
        {
            if (slotLabels[i] != null) slotLabels[i].text = $"{i + 4}  ·  {(i == 0 && selectedIndex >= 0 && selectedIndex < traps.Count ? traps[selectedIndex].DisplayName : "空槽")}";
            if (i < slotHighlights.Length && slotHighlights[i] != null) slotHighlights[i].enabled = i == 0;
        }
        ShowDetails(detail);
    }

    private void HandleTrapClick(TrapDefinition trap)
    {
        if (UpgradeDialogOpen) return;
        if (upgradePage) HandleHover(trap);
        else owner?.AssignTrap(trap);
    }

    private void HandleHover(TrapDefinition trap)
    {
        hovered = trap;
        ShowDetails(hovered != null ? hovered : owner?.SelectedTrap);
    }

    private void ShowDetails(TrapDefinition trap)
    {
        if (detailIcon != null)
        {
            detailIcon.sprite = trap != null ? trap.MenuIcon : null;
            detailIcon.enabled = trap != null && trap.MenuIcon != null;
        }
        if (detailFallbackIcon != null)
        {
            detailFallbackIcon.text = trap == null ? "—" : trap.MenuIcon != null ? string.Empty
                : trap.MountType == TrapMountType.Ground ? "GROUND" : trap.MountType == TrapMountType.Wall ? "WALL" : "CEILING";
            detailFallbackIcon.enabled = trap == null || trap.MenuIcon == null;
        }
        if (detailName != null) detailName.text = trap != null ? trap.DisplayName : "选择一个防御设施";
        if (detailMount != null) detailMount.text = trap != null ? trap.MountType.DisplayName() : "将鼠标移到卡片上查看详情";
        if (detailFootprint != null) detailFootprint.text = trap != null
            ? $"{(trap.MountType == TrapMountType.Ground ? "占地" : "占格")}  {trap.Footprint.x} × {trap.Footprint.y}" : string.Empty;
        if (detailCost != null) detailCost.text = trap != null ? $"部署费用  {trap.Cost} 金币" : string.Empty;
        if (purchaseHint != null) purchaseHint.text = trap == null ? string.Empty
            : owner != null ? owner.DetailHintFor(trap) : previewHint ?? "已就绪 · 选择不扣款，放置时支付";
    }

    private void OnDestroy()
    {
        UnbindUpgradeService();
        if (!initialized) return;
        for (int i = 0; i < cards.Length; i++) if (cards[i] != null)
        { cards[i].Configure(null, null); cards[i].ConfigureUpgrade(null); }
    }

    private static void EnsureEventSystem()
    {
        EventSystem system = FindAnyObjectByType<EventSystem>();
        if (system == null) system = new GameObject("EventSystem", typeof(EventSystem)).GetComponent<EventSystem>();
        StandaloneInputModule legacy = system.GetComponent<StandaloneInputModule>();
        if (legacy != null)
        {
            legacy.enabled = false;
            Destroy(legacy);
        }
        if (system.GetComponent<InputSystemUIInputModule>() == null)
            system.gameObject.AddComponent<InputSystemUIInputModule>();
        if (Application.isPlaying) DontDestroyOnLoad(system.gameObject);
    }
}
