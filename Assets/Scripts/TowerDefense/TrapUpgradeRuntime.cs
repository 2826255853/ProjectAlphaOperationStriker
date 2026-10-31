using UnityEngine;

/// <summary>Lazy runtime owner. No menu or prefab generation is required to use paid upgrades.</summary>
public static class TrapUpgradeRuntime
{
    private static EconomyManager wallet;
    private static TrapUpgradeService service;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InitializeAfterSceneLoad()
    {
        GetOrCreate();
    }

    public static TrapUpgradeService GetOrCreate()
    {
        EconomyManager current = EconomyManager.GetOrCreate();
        if (current == wallet && service != null && !service.IsDisposed) return service;
        if (current.UpgradeService != null && !current.UpgradeService.IsDisposed)
        {
            service?.Dispose();
            wallet = current;
            service = current.UpgradeService;
            return service;
        }
        ResetRuntimeState();
        wallet = current;
        service = TrapUpgradeService.ForWallet(current,
            Resources.Load<TrapUpgradeCatalog>(TrapUpgradeCatalog.ResourcePath),
            SentryPhalanxBranchAdapter.IsAvailable);
        return service;
    }

    internal static void ResetRuntimeState()
    {
        service?.Dispose();
        service = null;
        wallet = null;
    }
}
