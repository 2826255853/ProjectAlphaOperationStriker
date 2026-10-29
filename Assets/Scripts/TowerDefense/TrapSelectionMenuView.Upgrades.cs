using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed partial class TrapSelectionMenuView
{
    [SerializeField] private Button selectionTab;
    [SerializeField] private Button upgradeTab;
    [SerializeField] private GameObject upgradeModal;
    [SerializeField] private TextMeshProUGUI upgradeTitle;
    [SerializeField] private TextMeshProUGUI upgradeBody;
    [SerializeField] private TextMeshProUGUI upgradeBalance;
    [SerializeField] private TextMeshProUGUI upgradeFeedback;
    [SerializeField] private Button confirmUpgradeButton;
    [SerializeField] private TextMeshProUGUI confirmUpgradeLabel;
    [SerializeField] private Button cancelUpgradeButton;
    [SerializeField] private TextMeshProUGUI cancelUpgradeLabel;
    [SerializeField] private CanvasGroup upgradeBackgroundGroup;
    [SerializeField] private GameObject directionOptionsPanel;
    private RectTransform directionOptionsContent;
    private readonly List<DirectionOptionControl> directionOptionControls = new List<DirectionOptionControl>();
    private TrapUpgradeService upgradeService;
    private TrapUpgradeMenuSession upgradeSession;
    private bool upgradePage;
    public bool UpgradeDialogOpen => upgradeSession != null && upgradeSession.IsOpen;

    private sealed class DirectionOptionControl
    {
        public Button Button;
        public TextMeshProUGUI Label;
    }

    // Called by the incremental editor migration as well as old-prefab runtime compatibility.
    public void EnsureUpgradeUI()
    {
        Transform panel = filterButtons.Length > 0 && filterButtons[0] != null ? filterButtons[0].transform.parent : transform;
        if (upgradeBackgroundGroup == null && panel != transform)
        {
            upgradeBackgroundGroup = panel.GetComponent<CanvasGroup>();
            if (upgradeBackgroundGroup == null) upgradeBackgroundGroup = panel.gameObject.AddComponent<CanvasGroup>();
        }
        if (selectionTab == null)
            selectionTab = TrapUpgradeMenuUI.Button(panel, "Selection Tab", instructionLabel,
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(452, -202), new Vector2(116, 40), "选择", out _);
        if (upgradeTab == null)
            upgradeTab = TrapUpgradeMenuUI.Button(panel, "Upgrade Tab", instructionLabel,
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(578, -202), new Vector2(116, 40), "升级", out _);
        foreach (var card in GetComponentsInChildren<TrapSelectionMenuCardView>(true)) card.EnsureUpgradeUI();
        if (upgradeModal == null)
        {
            RectTransform overlay = TrapUpgradeMenuUI.Rect(transform, "Upgrade Modal", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            overlay.anchorMax = Vector2.one;
            overlay.offsetMin = overlay.offsetMax = Vector2.zero;
            overlay.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, .78f);
            upgradeModal = overlay.gameObject;
            RectTransform dialog = TrapUpgradeMenuUI.Rect(overlay, "Confirmation", new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(700, 500));
            dialog.gameObject.AddComponent<Image>().color = TrapUpgradeMenuUI.Background;
            upgradeTitle = TrapUpgradeMenuUI.Label(dialog, "Title", detailName, new Vector2(.5f, 1), new Vector2(0, -42), new Vector2(636, 48), "升级确认", 27);
            upgradeBody = TrapUpgradeMenuUI.Label(dialog, "Quote and Attributes", detailName, new Vector2(.5f, 1), new Vector2(0, -184), new Vector2(636, 222), "", 21);
            upgradeBalance = TrapUpgradeMenuUI.Label(dialog, "Balance", balanceLabel, new Vector2(.5f, 0), new Vector2(0, 162), new Vector2(636, 32), "", 21);
            upgradeFeedback = TrapUpgradeMenuUI.Label(dialog, "Result", instructionLabel, new Vector2(.5f, 0), new Vector2(0, 112), new Vector2(636, 60), "", 18);
            confirmUpgradeButton = TrapUpgradeMenuUI.Button(dialog, "Confirm Upgrade", instructionLabel, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-32, 28), new Vector2(290, 48), "确认升级", out confirmUpgradeLabel);
            cancelUpgradeButton = TrapUpgradeMenuUI.Button(dialog, "Cancel Upgrade", instructionLabel, new Vector2(0, 0), new Vector2(0, 0), new Vector2(32, 28), new Vector2(290, 48), "取消", out cancelUpgradeLabel);
            upgradeModal.SetActive(false);
        }
        EnsureDirectionOptionsUI();
    }

    private void EnsureDirectionOptionsUI()
    {
        Transform dialog = upgradeBody != null ? upgradeBody.transform.parent : upgradeModal.transform.Find("Confirmation");
        if (dialog == null) return;
        if (directionOptionsPanel == null)
        {
            RectTransform viewport = TrapUpgradeMenuUI.Rect(dialog, "Direction Options", new Vector2(.5f, .5f),
                new Vector2(.5f, .5f), new Vector2(0, -6), new Vector2(636, 350));
            viewport.gameObject.AddComponent<RectMask2D>();
            directionOptionsPanel = viewport.gameObject;
        }
        if (directionOptionsContent == null)
            directionOptionsContent = directionOptionsPanel.transform.Find("Content") as RectTransform;
        if (directionOptionsContent == null)
            directionOptionsContent = TrapUpgradeMenuUI.Rect(directionOptionsPanel.transform, "Content",
                new Vector2(0, 1), new Vector2(1, 1), Vector2.zero, Vector2.zero);
        directionOptionsContent.anchorMin = new Vector2(0, 1);
        directionOptionsContent.anchorMax = new Vector2(1, 1);
        directionOptionsContent.pivot = new Vector2(0, 1);
        directionOptionsContent.offsetMin = Vector2.zero;
        directionOptionsContent.offsetMax = Vector2.zero;
        ScrollRect scroll = directionOptionsPanel.GetComponent<ScrollRect>();
        if (scroll == null) scroll = directionOptionsPanel.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 28;
        scroll.viewport = directionOptionsPanel.transform as RectTransform;
        scroll.content = directionOptionsContent;
    }

    private void InitializeUpgrades()
    {
        EnsureUpgradeUI();
        selectionTab.onClick.AddListener(() => SetUpgradePage(false));
        upgradeTab.onClick.AddListener(() => SetUpgradePage(true));
        confirmUpgradeButton.onClick.AddListener(ConfirmUpgrade);
        cancelUpgradeButton.onClick.AddListener(() => BackFromUpgrade());
        BindUpgradeService();
    }

    public void SetUpgradePage(bool value)
    {
        if (UpgradeDialogOpen) return;
        upgradePage = value;
        Refresh();
    }

    private void BindUpgradeService()
    {
        TrapUpgradeService current = TrapUpgradeRuntime.GetOrCreate();
        if (ReferenceEquals(current, upgradeService)) return;
        UnbindUpgradeService();
        upgradeService = current;
        upgradeSession = new TrapUpgradeMenuSession(current);
        current.Upgraded += OnTrapUpgraded;
        current.RunReset += OnUpgradeRunReset;
    }

    private void UnbindUpgradeService()
    {
        if (upgradeService == null) return;
        upgradeService.Upgraded -= OnTrapUpgraded;
        upgradeService.RunReset -= OnUpgradeRunReset;
        upgradeService = null;
    }

    private void OnTrapUpgraded(string id, TrapUpgradeState state) => Refresh();
    private void OnUpgradeRunReset(Guid runId) { CancelUpgrade(); Refresh(); }

    public void OpenUpgrade(TrapDefinition trap)
    {
        if (UpgradeDialogOpen || trap == null) return;
        BindUpgradeService();
        upgradeSession.Open(trap);
        RefreshUpgradeDialog();
        EventSystem.current?.SetSelectedGameObject(cancelUpgradeButton.gameObject);
    }

    public void ConfirmUpgrade()
    {
        if (!UpgradeDialogOpen) return;
        if (upgradeSession.Step == TrapUpgradeMenuSession.DialogStep.Direction) return;
        TrapUpgradeQuote quote = upgradeSession.Quote;
        upgradeSession.Confirm(quote.TrapId, quote.Current.Level, quote.Version);
        Refresh();
    }

    private void ChooseNormalDirection()
    {
        if (upgradeSession == null || upgradeSession.Step != TrapUpgradeMenuSession.DialogStep.Direction) return;
        upgradeSession.ChooseNormal();
        RefreshUpgradeDialog();
        FocusUpgradeDialog();
    }

    private void ChooseBranchDirection(string branchId)
    {
        if (upgradeSession == null || upgradeSession.Step != TrapUpgradeMenuSession.DialogStep.Direction) return;
        upgradeSession.SelectBranch(branchId);
        RefreshUpgradeDialog();
        FocusUpgradeDialog();
    }

    public bool BackFromUpgrade()
    {
        if (!UpgradeDialogOpen) return false;
        upgradeSession.Back();
        RefreshUpgradeDialog();
        FocusUpgradeDialog();
        return true;
    }

    private void FocusUpgradeDialog()
    {
        EventSystem eventSystem = EventSystem.current;
        if (eventSystem == null || upgradeSession == null) return;
        if (upgradeSession.Step == TrapUpgradeMenuSession.DialogStep.Direction && directionOptionControls.Count > 0)
        {
            eventSystem.SetSelectedGameObject(directionOptionControls[0].Button.gameObject);
            return;
        }
        Button target = confirmUpgradeButton != null && confirmUpgradeButton.gameObject.activeInHierarchy &&
            confirmUpgradeButton.interactable ? confirmUpgradeButton : cancelUpgradeButton;
        if (target != null) eventSystem.SetSelectedGameObject(target.gameObject);
    }

    private void CancelUpgrade()
    {
        upgradeSession?.Cancel();
        RefreshUpgradeDialog();
    }

    private void RefreshUpgradeDialog()
    {
        if (upgradeModal == null) return;
        bool visible = UpgradeDialogOpen;
        upgradeModal.SetActive(visible);
        if (upgradeBackgroundGroup != null)
        { upgradeBackgroundGroup.interactable = !visible; upgradeBackgroundGroup.blocksRaycasts = !visible; }
        if (selectionTab != null) selectionTab.interactable = upgradePage;
        if (upgradeTab != null) upgradeTab.interactable = upgradePage == false;
        if (!visible) return;
        TrapUpgradeQuote quote = upgradeSession.Quote;
        var step = upgradeSession.Step;
        bool directions = step == TrapUpgradeMenuSession.DialogStep.Direction;
        bool branch = step == TrapUpgradeMenuSession.DialogStep.Branch;
        bool complete = step == TrapUpgradeMenuSession.DialogStep.Result;
        string branchName = branch && upgradeSession.SelectedBranch != null ? upgradeSession.SelectedBranch.displayName : string.Empty;
        upgradeTitle.text = $"{(upgradeSession.Definition != null ? upgradeSession.Definition.DisplayName : "陷阱")} · " +
            (directions ? "四级方向" : branch ? branchName : "升级确认");
        upgradeBody.gameObject.SetActive(!directions);
        upgradeBalance.gameObject.SetActive(!directions);
        upgradeFeedback.gameObject.SetActive(!directions);
        directionOptionsPanel.SetActive(directions);
        if (directions) RefreshDirectionOptions();
        else if (branch)
        {
            TrapUpgradeCatalog.Branch selected = upgradeSession.SelectedBranch;
            upgradeBody.text = $"{selected.displayName}\n{selected.description}\n当前等级 {quote.Current.Level} → 目标等级 {quote.Target.Level} · 分支\n" +
                $"升级费用：{quote.Cost} 金币\n可用状态：{(upgradeSession.SelectedBranchAvailable ? "已接入，可升级" : upgradeSession.SelectedBranchUnavailableReason)}\n" +
                TrapUpgradeMenuSession.AttributePreview(upgradeSession.Definition, quote);
        }
        else
            upgradeBody.text = $"当前等级 {quote.Current.Level} → 目标等级 {quote.Target.Level}" +
                (quote.Target.Level == 4 ? (quote.Target.Direction == TrapUpgradeDirection.Branch ? " · 分支" : " · 普通") : "") +
                $"\n升级费用：{quote.Cost} 金币\n" + TrapUpgradeMenuSession.AttributePreview(upgradeSession.Definition, quote);
        upgradeBalance.text = owner != null ? owner.BalanceText : "金币：--";
        EconomyManager wallet = EconomyManager.Instance;
        bool affordable = wallet != null && wallet.CanAfford(quote.Cost);
        upgradeFeedback.text = upgradeSession.Feedback;
        if (branch && !upgradeSession.SelectedBranchAvailable) upgradeFeedback.text = upgradeSession.SelectedBranchUnavailableReason;
        if (!complete && !affordable && string.IsNullOrEmpty(upgradeFeedback.text)) upgradeFeedback.text = "金币不足，确认不会扣款。";
        confirmUpgradeButton.gameObject.SetActive(!directions);
        confirmUpgradeButton.interactable = !directions && !complete && quote.CanUpgrade && (!branch || upgradeSession.SelectedBranchAvailable);
        confirmUpgradeLabel.text = branch ? "确认分支升级" : step == TrapUpgradeMenuSession.DialogStep.ConfirmNormal ? "确认四级升级" : "确认升级";
        cancelUpgradeLabel.text = complete ? "返回列表" : directions || branch || step == TrapUpgradeMenuSession.DialogStep.ConfirmNormal ? "返回" : "取消";
    }

    private void RefreshDirectionOptions()
    {
        if (directionOptionsContent == null || upgradeSession == null) return;
        IReadOnlyList<TrapUpgradeCatalog.Branch> branches = upgradeSession.Branches;
        int count = 1 + branches.Count;
        const float rowHeight = 150f;
        while (directionOptionControls.Count < count)
        {
            Button button = TrapUpgradeMenuUI.Button(directionOptionsContent, "Direction Option", detailName,
                new Vector2(.5f, 1), new Vector2(.5f, 1), Vector2.zero, new Vector2(612, rowHeight), "", out TextMeshProUGUI label);
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.fontSize = 14;
            directionOptionControls.Add(new DirectionOptionControl { Button = button, Label = label });
        }
        directionOptionsContent.sizeDelta = new Vector2(0, count * rowHeight + 8);
        TrapUpgradeQuote normalQuote = upgradeSession.QuoteForNormalDirection();
        ConfigureDirectionOption(0, rowHeight, "普通四级", "沿用普通四级成长倍率。", normalQuote,
            normalQuote != null && normalQuote.CanUpgrade ? "可用" : normalQuote?.Message ?? "报价不可用", ChooseNormalDirection);
        for (int i = 0; i < branches.Count; i++)
        {
            TrapUpgradeCatalog.Branch item = branches[i];
            TrapUpgradeQuote branchQuote = upgradeSession.QuoteForBranch(item.branchId);
            string status = branchQuote != null && branchQuote.CanUpgrade ? "已接入，可选择" : branchQuote?.Message ?? "尚未接入";
            ConfigureDirectionOption(i + 1, rowHeight, item.displayName, item.description, branchQuote,
                status, () => ChooseBranchDirection(item.branchId));
        }
        for (int i = count; i < directionOptionControls.Count; i++) directionOptionControls[i].Button.gameObject.SetActive(false);
    }

    private void ConfigureDirectionOption(int index, float rowHeight, string title, string description,
        TrapUpgradeQuote quote, string status, UnityEngine.Events.UnityAction onClick)
    {
        DirectionOptionControl option = directionOptionControls[index];
        option.Button.gameObject.SetActive(true);
        RectTransform rect = option.Button.transform as RectTransform;
        rect.anchoredPosition = new Vector2(0, -rowHeight * index - 4);
        rect.sizeDelta = new Vector2(612, rowHeight - 8);
        option.Label.rectTransform.sizeDelta = new Vector2(604, rowHeight - 8);
        string attributes = quote != null ? TrapUpgradeMenuSession.AttributePreview(upgradeSession.Definition, quote) : "属性预览不可用";
        option.Label.text = $"{title} · {status}\n{description}\n费用：{quote?.Cost.ToString() ?? "--"} 金币\n{attributes}";
        option.Button.onClick.RemoveAllListeners();
        option.Button.onClick.AddListener(onClick);
    }

    public void SetUpgradePreview(System.Collections.Generic.IReadOnlyList<TrapDefinition> definitions,
        TrapUpgradeService service, TrapDefinition detail, string balance)
    {
        EnsureUpgradeUI();
        SetPreview(definitions, -1, detail, balance, null);
        upgradeSession = new TrapUpgradeMenuSession(service);
        upgradeSession.Open(detail);
        for (int i = 0; i < cards.Length; i++)
            cards[i]?.SetUpgradeState(i < definitions.Count, i < definitions.Count ? service.GetState(definitions[i].TrapId).Level : 1);
        RefreshUpgradeDialog();
        upgradeBalance.text = balance;
        upgradeFeedback.text = "确认后影响本局所有同类陷阱 · 战斗继续运行";
    }

    public void SetUpgradeDirectionPreview(System.Collections.Generic.IReadOnlyList<TrapDefinition> definitions,
        TrapUpgradeService service, TrapDefinition detail, string balance)
    {
        SetUpgradePreview(definitions, service, detail, balance);
        if (upgradeSession.Quote == null || upgradeSession.Quote.Current.Level != 3) return;
        TrapUpgradeQuote quote = upgradeSession.Quote;
        upgradeSession.Confirm(quote.TrapId, quote.Current.Level, quote.Version);
        RefreshUpgradeDialog();
    }
}
