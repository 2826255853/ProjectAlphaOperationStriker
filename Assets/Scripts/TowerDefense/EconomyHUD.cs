using UnityEngine;

/// <summary>Persistent runtime balance display, below the wave/core panel.</summary>
public sealed class EconomyHUD : MonoBehaviour
{
    private readonly EconomyUIState economy = new EconomyUIState();
    private GUIStyle balanceStyle;
    public string BalanceText => economy.BalanceText;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureHUD()
    {
        if (FindAnyObjectByType<EconomyHUD>() != null) return;
        var host = new GameObject("Economy HUD");
        DontDestroyOnLoad(host);
        host.AddComponent<EconomyHUD>();
    }

    private void OnEnable() => economy.Enable();
    private void Update() => economy.Refresh();
    private void OnDisable() => economy.Dispose();
    private void OnDestroy() => economy.Dispose();

    private void OnGUI()
    {
        if (TrapSelectionMenu.IsOpen) return; // The menu shows the same balance in its header.
        economy.Refresh();
        if (balanceStyle == null)
            balanceStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 20,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = new Color(1f, 0.82f, 0.28f) }
            };
        Rect panel = new Rect(18f, 164f, 260f, 42f);
        Color previous = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.78f);
        GUI.DrawTexture(panel, Texture2D.whiteTexture);
        GUI.color = previous;
        GUI.Label(new Rect(panel.x + 14f, panel.y + 5f, panel.width - 28f, 32f), BalanceText, balanceStyle);
    }
}
