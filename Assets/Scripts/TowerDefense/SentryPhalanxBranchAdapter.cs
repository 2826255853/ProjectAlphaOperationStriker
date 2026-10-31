using UnityEngine;

/// <summary>
/// Keeps the sentry TrapInstance alive while switching its weapon and visuals
/// when the level-four Phalanx branch is committed.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("Tower Defense/Sentry Phalanx Branch Adapter")]
public sealed class SentryPhalanxBranchAdapter : MonoBehaviour
{
    public const string BranchId = "phalanx";
    private const string SentryTrapId = "auto_sentry_turret";

    [SerializeField] private GameObject phalanxPrefab;
    [SerializeField] private AutoSentryTurret sentry;
    [SerializeField] private PhalanxAutoCannon cannon;
    [SerializeField] private GameObject phalanxVisual;

    private TrapInstance trap;
    private bool phalanxActive;

    public bool IsPhalanxActive => phalanxActive;
    public PhalanxAutoCannon Cannon => cannon;
    public GameObject PhalanxVisual => phalanxVisual;

    private void Awake()
    {
        ResolveReferences();
        EnsureCannon();
        EnsureVisual();
        Subscribe();
        ApplyState(trap != null ? trap.UpgradeState : TrapUpgradeState.Initial);
    }

    private void OnEnable()
    {
        ResolveReferences();
        Subscribe();
        ApplyState(trap != null ? trap.UpgradeState : TrapUpgradeState.Initial);
    }

    private void Start()
    {
        // AutoSentryTurret builds its authored fallback art in Awake; reapply the
        // branch state after all sibling Awake calls so that art is also hidden on
        // a scene that starts already upgraded.
        ApplyState(trap != null ? trap.UpgradeState : TrapUpgradeState.Initial);
    }

    private void OnDisable() => Unsubscribe();

    private void OnDestroy() => Unsubscribe();

    private void ResolveReferences()
    {
        if (trap == null) trap = GetComponent<TrapInstance>();
        if (sentry == null) sentry = GetComponent<AutoSentryTurret>();
    }

    private void Subscribe()
    {
        if (trap != null) trap.UpgradeStateChanged -= HandleUpgradeStateChanged;
        if (trap != null) trap.UpgradeStateChanged += HandleUpgradeStateChanged;
    }

    private void Unsubscribe()
    {
        if (trap != null) trap.UpgradeStateChanged -= HandleUpgradeStateChanged;
    }

    private void HandleUpgradeStateChanged(TrapInstance _, TrapUpgradeState state) => ApplyState(state);

    private void EnsureCannon()
    {
        if (cannon == null) cannon = GetComponent<PhalanxAutoCannon>();
        if (cannon == null) cannon = gameObject.AddComponent<PhalanxAutoCannon>();
        cannon.enabled = false;
    }

    private void EnsureVisual()
    {
        if (phalanxVisual != null) return;
        if (phalanxPrefab == null)
            phalanxPrefab = Resources.Load<GameObject>("PhalanxVisual");
        if (phalanxPrefab == null)
        {
            TrapDefinition definition = Resources.Load<TrapDefinition>("Phalanx");
            phalanxPrefab = definition != null ? definition.Prefab : null;
        }
        if (phalanxPrefab == null) return;

        phalanxVisual = Instantiate(phalanxPrefab, transform);
        phalanxVisual.name = "Phalanx Branch Visual";
        phalanxVisual.transform.localPosition = Vector3.zero;
        phalanxVisual.transform.localRotation = Quaternion.identity;
        phalanxVisual.transform.localScale = Vector3.one;

        TrapInstance nestedTrap = phalanxVisual.GetComponent<TrapInstance>();
        if (nestedTrap != null)
        {
            // Disable first so the visual prefab does not remain in TrapInstance.ActiveTraps
            // until Unity processes the deferred Destroy call.
            nestedTrap.enabled = false;
            Destroy(nestedTrap);
        }
        foreach (Collider collider in phalanxVisual.GetComponentsInChildren<Collider>(true))
            Destroy(collider);
        phalanxVisual.SetActive(false);
    }

    private void ApplyState(TrapUpgradeState state)
    {
        bool next = state.Level == 4 && state.Direction == TrapUpgradeDirection.Branch &&
            string.Equals(state.BranchId, BranchId, System.StringComparison.Ordinal);
        phalanxActive = next;
        if (sentry != null)
        {
            sentry.enabled = !next;
            sentry.SetVisualsEnabled(!next);
        }
        if (cannon != null) cannon.enabled = next;
        if (phalanxVisual != null) phalanxVisual.SetActive(next);
    }

    public static bool IsAvailable(string trapId, string branchId) =>
        string.Equals(trapId, SentryTrapId, System.StringComparison.Ordinal) &&
        string.Equals(branchId, BranchId, System.StringComparison.Ordinal);
}
