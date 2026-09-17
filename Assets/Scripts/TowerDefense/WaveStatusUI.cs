using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Small runtime HUD for wave progress. It is created automatically for every scene,
/// so no Canvas or prefab setup is required. If several spawn points exist, the HUD
/// shows the furthest wave reached and the shortest active wait among them.
/// </summary>
public sealed class WaveStatusUI : MonoBehaviour
{
    private static readonly Color PanelColor = new Color(0f, 0f, 0f, 0.72f);
    private static readonly Color AccentColor = new Color(1f, 0.78f, 0.22f, 1f);

    private EnemySpawnPoint[] spawnPoints = System.Array.Empty<EnemySpawnPoint>();
    private float nextRefreshTime;
    private GUIStyle panelStyle;
    private GUIStyle labelStyle;
    private GUIStyle accentStyle;
    private GUIStyle resultStyle;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CreateForScene()
    {
        if (FindAnyObjectByType<WaveStatusUI>() != null) return;
        var hudObject = new GameObject("Wave Status UI");
        DontDestroyOnLoad(hudObject);
        hudObject.AddComponent<WaveStatusUI>();
    }

    private void Update()
    {
        if (Time.unscaledTime >= nextRefreshTime)
        {
            spawnPoints = FindObjectsByType<EnemySpawnPoint>();
            nextRefreshTime = Time.unscaledTime + 0.5f;
        }

        if (Keyboard.current != null && Keyboard.current.gKey.wasPressedThisFrame)
        {
            for (int i = 0; i < spawnPoints.Length; i++)
                if (spawnPoints[i] != null) spawnPoints[i].SkipWait();
        }
    }

    private void OnGUI()
    {
        GameFlowManager flow = GameFlowManager.Instance;
        EnemyCore core = FindAnyObjectByType<EnemyCore>();
        if (flow != null && flow.CurrentResult != GameFlowManager.Result.Playing)
        {
            EnsureStyles();
            string message = flow.CurrentResult == GameFlowManager.Result.Victory ? "游戏胜利" : "游戏失败";
            GUI.Label(new Rect(Screen.width * 0.5f - 180f, Screen.height * 0.5f - 40f, 360f, 80f), message, resultStyle);
        }
        if (spawnPoints.Length == 0) return;
        EnsureStyles();

        int currentWave = 0;
        int totalWaves = WaveManager.Instance != null ? WaveManager.Instance.TotalWaves : 0;
        float waitTime = float.MaxValue;
        bool isWaiting = false;
        for (int i = 0; i < spawnPoints.Length; i++)
        {
            EnemySpawnPoint point = spawnPoints[i];
            if (point == null) continue;
            currentWave = Mathf.Max(currentWave, point.CurrentWave);
            if (totalWaves <= 0) totalWaves = point.TotalWaves;
            if (point.IsWaitingForWave)
            {
                isWaiting = true;
                waitTime = Mathf.Min(waitTime, point.RemainingWaitTime);
            }
        }

        if (totalWaves <= 0) return;
        const float width = 260f;
        const float height = 138f;
        Rect panel = new Rect(18f, 18f, width, height);
        GUI.Box(panel, GUIContent.none, panelStyle);
        GUI.Label(new Rect(panel.x + 14f, panel.y + 9f, width - 28f, 28f),
            $"波次  {Mathf.Max(1, currentWave)} / {totalWaves}", labelStyle);

        if (isWaiting)
        {
            GUI.Label(new Rect(panel.x + 14f, panel.y + 39f, width - 28f, 22f),
                $"下一波将在 {Mathf.CeilToInt(waitTime)} 秒后开始", accentStyle);
            GUI.Label(new Rect(panel.x + 14f, panel.y + 61f, width - 28f, 20f),
                "按 G 跳过等待", labelStyle);
        }
        else
        {
            GUI.Label(new Rect(panel.x + 14f, panel.y + 42f, width - 28f, 22f),
                currentWave >= totalWaves ? "全部波次完成" : "本波进行中", labelStyle);
        }
        if (core != null)
            GUI.Label(new Rect(panel.x + 14f, panel.y + 86f, width - 28f, 22f),
                $"核心  {core.CurrentHealth:0.#} / {core.MaxHealth:0.#}", accentStyle);
        PlayerHealth player = PlayerHealth.Instance;
        if (player != null)
            GUI.Label(new Rect(panel.x + 14f, panel.y + 110f, width - 28f, 22f),
                $"玩家  {player.CurrentHealth:0.#} / {player.MaxHealth:0.#}", accentStyle);
    }

    private void EnsureStyles()
    {
        if (panelStyle != null) return;
        panelStyle = new GUIStyle(GUI.skin.box)
        {
            normal = { background = MakeTexture(1, 1, PanelColor) },
            border = new RectOffset(8, 8, 8, 8)
        };
        labelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 18,
            fontStyle = FontStyle.Bold,
            normal = { textColor = Color.white }
        };
        accentStyle = new GUIStyle(labelStyle)
        {
            fontSize = 15,
            normal = { textColor = AccentColor }
        };
        resultStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 42,
            fontStyle = FontStyle.Bold,
            normal = { textColor = Color.white }
        };
    }

    private static Texture2D MakeTexture(int width, int height, Color color)
    {
        var texture = new Texture2D(width, height);
        texture.SetPixel(0, 0, color);
        texture.Apply();
        return texture;
    }
}
