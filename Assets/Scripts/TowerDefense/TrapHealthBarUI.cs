using UnityEngine;

/// <summary>
/// Trap inspection HUD. While the player aims the view at a placed trap, the
/// trap's health bar is drawn over that trap, labelled with the trap name and
/// its current / maximum health.
///
/// The component bootstraps itself into every scene (see CreateForScene) and
/// draws with IMGUI, so no canvas, prefab or scene wiring is required. Health
/// comes from <see cref="TrapInstance"/>, which is the same source the ground
/// enemies use when they damage a trap.
/// </summary>
public sealed class TrapHealthBarUI : MonoBehaviour
{
    private const float RayDistance = 500f;
    // Collider-free trap prefabs (generated turret models) are resolved by
    // picking the trap nearest to the aim ray, mirroring the E-key dismantle
    // targeting in TrapPlacementController.
    private const float AimConeRadius = 0.35f;
    private const float ScreenEdgePadding = 8f;

    [Header("Visibility")]
    [Tooltip("视线离开陷阱后血条继续显示的秒数，避免准星轻微抖动导致闪烁。")]
    [SerializeField, Min(0f)] private float lingerAfterAimLost = 0.6f;

    [Header("Layout")]
    [SerializeField] private Vector2 panelSize = new Vector2(280f, 58f);
    [Tooltip("血条相对陷阱中心的抬升高度，用来把面板放到模型上方。")]
    [SerializeField, Min(0f)] private float anchorHeight = 1.7f;
    [SerializeField, Min(6f)] private float barHeight = 14f;

    [Header("Colors")]
    [SerializeField] private Color panelColor = new Color(0f, 0f, 0f, 0.78f);
    [SerializeField] private Color trackColor = new Color(0.1f, 0.1f, 0.12f, 0.85f);
    [SerializeField] private Color fillColor = new Color(0.35f, 0.9f, 0.35f, 1f);
    [SerializeField] private Color lowFillColor = new Color(0.95f, 0.3f, 0.2f, 1f);
    [SerializeField, Range(0f, 1f)] private float lowHealthRatio = 0.3f;

    private Camera viewCamera;
    private TrapInstance aimedTrap;
    private TrapInstance shownTrap;
    private float hideAtTime;
    private float displayedHealth = 1f;
    private float cachedAnchorHeight;
    private bool anchorVisible;
    private Vector2 anchorScreenPoint;
    private GUIStyle nameStyle;
    private GUIStyle valueStyle;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CreateForScene()
    {
        if (FindAnyObjectByType<TrapHealthBarUI>() != null) return;
        var hudObject = new GameObject("Trap Health Bar UI");
        DontDestroyOnLoad(hudObject);
        hudObject.AddComponent<TrapHealthBarUI>();
    }

    private void Update()
    {
        // Re-resolve the camera if it was destroyed or a scene swap changed it.
        if (viewCamera == null || !viewCamera.isActiveAndEnabled) viewCamera = Camera.main;
        // The trap selection menu owns the cursor, so no aiming happens there.
        aimedTrap = viewCamera == null || TrapSelectionMenu.CursorOwned ? null : FindTrapAtCrosshair();

        if (aimedTrap != null) showTrap(aimedTrap);
        else if (shownTrap != null && Time.unscaledTime >= hideAtTime) shownTrap = null;

        UpdateAnchor();
    }

    private void showTrap(TrapInstance trap)
    {
        bool switched = trap != shownTrap;
        shownTrap = trap;
        hideAtTime = Time.unscaledTime + lingerAfterAimLost;
        float target = trap.MaxHealth <= 0f ? 0f : Mathf.Clamp01(trap.CurrentHealth / trap.MaxHealth);
        // Snap when the aim moves onto another trap, animate otherwise so a hit
        // reads as the bar sliding down.
        displayedHealth = switched ? target : Mathf.MoveTowards(displayedHealth, target, Time.unscaledDeltaTime * 2.5f);
        // Renderer bounds are stable per trap, so measuring them once is enough.
        if (switched) cachedAnchorHeight = MeasureAnchorHeight(trap);
    }

    private TrapInstance FindTrapAtCrosshair()
    {
        Ray ray = viewCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        // Anything solid in front of the crosshair occludes the bar. A hit that
        // resolves to a trap wins immediately; a hit without a trap only caps
        // how far the collider-free fallback below is allowed to reach.
        float occludedAt = float.PositiveInfinity;
        if (Physics.Raycast(ray, out RaycastHit hit, RayDistance, ~0, QueryTriggerInteraction.Ignore))
        {
            TrapInstance hitTrap = hit.collider.GetComponentInParent<TrapInstance>();
            if (hitTrap != null) return hitTrap;
            occludedAt = hit.distance;
        }
        return FindTrapNearRay(ray, occludedAt);
    }

