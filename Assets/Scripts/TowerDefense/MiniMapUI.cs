using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Top-down route and enemy overview for the current gameplay scene.</summary>
public sealed class MiniMapUI : MonoBehaviour
{
    private const float RefreshInterval = 0.35f;
    private const float MapSide = 222f;
    private static readonly Color PanelColor = new Color(0.025f, 0.04f, 0.045f, 0.88f);
    private static readonly Color GridColor = new Color(0.35f, 0.48f, 0.49f, 0.28f);
    private static readonly Color RouteColor = new Color(0.28f, 0.83f, 0.88f, 0.9f);
    private static readonly Color GroundColor = new Color(1f, 0.25f, 0.24f, 1f);
    private static readonly Color FlyingColor = new Color(1f, 0.82f, 0.22f, 1f);
    private static readonly Color EntranceColor = new Color(0.9f, 0.96f, 0.95f, 1f);

    private readonly List<RoutePreview> routePreviews = new List<RoutePreview>();
    private EnemySpawnPoint[] spawnPoints = System.Array.Empty<EnemySpawnPoint>();
    private EnemyInstance[] enemies = System.Array.Empty<EnemyInstance>();
    private EnemySpawnPoint[] cachedRouteEntrances = System.Array.Empty<EnemySpawnPoint>();
    private Rect mapArea;
    private Bounds worldBounds;
    private float nextRefreshTime;
    private bool hasWorldBounds;
    private int previewWave;
    private int cachedRouteWave = -1;
    private int cachedRouteSceneHandle = -1;
    private Texture2D pixelTexture;
    private Texture2D circleTexture;
    private GUIStyle titleStyle;
    private GUIStyle legendStyle;

    private sealed class RoutePreview
    {
        public EnemySpawnPoint SpawnPoint;
        public MonsterType MonsterType;
        public readonly List<Vector3[]> Branches = new List<Vector3[]>();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CreateForScene()
    {
        if (FindAnyObjectByType<MiniMapUI>() != null) return;
        var hudObject = new GameObject("Mini Map UI");
        DontDestroyOnLoad(hudObject);
        hudObject.AddComponent<MiniMapUI>();
    }

    private void Update()
    {
        if (Time.unscaledTime < nextRefreshTime) return;
        nextRefreshTime = Time.unscaledTime + RefreshInterval;
        RefreshMapData();
    }

