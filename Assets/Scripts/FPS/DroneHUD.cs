#if ENABLE_DRONE_CODE
using UnityEngine;
using TMPro;

/// <summary>
/// Displays a simple HUD prompt when the player is in drone mode.
/// Shows "无人机模式 / 按 V 返回" overlay.
/// </summary>
[RequireComponent(typeof(Canvas))]
public sealed class DroneHUD : MonoBehaviour
{
    private DroneControlSession session;
    private Canvas canvas;
    private TextMeshProUGUI promptText;
    private bool isInitialized;

    private const string PromptMessage = "无人机模式 | 按 V 返回玩家";

    private void Awake()
    {
        canvas = GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100; // High priority to appear on top

        CreatePromptText();
        isInitialized = true;

        // Start hidden
        canvas.enabled = false;
    }

    private void CreatePromptText()
    {
        // Create text object
        GameObject textObj = new GameObject("DronePrompt");
        textObj.transform.SetParent(transform, false);

        promptText = textObj.AddComponent<TextMeshProUGUI>();
        promptText.text = PromptMessage;
        promptText.fontSize = 24;
        promptText.color = new Color(1f, 1f, 1f, 0.9f);
        promptText.alignment = TextAlignmentOptions.Center;

        // Load font from Resources (reuse existing TMP font)
        TMP_FontAsset font = Resources.Load<TMP_FontAsset>("UI/Fonts/MenuChinese SDF");
        if (font != null)
        {
            promptText.font = font;
        }
        else
        {
            Debug.LogWarning("[DroneHUD] MenuChinese SDF font not found. Using default TMP font.");
        }

        // Position at top-center of screen
        RectTransform rectTransform = promptText.GetComponent<RectTransform>();
        rectTransform.anchorMin = new Vector2(0.5f, 1f);
        rectTransform.anchorMax = new Vector2(0.5f, 1f);
        rectTransform.pivot = new Vector2(0.5f, 1f);
        rectTransform.anchoredPosition = new Vector2(0f, -40f); // 40px from top
        rectTransform.sizeDelta = new Vector2(600f, 50f);
    }

    /// <summary>Bind to a drone control session to track its state.</summary>
    public void BindSession(DroneControlSession droneSession)
    {
        session = droneSession;
    }

    private void Update()
    {
        if (!isInitialized || session == null) return;

        // Show HUD only when in Drone state
        bool shouldShow = (session.State == DroneControlState.Drone);

        if (canvas.enabled != shouldShow)
        {
            canvas.enabled = shouldShow;
        }
    }

    private void OnDestroy()
    {
        session = null;
    }
}
#endif
