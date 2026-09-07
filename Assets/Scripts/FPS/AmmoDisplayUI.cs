using TMPro;
using UnityEngine;
using UnityEngine.UI;
using KINEMATION.FPSAnimationPack.Scripts.Player;
using KINEMATION.FPSAnimationPack.Scripts.Weapon;

/// <summary>
/// Lightweight runtime HUD for the active weapon magazine.
/// The weapon package owns firing and reloading; this component only presents
/// the current magazine count. Reserve ammunition is intentionally infinite.
/// </summary>
public sealed class AmmoDisplayUI : MonoBehaviour
{
    private const string InfiniteReserve = "∞";

    [Header("Layout")]
    [SerializeField, Min(12f)] private float ammoFontSize = 32f;
    [SerializeField, Min(10f)] private float hintFontSize = 14f;
    [SerializeField] private Vector2 panelSize = new Vector2(220f, 88f);
    [SerializeField] private Vector2 panelMargin = new Vector2(28f, 24f);

    [Header("Colors")]
    [SerializeField] private Color panelColor = new Color(0.02f, 0.025f, 0.04f, 0.78f);
    [SerializeField] private Color normalAmmoColor = Color.white;
    [SerializeField] private Color lowAmmoColor = new Color(1f, 0.28f, 0.2f, 1f);
    [SerializeField] private Color hintColor = new Color(0.75f, 0.8f, 0.88f, 1f);

    private FPSPlayer player;
    private TMP_Text ammoText;
    private TMP_Text hintText;
    private int lastAmmo = -1;
    private int lastMaxAmmo = -1;

    private void Start()
    {
        player = GetComponentInChildren<FPSPlayer>(true);
        if (player == null)
        {
            player = FindAnyObjectByType<FPSPlayer>();
        }

        CreateHud();
        RefreshHud(true);
    }

    private void Update()
    {
        RefreshHud(false);
    }

    private void CreateHud()
    {
        GameObject canvasObject = new GameObject("Ammo HUD");
        canvasObject.transform.SetParent(transform, false);

        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;

        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        canvasObject.AddComponent<GraphicRaycaster>();

        GameObject panelObject = new GameObject("Ammo Panel");
        panelObject.transform.SetParent(canvasObject.transform, false);
        RectTransform panelRect = panelObject.AddComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(1f, 0f);
        panelRect.anchorMax = new Vector2(1f, 0f);
        panelRect.pivot = new Vector2(1f, 0f);
        // With a bottom-right pivot, a negative X margin keeps the panel on-screen.
        panelRect.anchoredPosition = new Vector2(-panelMargin.x, panelMargin.y);
        panelRect.sizeDelta = panelSize;
        Image panelImage = panelObject.AddComponent<Image>();
        panelImage.color = panelColor;

        ammoText = CreateText(
            panelObject.transform,
            "Magazine Text",
            new Vector2(0f, 0.25f),
            new Vector2(1f, 1f),
            new Vector2(0f, -2f),
            new Vector2(0f, -2f),
            ammoFontSize,
            TextAlignmentOptions.Center,
            normalAmmoColor);

        hintText = CreateText(
            panelObject.transform,
            "Reload Hint",
            new Vector2(0f, 0f),
            new Vector2(1f, 0.35f),
            Vector2.zero,
            Vector2.zero,
            hintFontSize,
            TextAlignmentOptions.Center,
            hintColor);
        hintText.text = "R  RELOAD   |   RESERVE: ∞";
    }

    private TMP_Text CreateText(
        Transform parent,
        string objectName,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 offsetMin,
        Vector2 offsetMax,
        float fontSize,
        TextAlignmentOptions alignment,
        Color color)
    {
        GameObject textObject = new GameObject(objectName);
        textObject.transform.SetParent(parent, false);

        RectTransform rect = textObject.AddComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;

        TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
        text.font = TMP_Settings.defaultFontAsset;
        text.fontSize = fontSize;
        text.color = color;
        text.alignment = alignment;
        text.enableAutoSizing = false;
        text.raycastTarget = false;
        return text;
    }

    private void RefreshHud(bool force)
    {
        if (player == null || ammoText == null)
        {
            return;
        }

        FPSWeapon activeWeapon;
        try
        {
            activeWeapon = player.GetActiveWeapon();
        }
        catch (System.ArgumentOutOfRangeException)
        {
            // FPSPlayer fills its weapon list in Start; retry next frame.
            return;
        }

        if (activeWeapon == null)
        {
            return;
        }

        int ammo = activeWeapon.GetActiveAmmo();
        int maxAmmo = activeWeapon.GetMaxAmmo();
        if (!force && ammo == lastAmmo && maxAmmo == lastMaxAmmo)
        {
            return;
        }

        lastAmmo = ammo;
        lastMaxAmmo = maxAmmo;
        ammoText.text = $"{ammo} / {InfiniteReserve}";
        ammoText.color = ammo <= Mathf.Max(1, Mathf.CeilToInt(maxAmmo * 0.2f))
            ? lowAmmoColor
            : normalAmmoColor;
    }
}
