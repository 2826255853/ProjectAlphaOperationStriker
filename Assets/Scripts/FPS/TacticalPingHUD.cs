using UnityEngine;
using UnityEngine.InputSystem;
using TowerDefense;

[DisallowMultipleComponent]
[DefaultExecutionOrder(1020)]
public sealed class TacticalPingHUD : MonoBehaviour
{
    // TODO(确认): Keep one ping per source for 20 seconds by default.
    private const float PingLifetime = 20f;
    private static readonly Color PlayerColor = new Color32(255, 219, 66, 255);
    private static readonly Color DroneColor = new Color32(74, 181, 255, 255);
    private static readonly Color OutlineColor = new Color(0f, 0f, 0f, 0.9f);

    private struct Ping
    {
        public Vector3 Position;
        public float ExpiresAt;

        public bool IsVisible => Time.time < ExpiresAt;
    }

    private Ping playerPing;
    private Ping dronePing;
    private Camera playerCamera;
    private PlayerHealth playerHealth;
    private PlayerObserverMode observerMode;
    private Camera viewCamera;
    private Transform droneRoot;
    private bool isDroneView;
    private GUIStyle distanceStyle;
#if ENABLE_DRONE_CODE
    private DroneControlSession droneSession;
#endif

    private void Awake()
    {
        playerCamera = GetComponentInChildren<Camera>(true);
        playerHealth = GetComponent<PlayerHealth>();
        observerMode = GetComponent<PlayerObserverMode>();
    }

    private void LateUpdate()
    {
        viewCamera = playerCamera;
        droneRoot = null;
        isDroneView = false;
#if ENABLE_DRONE_CODE
        if (droneSession == null) TryGetComponent(out droneSession);
        if (droneSession != null)
        {
            droneRoot = droneSession.DroneTransform;
            isDroneView = droneSession.IsDroneControlled && droneRoot != null;
            if (isDroneView) viewCamera = droneSession.ControlCamera;
        }
#endif

        if (!CanUseHUD() || Time.timeScale <= 0f || Keyboard.current?.zKey.wasPressedThisFrame != true)
            return;

        if (!TryGetMapPoint(out Vector3 point)) return;

        Ping ping = new Ping { Position = point, ExpiresAt = Time.time + PingLifetime };
        if (isDroneView) dronePing = ping;
        else playerPing = ping;
    }

    private bool CanUseHUD()
    {
        return viewCamera != null && viewCamera.isActiveAndEnabled
            && (playerHealth == null || !playerHealth.IsDead)
            && (isDroneView || observerMode == null || !observerMode.IsActive)
            && !GameStartMenu.BlocksPlayerCursor && !TrapSelectionMenu.CursorOwned;
    }

