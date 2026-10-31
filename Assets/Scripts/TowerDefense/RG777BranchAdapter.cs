using UnityEngine;

/// <summary>Activates the RG777 visual and XM777 weapon tuning for the integrated level-four branch.</summary>
[DisallowMultipleComponent]
[AddComponentMenu("Tower Defense/RG777 Branch Adapter")]
public sealed class RG777BranchAdapter : MonoBehaviour
{
    public const string BranchId = XM777Trap.RG777BranchId;
    public const string TrapId = "xm777";
    private const string VisualResourcePath = "RG777Visual";

    [SerializeField] private GameObject visualPrefab;
    [SerializeField] private XM777Trap weapon;
    [SerializeField] private GameObject rg777Visual;

    private TrapInstance trap;
    private Renderer[] xm777Renderers;
    private bool[] authoredRendererStates;
    private bool branchActive;
    private bool hasAppliedState;
    private Transform rgYawPivot;
    private Transform rgPitchPivot;
    private Transform rgMuzzle;
    private Quaternion rgYawRestRotation;
    private Quaternion rgPitchRestRotation;
    private bool rgRestCaptured;

    public bool IsBranchActive => branchActive;
    public GameObject RG777Visual => rg777Visual;

    private void Awake()
    {
        ResolveReferences();
        CaptureXM777Visual();
        ApplyState(trap != null ? trap.UpgradeState : TrapUpgradeState.Initial);
    }

    private void OnEnable()
    {
        ResolveReferences();
        if (trap != null)
        {
            trap.UpgradeStateChanged -= HandleUpgradeStateChanged;
            trap.UpgradeStateChanged += HandleUpgradeStateChanged;
        }
        ApplyState(trap != null ? trap.UpgradeState : TrapUpgradeState.Initial);
    }

    private void Start()
    {
        // TrapInstance synchronizes its shared upgrade state in Awake.
        ApplyState(trap != null ? trap.UpgradeState : TrapUpgradeState.Initial);
    }

    private void OnDisable()
    {
        if (trap != null) trap.UpgradeStateChanged -= HandleUpgradeStateChanged;
    }

    private void OnDestroy()
    {
        if (trap != null) trap.UpgradeStateChanged -= HandleUpgradeStateChanged;
    }

    private void ResolveReferences()
    {
        if (trap == null) trap = GetComponent<TrapInstance>();
        if (weapon == null) weapon = GetComponent<XM777Trap>();
    }

    private void CaptureXM777Visual()
    {
        if (xm777Renderers != null) return;
        xm777Renderers = weapon != null
            ? weapon.GetComponentsInChildren<Renderer>(true)
            : GetComponentsInChildren<Renderer>(true);
        authoredRendererStates = new bool[xm777Renderers.Length];
        for (int i = 0; i < xm777Renderers.Length; i++)
            authoredRendererStates[i] = xm777Renderers[i] != null && xm777Renderers[i].enabled;
    }

    private void EnsureRG777Visual()
    {
        if (rg777Visual != null) return;
        if (visualPrefab == null) visualPrefab = Resources.Load<GameObject>(VisualResourcePath);
        if (visualPrefab == null)
        {
            Debug.LogError("RG777 分支视觉预制体缺失：Resources/" + VisualResourcePath, this);
            return;
        }

        rg777Visual = Instantiate(visualPrefab, transform);
        rg777Visual.name = "RG777 Branch Visual";
        rg777Visual.transform.localPosition = Vector3.zero;
        rg777Visual.transform.localRotation = Quaternion.identity;
        rg777Visual.SetActive(false);
    }

    private void HandleUpgradeStateChanged(TrapInstance _, TrapUpgradeState state) => ApplyState(state);

    private void ApplyState(TrapUpgradeState state)
    {
        bool next = state.Level == 4 && state.Direction == TrapUpgradeDirection.Branch
            && string.Equals(state.BranchId, BranchId, System.StringComparison.Ordinal);
        if (hasAppliedState && next == branchActive) return;
        hasAppliedState = true;
        branchActive = next;
        CaptureXM777Visual();

        if (!next)
        {
            if (weapon != null) weapon.RestoreAuthoredAimRig();
            SetXM777Renderers(true);
            if (rg777Visual != null) rg777Visual.SetActive(false);
            return;
        }

        EnsureRG777Visual();
        if (rg777Visual == null || weapon == null) return;
        if (!ResolveRG777Rig())
        {
            Debug.LogError("RG777 分支模型缺少 Yaw Pivot / XM777_Pitch_Root / Muzzle。", rg777Visual);
            return;
        }

        rgYawPivot.rotation = rgYawRestRotation;
        rgPitchPivot.localRotation = rgPitchRestRotation;
        rg777Visual.SetActive(true);
        weapon.ConfigureAimRig(rgYawPivot, rgPitchPivot, rgMuzzle);
        SetXM777Renderers(false);
    }

    private bool ResolveRG777Rig()
    {
        if (rgYawPivot == null) rgYawPivot = FindDeepChild(rg777Visual.transform, "Yaw Pivot");
        if (rgPitchPivot == null) rgPitchPivot = FindDeepChild(rg777Visual.transform, "XM777_Pitch_Root");
        if (rgMuzzle == null) rgMuzzle = FindDeepChild(rg777Visual.transform, "Muzzle");
        if (rgYawPivot == null || rgPitchPivot == null || rgMuzzle == null) return false;
        if (!rgRestCaptured)
        {
            rgYawRestRotation = rgYawPivot.rotation;
            rgPitchRestRotation = rgPitchPivot.localRotation;
            rgRestCaptured = true;
        }
        return true;
    }

    private void SetXM777Renderers(bool visible)
    {
        if (xm777Renderers == null) return;
        for (int i = 0; i < xm777Renderers.Length; i++)
            if (xm777Renderers[i] != null)
                xm777Renderers[i].enabled = visible && authoredRendererStates[i];
    }

    private static Transform FindDeepChild(Transform root, string childName)
    {
        if (root == null) return null;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            if (child.name == childName) return child;
            Transform nested = FindDeepChild(child, childName);
            if (nested != null) return nested;
        }
        return null;
    }

    public static bool IsAvailable(string trapId, string branchId) =>
        string.Equals(trapId, TrapId, System.StringComparison.Ordinal)
        && string.Equals(branchId, BranchId, System.StringComparison.Ordinal)
        && Resources.Load<GameObject>(VisualResourcePath) != null;
}