    /// <summary>
    /// Picks the trap whose origin sits closest to the ray, ignoring traps
    /// behind the origin or further away than <paramref name="maxAlongRay"/>.
    /// Extracted so the crosshair targeting rule is testable without a camera.
    /// </summary>
    public static TrapInstance FindTrapNearRay(Ray ray, float maxAlongRay)
    {
        TrapInstance closest = null;
        float closestDistance = AimConeRadius;
        foreach (TrapInstance trap in TrapInstance.ActiveTraps)
        {
            if (trap == null) continue;
            Vector3 toTrap = trap.transform.position - ray.origin;
            float alongRay = Vector3.Dot(toTrap, ray.direction);
            if (alongRay < 0f || alongRay > maxAlongRay) continue;
            float distance = Vector3.Cross(ray.direction, toTrap).magnitude;
            if (distance < closestDistance) { closestDistance = distance; closest = trap; }
        }
        return closest;
    }

    /// <summary>Projects the shown trap into screen space so the bar tracks it.</summary>
    private void UpdateAnchor()
    {
        anchorVisible = false;
        TrapInstance trap = shownTrap;
        if (trap == null || viewCamera == null) return;
        Vector3 worldPoint = trap.transform.position + Vector3.up * cachedAnchorHeight;
        Vector3 screenPoint = viewCamera.WorldToScreenPoint(worldPoint);
        if (screenPoint.z <= 0f) return; // Behind the camera.
        anchorVisible = true;
        // GUI space has its origin at the top-left, screen space at the bottom-left.
        anchorScreenPoint = new Vector2(screenPoint.x, Screen.height - screenPoint.y);
    }

    private void OnGUI()
    {
        TrapInstance trap = shownTrap;
        if (trap == null || !anchorVisible || trap.MaxHealth <= 0f) return;
        EnsureStyles();

        float panelWidth = Mathf.Min(panelSize.x, Screen.width - ScreenEdgePadding * 2f);
        float panelHeight = Mathf.Max(panelSize.y, barHeight + 28f);
        Rect panel = new Rect(
            Mathf.Clamp(anchorScreenPoint.x - panelWidth * 0.5f, ScreenEdgePadding, Mathf.Max(ScreenEdgePadding, Screen.width - panelWidth - ScreenEdgePadding)),
            Mathf.Clamp(anchorScreenPoint.y - panelHeight, ScreenEdgePadding, Mathf.Max(ScreenEdgePadding, Screen.height - panelHeight - ScreenEdgePadding)),
            panelWidth,
            panelHeight);

        GUI.color = panelColor;
        GUI.DrawTexture(panel, Texture2D.whiteTexture);
        GUI.color = Color.white;

        string label = trap.Definition != null ? trap.Definition.DisplayName : trap.gameObject.name;
        Rect titleRect = new Rect(panel.x + 10f, panel.y + 5f, panel.width - 20f, 20f);
        GUI.Label(titleRect, label, nameStyle);
        GUI.Label(titleRect, $"{trap.CurrentHealth:0} / {trap.MaxHealth:0}", valueStyle);

        Rect bar = new Rect(panel.x + 10f, panel.yMax - barHeight - 8f, panel.width - 20f, barHeight);
        GUI.color = trackColor;
        GUI.DrawTexture(bar, Texture2D.whiteTexture);
        GUI.color = displayedHealth <= lowHealthRatio ? lowFillColor : fillColor;
        GUI.DrawTexture(new Rect(bar.x + 1f, bar.y + 1f, (bar.width - 2f) * displayedHealth, bar.height - 2f), Texture2D.whiteTexture);
        GUI.color = Color.white;
    }

    /// <summary>
    /// Height above the trap origin where the bar is anchored. Trap models differ
    /// in scale, so the renderer bounds are used when available and
    /// <see cref="anchorHeight"/> is only the fallback for collider-free ghosts.
    /// </summary>
    private float MeasureAnchorHeight(TrapInstance trap)
    {
        Renderer[] renderers = trap.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return anchorHeight;
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        float height = bounds.max.y - trap.transform.position.y + 0.25f;
        return Mathf.Max(anchorHeight * 0.5f, height);
    }

    private void EnsureStyles()
    {
        if (nameStyle != null) return;
        nameStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 14,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.UpperLeft
        };
        nameStyle.normal.textColor = Color.white;
        valueStyle = new GUIStyle(nameStyle)
        {
            fontStyle = FontStyle.Normal,
            alignment = TextAnchor.UpperRight
        };
        valueStyle.normal.textColor = new Color(0.85f, 0.9f, 0.95f, 1f);
    }
}