    private bool TryGetMapPoint(out Vector3 point)
    {
        Ray ray = viewCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f));
        RaycastHit[] hits = Physics.RaycastAll(ray, viewCamera.farClipPlane,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        float closestDistance = float.PositiveInfinity;
        point = default;
        foreach (RaycastHit hit in hits)
        {
            if (hit.distance >= closestDistance) continue;

            Collider collider = hit.collider;
            Transform target = collider.transform;
            if (target.IsChildOf(transform) || (droneRoot != null && target.IsChildOf(droneRoot))
                || collider.GetComponentInParent<EnemyHealth>() != null
                || collider.GetComponentInParent<TrapInstance>() != null) continue;

            closestDistance = hit.distance;
            point = hit.point;
        }

        return closestDistance < float.PositiveInfinity;
    }

    private void OnGUI()
    {
        if (Event.current.type != EventType.Repaint || !CanUseHUD()) return;

        float scale = Mathf.Max(0.5f, Screen.height / 1080f);
        Rect viewport = viewCamera.pixelRect;
        Vector2 droneReticle = new Vector2(viewport.center.x, Screen.height - viewport.center.y);
        Vector3 origin = isDroneView ? droneRoot.position : transform.position;
        bool showPlayerPing = TryProject(playerPing, out Vector2 playerPoint);
        bool showDronePing = TryProject(dronePing, out Vector2 dronePoint);

        Color previousColor = GUI.color;
        if (showPlayerPing && showDronePing
            && Mathf.Abs(playerPoint.x - dronePoint.x) < 54f * scale
            && Mathf.Abs(playerPoint.y - dronePoint.y) < 40f * scale)
        {
            // Separate the labels when both pings mark the same screen location.
            float center = Mathf.Clamp((playerPoint.x + dronePoint.x) * 0.5f,
                viewport.xMin + 63f * scale, viewport.xMax - 63f * scale);
            float separation = playerPoint.x <= dronePoint.x ? 29f * scale : -29f * scale;
            playerPoint.x = center - separation;
            dronePoint.x = center + separation;
        }

        if (showPlayerPing) DrawPing(playerPoint, Vector3.Distance(origin, playerPing.Position), PlayerColor, scale);
        if (showDronePing) DrawPing(dronePoint, Vector3.Distance(origin, dronePing.Position), DroneColor, scale);
        if (isDroneView) DrawDroneReticle(droneReticle, scale);
        GUI.color = previousColor;
    }

    private bool TryProject(Ping ping, out Vector2 point)
    {
        point = default;
        if (!ping.IsVisible) return false;

        Vector3 screen = viewCamera.WorldToScreenPoint(ping.Position);
        if (screen.z <= 0f || !viewCamera.pixelRect.Contains(screen)) return false;

        point = new Vector2(screen.x, Screen.height - screen.y);
        return true;
    }

    private void DrawPing(Vector2 point, float meters, Color color, float scale)
    {
        Matrix4x4 previousMatrix = GUI.matrix;
        GUIUtility.RotateAroundPivot(45f, point);
        float radius = 9f * scale;
        float length = radius * 2f;
        float line = 2f * scale;
        DrawOutlinedRect(new Rect(point.x - radius, point.y - radius, length, line), color, scale);
        DrawOutlinedRect(new Rect(point.x - radius, point.y + radius - line, length, line), color, scale);
        DrawOutlinedRect(new Rect(point.x - radius, point.y - radius, line, length), color, scale);
        DrawOutlinedRect(new Rect(point.x + radius - line, point.y - radius, line, length), color, scale);
        GUI.matrix = previousMatrix;

        distanceStyle ??= new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold
        };
        distanceStyle.fontSize = Mathf.RoundToInt(18f * scale);
        string label = $"{Mathf.RoundToInt(meters)} m";
        Rect rect = new Rect(point.x - 32f * scale, point.y + 13f * scale, 64f * scale, 25f * scale);
        GUI.color = Color.white;
        GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f,
            OutlineColor, 0f, 4f * scale);
        distanceStyle.normal.textColor = color;
        GUI.Label(rect, label, distanceStyle);
    }

    private static void DrawDroneReticle(Vector2 center, float scale)
    {
        float gap = 4f * scale;
        float length = 6f * scale;
        float line = 2f * scale;
        DrawOutlinedRect(new Rect(center.x - gap - length, center.y - line * 0.5f, length, line), Color.white, scale);
        DrawOutlinedRect(new Rect(center.x + gap, center.y - line * 0.5f, length, line), Color.white, scale);
        DrawOutlinedRect(new Rect(center.x - line * 0.5f, center.y - gap - length, line, length), Color.white, scale);
        DrawOutlinedRect(new Rect(center.x - line * 0.5f, center.y + gap, line, length), Color.white, scale);
    }

    private static void DrawOutlinedRect(Rect rect, Color color, float scale)
    {
        GUI.color = OutlineColor;
        GUI.DrawTexture(new Rect(rect.x - scale, rect.y - scale, rect.width + 2f * scale, rect.height + 2f * scale),
            Texture2D.whiteTexture);
        GUI.color = color;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
    }
}
