using System;
using UnityEngine;

/// <summary>
/// Paid trap removal. Dismantling (TrapPlacementGrid.RemoveTrap) intentionally
/// refunds nothing; selling goes through this service and returns part of the
/// purchase price. Default is half price (50%); a smaller or larger ratio — up
/// to a full refund — is the hook the future progression (养成) system uses.
/// </summary>
public static class TrapSaleService
{
    /// <summary>Default salvage rate: half price. TODO(确认): 与养成的最终数值一起定表。</summary>
    public const float DefaultRefundRatio = 0.5f;

    private static float refundRatio = DefaultRefundRatio;

    /// <summary>Current salvage rate in 0..1. 0.5 = half price, 1 = full refund.</summary>
    public static float RefundRatio
    {
        get => refundRatio;
        set => refundRatio = Mathf.Clamp01(value);
    }

    /// <summary>
    /// Progression hook: return a per-trap salvage rate override, or null to use
    /// <see cref="RefundRatio"/>. The 养成系统 can raise this per trap family (or
    /// return 1 for a fully upgraded, fully refundable trap) without any change
    /// to the placement or economy code.
    /// </summary>
    public static Func<TrapDefinition, float?> RefundRatioOverride { get; set; }

    /// <summary>Salvage value of a definition at the currently resolved rate.</summary>
    public static int RefundFor(TrapDefinition definition)
    {
        if (definition == null || definition.Cost <= 0) return 0;
        float ratio = ResolveRatio(definition);
        // Floor keeps salvage at or below the paid price even at ratio 1,
        // so buying and selling in a loop can never mint money.
        return Mathf.Clamp(Mathf.FloorToInt(definition.Cost * ratio), 0, definition.Cost);
    }

    public static int RefundFor(TrapInstance instance) => instance == null ? 0 : RefundFor(instance.Definition);

    /// <summary>Sells at the resolved rate (progression overrides honoured).</summary>
    public static bool TrySell(TrapPlacementGrid grid, TrapInstance instance, out int refund, out string failure)
        => TrySell(grid, instance, null, out refund, out failure);

    /// <summary>
    /// Explicit-rate sale. <paramref name="ratio"/> of null uses the resolved
    /// rate; pass 1f for a full refund.
    /// </summary>
    public static bool TrySell(TrapPlacementGrid grid, TrapInstance instance, float? ratio, out int refund,
        out string failure)
    {
        refund = 0;
        failure = string.Empty;
        if (grid == null || instance == null)
        {
            failure = "没有可出售的陷阱。";
            return false;
        }
        if (!grid.OwnsTrap(instance))
        {
            failure = "该陷阱不属于此放置网格，出售已取消。";
            return false;
        }

        TrapDefinition definition = instance.Definition;
        if (definition == null || definition.Cost < 0)
        {
            failure = "陷阱价格配置无效，出售已取消。";
            return false;
        }

        EconomyManager wallet = EconomyManager.Instance;
        if (wallet == null || !wallet.IsInitialized)
        {
            failure = "经济系统尚未初始化，无法出售陷阱。";
            return false;
        }

        float effective = ratio.HasValue ? Mathf.Clamp01(ratio.Value) : ResolveRatio(definition);
        refund = Mathf.Clamp(Mathf.FloorToInt(definition.Cost * effective), 0, definition.Cost);

        // Grant first, mirroring the purchase reservation/commit ordering: if the
        // wallet rejects the credit (overflow or a run change) nothing is removed.
        try
        {
            wallet.Grant(refund);
        }
        catch (Exception exception)
        {
            refund = 0;
            failure = $"出售结算失败：{exception.Message}";
            return false;
        }

        if (grid.RemoveTrap(instance)) return true;

        // The credit is already committed, so give it back instead of silently
        // minting money when removal fails.
        if (refund > 0 && !wallet.TrySpend(refund))
            Debug.LogWarning($"[TrapSaleService] 陷阱移除失败，且无法撤回 {refund} 金币的回收款。", grid);
        refund = 0;
        failure = "陷阱移除失败，出售已回滚。";
        return false;
    }

    private static float ResolveRatio(TrapDefinition definition)
    {
        Func<TrapDefinition, float?> hook = RefundRatioOverride;
        if (hook != null)
        {
            float? overridden = hook(definition);
            if (overridden.HasValue) return Mathf.Clamp01(overridden.Value);
        }
        return refundRatio;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        // The progression hook belongs to a run; drop stale subscribers so a
        // stopped-play-mode delegate never leaks into the next session.
        RefundRatioOverride = null;
        refundRatio = DefaultRefundRatio;
    }
}
