using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// Runtime HUD for wave progress, incoming routes and core breaches.
/// </summary>
public sealed class WaveStatusUI : MonoBehaviour
{
    private static readonly Color PanelColor = new Color(0f, 0f, 0f, 0.72f);
    private static readonly Color AccentColor = new Color(1f, 0.78f, 0.22f, 1f);
    private static readonly Color AlertColor = new Color(1f, 0.42f, 0.36f, 1f);
    private const string GuideSeenKey = "AlphaStriker.WaveHud.GuideSeen";
    private const float GuideCombatSeconds = 10f;
    private const float LeakHoldSeconds = 6f;

    private readonly SortedDictionary<string, int> leakedRoutes = new SortedDictionary<string, int>(StringComparer.Ordinal);
    private EnemySpawnPoint[] spawnPoints = Array.Empty<EnemySpawnPoint>();
    private EnemyCore core;
    private Guid runId;
    private int currentWave;
    private int totalWaves;
    private float waitTime;
    private bool allCompleted;
    private float nextRefreshTime;
    private int forecastWave;
    private string groundForecast;
    private string airForecast;
    private bool hasAirRaid;
    private int leakWave;
    private float lastLeakTime;
    private string leakSummary;
    private bool showGuide;
    private float combatGuideTime;
    private GUIStyle panelStyle;
    private GUIStyle labelStyle;
    private GUIStyle accentStyle;
    private GUIStyle resultStyle;
    private GUIStyle resultHintStyle;
    private GUIStyle intelStyle;
    private GUIStyle guideStyle;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CreateForScene()
    {
        if (FindAnyObjectByType<WaveStatusUI>() != null) return;
        var hudObject = new GameObject("Wave Status UI");
        DontDestroyOnLoad(hudObject);
        hudObject.AddComponent<WaveStatusUI>();
    }

    private void OnEnable()
    {
        EnemyInstance.CoreReached += HandleCoreReached;
        SceneManager.sceneLoaded += HandleSceneLoaded;
        ResetMatch(EconomyManager.Instance != null ? EconomyManager.Instance.RunId : Guid.Empty);
    }

