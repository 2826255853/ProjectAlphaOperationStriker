using UnityEngine;

/// <summary>Short-lived FPS-style indicator showing where the latest damage came from.</summary>
public sealed class PlayerDamageDirectionUI : MonoBehaviour
{
    [SerializeField, Min(0.1f)] private float indicatorLifetime = 0.8f;
    [SerializeField, Min(0f)] private float edgeDistance = 170f;
    [SerializeField, Min(20f)] private float arrowSize = 58f;
    [SerializeField] private Color indicatorColor = new Color(1f, 0.16f, 0.08f, 0.95f);

    private PlayerHealth playerHealth;
    private Camera viewCamera;
    private Vector3 sourcePosition;
    private float indicatorStartedAt = -Mathf.Infinity;
    private GUIStyle arrowStyle;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureUI()
    {
        if (FindAnyObjectByType<PlayerDamageDirectionUI>() != null) return;
        new GameObject("Player Damage Direction UI").AddComponent<PlayerDamageDirectionUI>();
    }

    private void Update()
    {
        if (playerHealth == null)
        {
            SubscribeToPlayer();
        }

        if (viewCamera == null || !viewCamera.isActiveAndEnabled) viewCamera = Camera.main;
    }

    private void SubscribeToPlayer()
    {
        playerHealth = PlayerHealth.Instance;
        if (playerHealth != null) playerHealth.DamagedFrom += HandleDamage;
    }

    private void OnDestroy()
    {
        if (playerHealth != null) playerHealth.DamagedFrom -= HandleDamage;
    }

    private void HandleDamage(PlayerHealth _, Vector3 damageSource)
    {
        sourcePosition = damageSource;
        indicatorStartedAt = Time.unscaledTime;
    }

    private void OnGUI()
    {
        float elapsed = Time.unscaledTime - indicatorStartedAt;
        if (elapsed < 0f || elapsed >= indicatorLifetime || viewCamera == null) return;

        Vector3 fromView = sourcePosition - viewCamera.transform.position;
        fromView.y = 0f;
        if (fromView.sqrMagnitude < 0.0001f) return;
        fromView.Normalize();

        Vector3 cameraForward = viewCamera.transform.forward;
        cameraForward.y = 0f;
        cameraForward.Normalize();
        Vector3 cameraRight = viewCamera.transform.right;
        cameraRight.y = 0f;
        cameraRight.Normalize();
        float angle = Mathf.Atan2(Vector3.Dot(fromView, cameraRight), Vector3.Dot(fromView, cameraForward)) * Mathf.Rad2Deg;

        float fade = 1f - Mathf.Clamp01(elapsed / indicatorLifetime);
        float pulse = 1f + Mathf.Sin(elapsed * 18f) * 0.06f;
        Vector2 center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        Vector2 direction = new Vector2(Mathf.Sin(angle * Mathf.Deg2Rad), -Mathf.Cos(angle * Mathf.Deg2Rad));
        Vector2 position = center + direction * edgeDistance;
        Rect arrowRect = new Rect(position.x - arrowSize * pulse * 0.5f, position.y - arrowSize * pulse * 0.5f, arrowSize * pulse, arrowSize * pulse);

        Color previousColor = GUI.color;
        Matrix4x4 previousMatrix = GUI.matrix;
        GUI.color = new Color(indicatorColor.r, indicatorColor.g, indicatorColor.b, indicatorColor.a * fade);
        GUIUtility.RotateAroundPivot(angle, position);
        if (arrowStyle == null)
        {
            arrowStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold
            };
        }
        arrowStyle.fontSize = Mathf.RoundToInt(arrowSize);
        GUI.Label(arrowRect, "▲", arrowStyle);
        GUI.matrix = previousMatrix;
        GUI.color = previousColor;
    }
}
