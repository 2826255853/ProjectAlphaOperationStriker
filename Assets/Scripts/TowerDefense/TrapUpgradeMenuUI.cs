using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Additive Canvas controls; existing graphics, layout and fonts are never rewritten.</summary>
public static class TrapUpgradeMenuUI
{
    public static readonly Color Background = new Color(.035f, .13f, .18f, 1f);
    public static RectTransform Rect(Transform parent, string name, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = anchor;
        rect.pivot = pivot;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        return rect;
    }

    public static TextMeshProUGUI Label(Transform parent, string name, TextMeshProUGUI template,
        Vector2 anchor, Vector2 position, Vector2 size, string value, float fontSize)
    {
        RectTransform rect = Rect(parent, name, anchor, new Vector2(.5f, .5f), position, size);
        var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        if (template != null)
        {
            text.font = template.font;
            text.fontSharedMaterial = template.fontSharedMaterial;
            text.color = template.color;
        }
        else text.color = Color.white;
        text.fontSize = fontSize;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.textWrappingMode = TextWrappingModes.Normal;
        text.raycastTarget = false;
        text.text = value;
        return text;
    }

    public static Button Button(Transform parent, string name, TextMeshProUGUI template,
        Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size, string value, out TextMeshProUGUI label)
    {
        RectTransform rect = Rect(parent, name, anchor, pivot, position, size);
        var image = rect.gameObject.AddComponent<Image>();
        image.color = Background;
        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        label = Label(rect, "Label", template, new Vector2(.5f, .5f), Vector2.zero, size - new Vector2(8, 0), value, 18);
        label.alignment = TextAlignmentOptions.Center;
        return button;
    }
}