    private void RefreshMapData()
    {
        spawnPoints = FindObjectsByType<EnemySpawnPoint>(FindObjectsSortMode.None);
        enemies = FindObjectsByType<EnemyInstance>(FindObjectsSortMode.None);
        hasWorldBounds = false;
        previewWave = 0;
        int wave = EnemySpawnPoint.GetWaveStatus(spawnPoints, out _, out float wait, out _);
        previewWave = wait > 0f ? wave : 0;
        EnemyCore sceneCore = FindAnyObjectByType<EnemyCore>();

        for (int i = 0; i < spawnPoints.Length; i++)
        {
            EnemySpawnPoint spawnPoint = spawnPoints[i];
            if (spawnPoint == null) continue;
            IncludeInBounds(spawnPoint.transform.position);
            MonsterPathGrid pathGrid = spawnPoint.PathGrid;
            if (pathGrid != null)
            {
                Vector3 gridOrigin = pathGrid.transform.TransformPoint(Vector3.zero);
                Vector3 gridWidth = pathGrid.transform.TransformPoint(new Vector3(pathGrid.Columns * pathGrid.CellSize, 0f, 0f));
                Vector3 gridDepth = pathGrid.transform.TransformPoint(new Vector3(0f, 0f, pathGrid.Rows * pathGrid.CellSize));
                Vector3 gridFarCorner = pathGrid.transform.TransformPoint(new Vector3(
                    pathGrid.Columns * pathGrid.CellSize, 0f, pathGrid.Rows * pathGrid.CellSize));
                IncludeInBounds(gridOrigin);
                IncludeInBounds(gridWidth);
                IncludeInBounds(gridDepth);
                IncludeInBounds(gridFarCorner);
            }

            AuthoredMonsterRoute authoredRoute = spawnPoint.GetComponent<AuthoredMonsterRoute>();
            if (authoredRoute != null)
            {
                IReadOnlyList<AuthoredMonsterRoute.Route> routes = authoredRoute.Routes;
                for (int routeIndex = 0; routeIndex < routes.Count; routeIndex++)
                {
                    Transform[] points = routes[routeIndex]?.points;
                    if (points == null) continue;
                    for (int pointIndex = 0; pointIndex < points.Length; pointIndex++)
                        if (points[pointIndex] != null) IncludeInBounds(points[pointIndex].position);
                }
            }

            EnemyCore core = spawnPoint.Core != null ? spawnPoint.Core : sceneCore;
            if (core != null) IncludeInBounds(core.Position);
            if (spawnPoint.TargetPoint != null) IncludeInBounds(spawnPoint.TargetPoint.position);
            else IncludeInBounds(spawnPoint.TargetPosition);
        }

        if (previewWave > 0)
        {
            var routeEntrances = new List<EnemySpawnPoint>();
            for (int i = 0; i < spawnPoints.Length; i++)
            {
                EnemySpawnPoint spawnPoint = spawnPoints[i];
                if (spawnPoint == null || !spawnPoint.IsForecastEntrance(previewWave))
                    continue;
                routeEntrances.Add(spawnPoint);
            }

            int sceneHandle = SceneManager.GetActiveScene().GetHashCode();
            if (ShouldRebuildRoutePreviews(previewWave, sceneHandle, routeEntrances))
            {
                routePreviews.Clear();
                for (int i = 0; i < routeEntrances.Count; i++)
                {
                    EnemySpawnPoint spawnPoint = routeEntrances[i];
                    AuthoredMonsterRoute authoredRoute = spawnPoint.GetComponent<AuthoredMonsterRoute>();
                    EnemyCore core = spawnPoint.Core != null ? spawnPoint.Core : sceneCore;
                    routePreviews.Add(BuildRoutePreview(spawnPoint, authoredRoute, core, previewWave));
                }
                cachedRouteWave = previewWave;
                cachedRouteSceneHandle = sceneHandle;
                cachedRouteEntrances = routeEntrances.ToArray();
            }
        }
        for (int i = 0; i < enemies.Length; i++)
        {
            EnemyInstance enemy = enemies[i];
            if (enemy != null && enemy.Resolution == EnemyInstance.ResolutionState.Alive)
                IncludeInBounds(enemy.transform.position);
        }

        if (!hasWorldBounds) worldBounds = new Bounds(Vector3.zero, new Vector3(1f, 0f, 1f));
        Vector3 size = worldBounds.size;
        size.x = Mathf.Max(10f, size.x * 1.08f);
        size.z = Mathf.Max(10f, size.z * 1.08f);
        worldBounds.size = size;
    }

    private bool ShouldRebuildRoutePreviews(int wave, int sceneHandle, List<EnemySpawnPoint> entrances)
    {
        if (wave != cachedRouteWave || sceneHandle != cachedRouteSceneHandle
            || entrances.Count != cachedRouteEntrances.Length)
            return true;

        for (int i = 0; i < entrances.Count; i++)
        {
            bool found = false;
            for (int cachedIndex = 0; cachedIndex < cachedRouteEntrances.Length; cachedIndex++)
            {
                if (entrances[i] != cachedRouteEntrances[cachedIndex]) continue;
                found = true;
                break;
            }
            if (!found) return true;
        }
        return false;
    }

    private RoutePreview BuildRoutePreview(EnemySpawnPoint spawnPoint, AuthoredMonsterRoute authoredRoute,
        EnemyCore core, int wave)
    {
        var preview = new RoutePreview
        {
            SpawnPoint = spawnPoint,
            MonsterType = spawnPoint.GetMonsterTypeForWave(wave)
        };

        if (preview.MonsterType == MonsterType.Flying)
        {
            Vector3 destination = core != null ? core.Position : GetFallbackDestination(spawnPoint);
            preview.Branches.Add(new[] { spawnPoint.transform.position, destination });
            IncludeInBounds(destination);
            return preview;
        }

        if (authoredRoute != null && authoredRoute.Routes.Count > 0)
        {
            IReadOnlyList<AuthoredMonsterRoute.Route> routes = authoredRoute.Routes;
            for (int i = 0; i < routes.Count; i++)
            {
                Transform[] points = routes[i]?.points;
                if (points == null || points.Length == 0) continue;
                var branch = new List<Vector3>(points.Length + 2) { spawnPoint.transform.position };
                for (int pointIndex = 0; pointIndex < points.Length; pointIndex++)
                {
                    if (points[pointIndex] == null) continue;
                    branch.Add(points[pointIndex].position);
                    IncludeInBounds(points[pointIndex].position);
                }
                if (core != null)
                {
                    Vector3 end = core.Position;
                    end.y = branch[branch.Count - 1].y;
                    branch.Add(end);
                }
                preview.Branches.Add(branch.ToArray());
            }
            return preview;
        }

        Vector3 target = core != null ? core.Position : GetFallbackDestination(spawnPoint);
        var path = new List<Vector3>();
        if (spawnPoint.PathGrid != null)
        {
            bool foundPath = spawnPoint.PathGrid.TryFindPath(spawnPoint.transform.position, target,
                spawnPoint.TravelDirection, path);
            if (!foundPath) return preview;
        }
        else
        {
            path.Add(target);
        }
        var pointsOnPath = new Vector3[path.Count + 1];
        pointsOnPath[0] = spawnPoint.transform.position;
        for (int i = 0; i < path.Count; i++)
        {
            pointsOnPath[i + 1] = path[i];
            IncludeInBounds(path[i]);
        }
        preview.Branches.Add(pointsOnPath);
        IncludeInBounds(target);
        return preview;
    }