    private void OnDisable()
    {
        EnemyInstance.CoreReached -= HandleCoreReached;
        SceneManager.sceneLoaded -= HandleSceneLoaded;
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode != LoadSceneMode.Single) return;
        spawnPoints = Array.Empty<EnemySpawnPoint>();
        core = null;
        ResetMatch(Guid.Empty);
    }

    private void ResetMatch(Guid id)
    {
        runId = id;
        nextRefreshTime = 0f;
        currentWave = 0;
        forecastWave = 0;
        leakWave = 0;
        leakSummary = null;
        leakedRoutes.Clear();
        combatGuideTime = 0f;
        showGuide = PlayerPrefs.GetInt(GuideSeenKey) == 0;
    }

    private void Update()
    {
        EconomyManager wallet = EconomyManager.Instance;
        if (wallet != null && wallet.RunId != runId) ResetMatch(wallet.RunId);

        bool refresh = Time.unscaledTime >= nextRefreshTime;
        if (refresh)
        {
            spawnPoints = FindObjectsByType<EnemySpawnPoint>(FindObjectsSortMode.None);
            if (core == null) core = FindAnyObjectByType<EnemyCore>();
            nextRefreshTime = Time.unscaledTime + 0.5f;
        }

        currentWave = EnemySpawnPoint.GetWaveStatus(spawnPoints, out totalWaves, out waitTime, out allCompleted);
        int nextForecastWave = waitTime > 0f ? currentWave : 0;
        if (nextForecastWave > 0 && (refresh || nextForecastWave != forecastWave))
            RefreshForecast(nextForecastWave);
        forecastWave = nextForecastWave;

        if (GameStartMenu.CursorOwned || TrapSelectionMenu.IsOpen || currentWave == 0
            || allCompleted || (GameFlowManager.Instance != null && GameFlowManager.Instance.CurrentResult != GameFlowManager.Result.Playing))
            return;

        if (Keyboard.current != null && Keyboard.current.gKey.wasPressedThisFrame)
        {
            foreach (EnemySpawnPoint point in spawnPoints)
                if (point != null && point.isActiveAndEnabled) point.SkipWait();
        }

        if (showGuide && waitTime <= 0f)
        {
            combatGuideTime += Time.deltaTime;
            if (combatGuideTime >= GuideCombatSeconds)
            {
                showGuide = false;
                PlayerPrefs.SetInt(GuideSeenKey, 1);
                PlayerPrefs.Save();
            }
        }
    }

    private void HandleCoreReached(EnemyInstance enemy)
    {
        if (enemy.WaveNumber <= 0) return;
        if (runId != enemy.RunId) ResetMatch(enemy.RunId);
        if (leakWave != enemy.WaveNumber)
        {
            leakWave = enemy.WaveNumber;
            leakedRoutes.Clear();
        }

        lastLeakTime = Time.time;
        string direction = GetEntranceDirection(enemy.SpawnPoint);
        if (enemy.MonsterType == MonsterType.Flying) direction += "空中";
        leakedRoutes.TryGetValue(direction, out int count);
        leakedRoutes[direction] = count + 1;

        var labels = new List<string>(leakedRoutes.Count);
        foreach (var route in leakedRoutes)
            labels.Add($"{route.Key} ×{route.Value}");
        leakSummary = $"漏怪  ·  {string.Join("、", labels)}";
    }

    private void RefreshForecast(int wave)
    {
        var ground = new SortedSet<string>(StringComparer.Ordinal);
        var upper = new SortedSet<string>(StringComparer.Ordinal);
        var ascending = new SortedSet<string>(StringComparer.Ordinal);
        var air = new SortedSet<string>(StringComparer.Ordinal);
        foreach (EnemySpawnPoint point in spawnPoints)
        {
            if (point == null || !point.IsForecastEntrance(wave)) continue;
            string direction = GetEntranceDirection(point);
            if (point.GetMonsterTypeForWave(wave) == MonsterType.Flying)
            {
                air.Add(direction);
                continue;
            }

            GetRouteFloors(point, out bool usesGround, out bool usesUpper);
            if (usesGround && usesUpper) ascending.Add(direction);
            else if (usesUpper) upper.Add(direction);
            else ground.Add(direction);
        }

        var lines = new List<string>(3);
        if (ground.Count > 0) lines.Add($"一层  ·  {string.Join("、", ground)}");
        if (upper.Count > 0) lines.Add($"二层  ·  {string.Join("、", upper)}");
        if (ascending.Count > 0) lines.Add($"一层 → 二层  ·  {string.Join("、", ascending)}");
        groundForecast = string.Join("\n", lines);
        hasAirRaid = air.Count > 0;
        airForecast = hasAirRaid ? $"空袭  ·  {string.Join("、", air)}" : "本波无空袭";
    }

    private static void GetRouteFloors(EnemySpawnPoint point, out bool ground, out bool upper)
    {
        // The authored walking points use floor surfaces. The core model sits above its floor.
        float groundY = point.PathGrid != null
            ? point.PathGrid.CellToWorld(Vector2Int.zero).y
            : point.transform.position.y;
        const float upperFloorThreshold = 3f;
        ground = true;
        upper = false;
        if (!point.TryGetComponent<AuthoredMonsterRoute>(out var route)) return;

        bool foundGround = false;
        foreach (AuthoredMonsterRoute.Route branch in route.Routes)
        {
            foreach (Transform step in branch.points)
            {
                if (step == null) continue;
                if (step.position.y - groundY >= upperFloorThreshold) upper = true;
                else foundGround = true;
            }
        }
        ground = foundGround || !upper;
    }

    private static string GetEntranceDirection(EnemySpawnPoint point)
    {
        if (point.TryGetComponent<MapForgeObjectProperties>(out var properties))
        {
            switch (properties.objectId)
            {
                case "enemy-spawn-left": return "左路";
                case "enemy-spawn-middle": return "中路";
                case "enemy-spawn-right": return "右路";
                case "enemy-spawn-upper-left": return "后场左侧";
                case "enemy-spawn-upper-right": return "后场右侧";
                case "enemy-spawn-pump": return "西侧冷却泵房";
                case "enemy-spawn-workshop": return "东侧车间";
                case "enemy-spawn-flying-front-left": return "前场左侧";
                case "enemy-spawn-flying-front-right": return "前场右侧";
                case "enemy-spawn-flying-rear-left": return "后场左侧";
                case "enemy-spawn-flying-rear-right": return "后场右侧";
            }
        }
        switch (point.name)
        {
            case "Enemy Spawn Left": return "左路";
            case "Enemy Spawn Center": return "中路";
            case "Enemy Spawn Right": return "右路";
        }
        if (point.Core == null) return point.name;
        Vector3 fromCore = point.transform.position - point.Core.Position;
        if (Mathf.Abs(fromCore.x) > Mathf.Abs(fromCore.z))
            return fromCore.x > 0f ? "东侧" : "西侧";
        return fromCore.z >= 0f ? "北侧" : "南侧";
    }

    private void OnGUI()
    {
        // The start menu is a full-screen overlay; keep the gameplay HUD out of it.
        if (GameStartMenu.CursorOwned || TrapSelectionMenu.IsOpen) return;
        GameFlowManager flow = GameFlowManager.Instance;
        bool playing = flow == null || flow.CurrentResult == GameFlowManager.Result.Playing;
        if (!playing)
        {
            EnsureStyles();
            string message = flow.CurrentResult == GameFlowManager.Result.Victory ? "游戏胜利" : "游戏失败";
            GUI.Label(new Rect(Screen.width * 0.5f - 180f, Screen.height * 0.5f - 40f, 360f, 80f), message, resultStyle);
            double remaining = System.Math.Ceiling(flow.ReturnSecondsRemaining);
            string hint = remaining > 0 ? $"{remaining} 秒后返回主菜单 · 按 G 立即返回" : "按 G 返回主菜单";
            GUI.Label(new Rect(Screen.width * 0.5f - 180f, Screen.height * 0.5f + 30f, 360f, 28f),
                hint, resultHintStyle);
        }
        bool showLeak = leakSummary != null && (Time.time - lastLeakTime < LeakHoldSeconds
            || (playing && forecastWave == 0 && !allCompleted && currentWave == leakWave));
        if (showLeak || (playing && currentWave > 0 && !allCompleted && (showGuide || forecastWave > 0)))
            DrawIntel(playing, showLeak);
        if (currentWave <= 0 || totalWaves <= 0) return;
        EnsureStyles();
        const float width = 260f;
        const float height = 138f;
        Rect panel = new Rect(18f, 18f, width, height);
        GUI.Box(panel, GUIContent.none, panelStyle);
        GUI.Label(new Rect(panel.x + 14f, panel.y + 9f, width - 28f, 28f),
            $"波次  {Mathf.Max(1, currentWave)} / {totalWaves}", labelStyle);

        if (playing && waitTime > 0f)
        {
            GUI.Label(new Rect(panel.x + 14f, panel.y + 39f, width - 28f, 22f),
                $"下一波将在 {Mathf.CeilToInt(waitTime)} 秒后开始", accentStyle);
            GUI.Label(new Rect(panel.x + 14f, panel.y + 61f, width - 28f, 20f),
                "按 G 跳过等待", labelStyle);
        }
        else if (playing || allCompleted)
        {
            GUI.Label(new Rect(panel.x + 14f, panel.y + 42f, width - 28f, 22f),
                allCompleted ? "全部波次完成" : "本波进行中", labelStyle);
        }
        if (core != null)
            GUI.Label(new Rect(panel.x + 14f, panel.y + 86f, width - 28f, 22f),
                $"核心  {core.CurrentHealth:0.#} / {core.MaxHealth:0.#}", accentStyle);
        PlayerHealth player = PlayerHealth.Instance;
        if (player != null)
            GUI.Label(new Rect(panel.x + 14f, panel.y + 110f, width - 28f, 22f),
                $"玩家  {player.CurrentHealth:0.#} / {player.MaxHealth:0.#}", accentStyle);
    }

    private void DrawIntel(bool playing, bool showLeak)
    {
        const float left = 290f;
        const float top = 18f;
        const float padding = 12f;
        float width = Mathf.Min(440f, Screen.width - left - 18f);
        if (width < 100f) return;
        float textWidth = width - 2f * padding;
        EnsureStyles();

        bool showForecast = playing && forecastWave > 0;
        bool showHint = playing && showGuide && currentWave > 0 && !allCompleted;
        string hint = showForecast && combatGuideTime <= 0f
            ? "先看小地图路线，按 4–8 选陷阱，左键放置。"
            : TrapPlacementController.IsPlacementModeActive
                ? "按 Esc 退出放置，沿小地图找漏网敌人，开火补漏。"
                : "沿小地图找漏网敌人，开火补漏。";

        float groundHeight = showForecast ? Measure(groundForecast, intelStyle, textWidth) : 0f;
        float airHeight = showForecast ? Measure(airForecast, intelStyle, textWidth) : 0f;
        float leakHeight = showLeak ? Measure(leakSummary, intelStyle, textWidth) : 0f;
        float guideHeight = showHint ? Measure(hint, guideStyle, textWidth) : 0f;
        float height = 2f * padding + groundHeight + airHeight + leakHeight + guideHeight;
        GUI.Box(new Rect(left, top, width, height), GUIContent.none, panelStyle);
        float y = top + padding;
        DrawIntelLine(leakSummary, intelStyle, AlertColor, left + padding, ref y, textWidth, leakHeight);
        DrawIntelLine(groundForecast, intelStyle, Color.white, left + padding, ref y, textWidth, groundHeight);
        DrawIntelLine(airForecast, intelStyle, hasAirRaid ? AccentColor : Color.white, left + padding, ref y, textWidth, airHeight);
        DrawIntelLine(hint, guideStyle, new Color(0.62f, 0.9f, 0.93f), left + padding, ref y, textWidth, guideHeight);
    }

    private static float Measure(string text, GUIStyle style, float width) =>
        string.IsNullOrEmpty(text) ? 0f : style.CalcHeight(new GUIContent(text), width) + 4f;

    private static void DrawIntelLine(string text, GUIStyle style, Color color, float x, ref float y, float width, float height)
    {
        if (height <= 0f) return;
        style.normal.textColor = color;
        GUI.Label(new Rect(x, y, width, height), text, style);
        y += height;
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
        intelStyle = new GUIStyle(labelStyle) { fontSize = 16, wordWrap = true };
        guideStyle = new GUIStyle(intelStyle) { fontSize = 15, fontStyle = FontStyle.Normal };
        resultStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 42,
            fontStyle = FontStyle.Bold,
            normal = { textColor = Color.white }
        };
        resultHintStyle = new GUIStyle(labelStyle) { alignment = TextAnchor.MiddleCenter };
    }

    private static Texture2D MakeTexture(int width, int height, Color color)
    {
        var texture = new Texture2D(width, height);
        texture.SetPixel(0, 0, color);
        texture.Apply();
        return texture;
    }
}
