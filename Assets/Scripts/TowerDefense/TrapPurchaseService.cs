using System;
using UnityEngine;

/// <summary>The paid entry point for player placement. Authoring uses the grid directly.</summary>
public static class TrapPurchaseService
{
    public static bool TryPurchase(TrapPlacementGrid grid, Vector2Int origin, TrapDefinition definition,
        out TrapInstance instance, out string failure)
    {
        instance = null;
        failure = string.Empty;
        if (grid == null || definition == null)
        {
            failure = "未选择陷阱或放置网格。";
            return false;
        }
        if (!grid.CanPlaceTrap(origin, definition, out failure)) return false;

        EconomyManager wallet = EconomyManager.Instance;
        if (wallet == null || !wallet.IsInitialized)
        {
            failure = "经济系统尚未初始化，无法购买陷阱。";
            return false;
        }
        if (definition.Cost < 0)
        {
            failure = "陷阱价格不能为负数。";
            return false;
        }
        if (!wallet.TryReserve(definition.Cost, out EconomyManager.SpendReservation payment))
        {
            failure = $"金币不足，还差 {Math.Max(0, definition.Cost - wallet.AvailableBalance)}。";
            return false;
        }

        bool committed = false;
        try
        {
            if (!grid.TryPlaceTrap(origin, definition, out instance, out failure)) return false;
            if (!payment.Commit())
            {
                failure = "本局已变更，购买已取消。";
                return false;
            }
            committed = true;
            return true;
        }
        catch (Exception exception)
        {
            failure = $"陷阱创建失败：{exception.Message}";
            return false;
        }
        finally
        {
            payment.Dispose();
            if (!committed && instance != null)
            {
                grid.RemoveTrap(instance);
                instance = null;
            }
        }
    }
}