    private static Vector3 GetFallbackDestination(EnemySpawnPoint spawnPoint)
    {
        return spawnPoint.TargetPoint != null ? spawnPoint.TargetPoint.position : spawnPoint.TargetPosition;
    }

    private void IncludeInBounds(Vector3 position)
    {
        if (!hasWorldBounds)
        {
            worldBounds = new Bounds(position, Vector3.zero);
            hasWorldBounds = true;
        }
        else
        {
            worldBounds.Encapsulate(position);
        }
    }

    private void OnGUI()
    {
        if (GameStartMenu.CursorOwned || TrapSelectionMenu.IsOpen) return;
        EnsureTexturesAndStyles();

        float top = TrapPlacementController.IsPlacementModeActive ? 368f : 244f;
        float side = Mathf.Min(MapSide, Screen.width - 36f, Screen.height - top - 65f);
        if (side < 164f) return;
        Rect panel = new Rect(18f, top, side, side + 47f);
        DrawRect(panel, PanelColor);
        GUI.Label(new Rect(panel.x + 9f, panel.y + 5f, side - 18f, 20f),
            previewWave > 0 ? $"路线预览  ·  第 {previewWave} 波" : "小地图", titleStyle);

        mapArea = new Rect(panel.x + 8f, panel.y + 27f, side - 16f, side - 16f);
        DrawRect(mapArea, new Color(0.07f, 0.105f, 0.105f, 0.93f));
        DrawGrid();
        if (previewWave > 0)
        {
            DrawRoutes();
            DrawSpawnPoints();
        }
        DrawEnemies();
        DrawLegend(new Rect(panel.x + 8f, mapArea.yMax + 5f, side - 16f, 13f));
    }

    private void DrawGrid()
    {
        for (int i = 1; i < 4; i++)
        {
            float x = Mathf.Lerp(mapArea.xMin, mapArea.xMax, i * 0.25f);
            float y = Mathf.Lerp(mapArea.yMin, mapArea.yMax, i * 0.25f);
            DrawLine(new Vector2(x, mapArea.yMin), new Vector2(x, mapArea.yMax), 1f, GridColor);
            DrawLine(new Vector2(mapArea.xMin, y), new Vector2(mapArea.xMax, y), 1f, GridColor);
        }
    }

    private void DrawRoutes()
    {
        for (int i = 0; i < routePreviews.Count; i++)
        {
            RoutePreview preview = routePreviews[i];
            for (int branchIndex = 0; branchIndex < preview.Branches.Count; branchIndex++)
            {
                Vector3[] branch = preview.Branches[branchIndex];
                for (int pointIndex = 1; pointIndex < branch.Length; pointIndex++)
                    DrawLine(WorldToMap(branch[pointIndex - 1]), WorldToMap(branch[pointIndex]),
                        branchIndex == 0 ? 2f : 1f, branchIndex == 0 ? RouteColor : new Color(RouteColor.r, RouteColor.g, RouteColor.b, 0.38f));
            }
        }
    }

    private void DrawSpawnPoints()
    {
        for (int i = 0; i < routePreviews.Count; i++)
        {
            RoutePreview preview = routePreviews[i];
            Vector2 position = WorldToMap(preview.SpawnPoint.transform.position);
            DrawCircle(position, 12f, EntranceColor);
            DrawCircle(position, 7f, preview.MonsterType == MonsterType.Flying ? FlyingColor : RouteColor);
        }
    }

