using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>One square, editor-authored trap card in the selection menu.</summary>
public sealed class TrapSelectionMenuCardView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [SerializeField] private Button button;
    [SerializeField] private Image cardBackground;
    [SerializeField] private Image icon;
    [SerializeField] private TextMeshProUGUI fallbackIcon;
    [SerializeField] private TextMeshProUGUI nameLabel;
    [SerializeField] private TextMeshProUGUI mountLabel;
    [SerializeField] private TextMeshProUGUI costLabel;
    [SerializeField] private TextMeshProUGUI statusLabel;
    [SerializeField] private Button upgradeButton;
    [SerializeField] private TextMeshProUGUI upgradeLabel;
    private System.Action<TrapDefinition> upgradeClicked;
    [SerializeField] private Color selectedColor = new Color(.09f, .36f, .41f, .98f);
    [SerializeField] private Color unaffordableColor = new Color(.32f, .12f, .12f, .96f);
    [SerializeField] private Color emptyColor = new Color(.075f, .16f, .20f, .85f);
    private TrapDefinition definition;
    private System.Action<TrapDefinition> clicked;
    private System.Action<TrapDefinition> hovered;
    private Color baseColor;
    private bool hasBaseColor;

    private void Awake()
    {
        CacheBaseColor();
    }

    public void Configure(System.Action<TrapDefinition> onClick, System.Action<TrapDefinition> onHover)
    {
        clicked = onClick;
        hovered = onHover;
        if (button != null)
        {
            button.onClick.RemoveListener(HandleClick);
            if (onClick != null) button.onClick.AddListener(HandleClick);
        }
        CacheBaseColor();
    }

    public void SetCard(TrapDefinition trap, bool selected, bool affordable, string status)
    {
        CacheBaseColor();
        definition = trap;
        bool hasTrap = trap != null;
        if (button != null) button.interactable = hasTrap;
        if (icon != null)
        {
            icon.sprite = hasTrap ? trap.MenuIcon : null;
            icon.enabled = hasTrap && trap.MenuIcon != null;
        }
        if (fallbackIcon != null)
        {
            fallbackIcon.text = !hasTrap ? "·" : trap.MenuIcon == null ? MountGlyph(trap.MountType) : string.Empty;
            fallbackIcon.color = hasTrap ? new Color(.12f, .82f, .94f, 1f) : new Color(.45f, .72f, .78f, .24f);
            fallbackIcon.enabled = fallbackIcon.text.Length > 0;
        }
        if (nameLabel != null) nameLabel.text = hasTrap ? trap.DisplayName : string.Empty;
        if (mountLabel != null) mountLabel.text = hasTrap ? trap.MountType.DisplayName() : string.Empty;
        if (costLabel != null) costLabel.text = hasTrap ? $"{trap.Cost} 金" : string.Empty;
        if (statusLabel != null) statusLabel.text = hasTrap ? status : string.Empty;
        if (cardBackground != null)
        {
            if (!hasTrap) cardBackground.color = emptyColor;
            else if (!affordable) cardBackground.color = unaffordableColor;
            else if (selected) cardBackground.color = selectedColor;
            else cardBackground.color = baseColor;
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (definition != null) hovered?.Invoke(definition);
    }

    public void OnPointerExit(PointerEventData eventData) => hovered?.Invoke(null);

    private void HandleClick()
    {
        if (definition != null) clicked?.Invoke(definition);
    }

    public void ConfigureUpgrade(System.Action<TrapDefinition> onUpgrade)
    {
        EnsureUpgradeUI();
        upgradeClicked = onUpgrade;
        if (upgradeButton == null) return;
        upgradeButton.onClick.RemoveListener(HandleUpgradeClick);
        if (onUpgrade != null) upgradeButton.onClick.AddListener(HandleUpgradeClick);
    }

    public void EnsureUpgradeUI()
    {
        if (upgradeButton == null)
        {
            upgradeButton = TrapUpgradeMenuUI.Button(transform, "Upgrade Arrow", nameLabel,
                new Vector2(1, 0), new Vector2(1, 0), new Vector2(-2, 2), new Vector2(28, 28), "↑", out upgradeLabel);
            upgradeLabel.fontSize = 20;
            upgradeButton.gameObject.SetActive(false);
        }
        if (upgradeButton.transform.Find("Arrow Ring") == null)
        {
            var ring = TrapUpgradeMenuUI.Rect(upgradeButton.transform, "Arrow Ring", new Vector2(.5f, .5f),
                new Vector2(.5f, .5f), Vector2.zero, new Vector2(27, 27)).gameObject.AddComponent<TrapUpgradeArrowRing>();
            ring.raycastTarget = false;
            ring.color = new Color(.22f, .92f, 1f, .9f);
            ring.transform.SetAsFirstSibling();
        }
    }

    public void SetUpgradeState(bool visible, int level)
    {
        if (upgradeButton == null) return;
        upgradeButton.gameObject.SetActive(visible && definition != null);
        upgradeButton.interactable = level < 4;
        if (upgradeLabel != null) upgradeLabel.text = level >= 4 ? "已满级" : "↑";
        if (upgradeLabel != null) upgradeLabel.fontSize = level >= 4 ? 8 : 20;
    }

    private void HandleUpgradeClick()
    {
        if (definition != null) upgradeClicked?.Invoke(definition);
    }

    private static string MountGlyph(TrapMountType mount)
        => mount == TrapMountType.Ground ? "G" : mount == TrapMountType.Wall ? "W" : "C";

    private void CacheBaseColor()
    {
        if (hasBaseColor || cardBackground == null) return;
        baseColor = cardBackground.color;
        hasBaseColor = true;
    }
}
