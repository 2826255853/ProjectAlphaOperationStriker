using System;
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
    private TrapUpgradeService upgradeService;
    private TrapUpgradeMenuSession upgradeSession;
    private bool upgradePage;
    public bool UpgradeDialogOpen => upgradeSession != null && upgradeSession.IsOpen;

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
        if (upgradeModal != null) return;
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
        if (upgradeSession.Step == TrapUpgradeMenuSession.DialogStep.Direction) upgradeSession.ChooseNormal();
        else
        {
            TrapUpgradeQuote quote = upgradeSession.Quote;
            upgradeSession.Confirm(quote.TrapId, quote.Current.Level, quote.Version);
        }
        Refresh();
    }

    public bool BackFromUpgrade()
    {
        if (!UpgradeDialogOpen) return false;
        upgradeSession.Back();
        RefreshUpgradeDialog();
        return true;
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
        bool complete = step == TrapUpgradeMenuSession.DialogStep.Result;
        upgradeTitle.text = $"{(upgradeSession.Definition != null ? upgradeSession.Definition.DisplayName : "陷阱")} · {(directions ? "四级方向" : "升级确认")}";
        upgradeBody.text = $"当前等级 {quote.Current.Level} → 目标等级 {quote.Target.Level}" +
            (quote.Target.Level == 4 ? " · 普通" : "") + $"\n升级费用：{quote.Cost} 金币\n" +
            TrapUpgradeMenuSession.AttributePreview(upgradeSession.Definition, quote);
        if (directions) upgradeBody.text = "选择四级方向：普通四级\n" + upgradeBody.text;
        upgradeBalance.text = owner != null ? owner.BalanceText : "金币：--";
        EconomyManager wallet = EconomyManager.Instance;
        bool affordable = wallet != null && wallet.CanAfford(quote.Cost);
        upgradeFeedback.text = upgradeSession.Feedback;
        if (!complete && !affordable && string.IsNullOrEmpty(upgradeFeedback.text)) upgradeFeedback.text = "金币不足，确认不会扣款。";
        confirmUpgradeButton.interactable = !complete && quote.CanUpgrade;
        confirmUpgradeLabel.text = directions ? "普通四级" : "确认升级";
        cancelUpgradeLabel.text = complete ? "返回列表" : directions || step == TrapUpgradeMenuSession.DialogStep.ConfirmNormal ? "返回" : "取消";
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
}