    private void DrawEnemies()
    {
        for (int i = 0; i < enemies.Length; i++)
        {
            EnemyInstance enemy = enemies[i];
            if (enemy == null || enemy.Resolution != EnemyInstance.ResolutionState.Alive) continue;
            Color color = enemy.MonsterType == MonsterType.Flying ? FlyingColor : GroundColor;
            DrawCircle(WorldToMap(enemy.transform.position), 8f, color);
        }
    }

    private void DrawLegend(Rect rect)
    {
        float column = rect.width / 4f;
        var icon = new Vector2(rect.x + 5f, rect.center.y);
        DrawCircle(icon, 7f, GroundColor);
        GUI.Label(new Rect(icon.x + 6f, rect.y, column - 10f, rect.height), "地面", legendStyle);
        icon.x += column;
        DrawCircle(icon, 7f, FlyingColor);
        GUI.Label(new Rect(icon.x + 6f, rect.y, column - 10f, rect.height), "空中", legendStyle);
        icon.x += column;
        DrawLine(icon - Vector2.right * 4f, icon + Vector2.right * 4f, 2f, RouteColor);
        GUI.Label(new Rect(icon.x + 6f, rect.y, column - 10f, rect.height), "路线", legendStyle);
        icon.x += column;
        DrawCircle(icon, 9f, EntranceColor);
        DrawCircle(icon, 4f, RouteColor);
        GUI.Label(new Rect(icon.x + 6f, rect.y, column - 10f, rect.height), "入口", legendStyle);
    }

    private Vector2 WorldToMap(Vector3 worldPosition)
    {
        float worldWidth = Mathf.Max(1f, worldBounds.size.x);
        float worldDepth = Mathf.Max(1f, worldBounds.size.z);
        float scale = Mathf.Min(mapArea.width / worldWidth, mapArea.height / worldDepth);
        Vector2 center = mapArea.center;
        return new Vector2(
            center.x + (worldPosition.x - worldBounds.center.x) * scale,
            center.y - (worldPosition.z - worldBounds.center.z) * scale);
    }

    private void DrawCircle(Vector2 center, float size, Color color)
    {
        GUI.color = color;
        GUI.DrawTexture(new Rect(center.x - size * 0.5f, center.y - size * 0.5f, size, size), circleTexture);
        GUI.color = Color.white;
    }

    private void DrawLine(Vector2 start, Vector2 end, float width, Color color)
    {
        Vector2 delta = end - start;
        if (delta.sqrMagnitude < 0.01f) return;
        Matrix4x4 oldMatrix = GUI.matrix;
        GUIUtility.RotateAroundPivot(Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg, start);
        GUI.color = color;
        GUI.DrawTexture(new Rect(start.x, start.y - width * 0.5f, delta.magnitude, width), pixelTexture);
        GUI.color = Color.white;
        GUI.matrix = oldMatrix;
    }

    private void DrawRect(Rect rect, Color color)
    {
        GUI.color = color;
        GUI.DrawTexture(rect, pixelTexture);
        GUI.color = Color.white;
    }

    private void EnsureTexturesAndStyles()
    {
        if (pixelTexture == null)
        {
            pixelTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Point
            };
            pixelTexture.SetPixel(0, 0, Color.white);
            pixelTexture.Apply();
        }

        if (circleTexture == null)
        {
            const int resolution = 32;
            circleTexture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear
            };
            for (int y = 0; y < resolution; y++)
            for (int x = 0; x < resolution; x++)
            {
                float distance = Vector2.Distance(new Vector2(x, y), new Vector2((resolution - 1) * 0.5f, (resolution - 1) * 0.5f));
                float alpha = Mathf.Clamp01((resolution * 0.5f - distance) * 2f);
                circleTexture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
            circleTexture.Apply();
        }

        if (titleStyle != null) return;
        titleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 13,
            fontStyle = FontStyle.Bold,
            normal = { textColor = Color.white }
        };
        legendStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 10,
            normal = { textColor = new Color(0.88f, 0.93f, 0.92f, 1f) }
        };
    }

    private void OnDestroy()
    {
        if (pixelTexture != null) Destroy(pixelTexture);
        if (circleTexture != null) Destroy(circleTexture);
    }
}
