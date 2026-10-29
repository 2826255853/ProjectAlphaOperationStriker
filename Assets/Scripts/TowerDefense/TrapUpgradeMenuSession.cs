using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>Only a pending quote and dialog navigation; levels and money belong to the paid service.</summary>
public sealed class TrapUpgradeMenuSession
{
    public enum DialogStep { Closed, Confirm, Direction, Branch, ConfirmNormal, Result }
    private readonly TrapUpgradeService service;
    public TrapDefinition Definition { get; private set; }
    public TrapUpgradeQuote Quote { get; private set; }
    public DialogStep Step { get; private set; }
    public string Feedback { get; private set; } = string.Empty;
    public bool IsOpen => Step != DialogStep.Closed;
    public IReadOnlyList<TrapUpgradeCatalog.Branch> Branches =>
        Definition == null ? Array.Empty<TrapUpgradeCatalog.Branch>() : service.GetBranches(Definition.TrapId);
    public TrapUpgradeCatalog.Branch SelectedBranch { get; private set; }
    public bool SelectedBranchAvailable => SelectedBranch != null && Quote != null && Quote.CanUpgrade;
    public string SelectedBranchUnavailableReason => SelectedBranch == null || Quote == null ? string.Empty : Quote.Message;

    public TrapUpgradeMenuSession(TrapUpgradeService service)
        => this.service = service ?? throw new ArgumentNullException(nameof(service));

    public TrapUpgradeQuote QuoteFor(TrapDefinition definition)
        => service.GetQuote(definition, definition != null && TrapUpgradeCatalog.IsValidId(definition.TrapId)
            && service.GetState(definition.TrapId).Level == 3 ? TrapUpgradeDirection.Normal : TrapUpgradeDirection.None);

    public TrapUpgradeQuote QuoteForNormalDirection() =>
        Definition == null ? null : service.GetQuote(Definition, TrapUpgradeDirection.Normal);

    public TrapUpgradeQuote QuoteForBranch(string branchId) =>
        Definition == null ? null : service.GetQuote(Definition, TrapUpgradeDirection.Branch, branchId);

    public void Open(TrapDefinition definition)
    {
        SelectedBranch = null;
        Definition = definition;
        Quote = QuoteFor(definition);
        Feedback = Quote.CanUpgrade ? string.Empty : Quote.Message;
        Step = DialogStep.Confirm;
    }

    public void Cancel()
    {
        Step = DialogStep.Closed;
        Definition = null;
        Quote = null;
        SelectedBranch = null;
        Feedback = string.Empty;
    }

    public void Back()
    {
        if (Step == DialogStep.ConfirmNormal) Step = DialogStep.Direction;
        else if (Step == DialogStep.Branch)
        {
            SelectedBranch = null;
            Quote = QuoteForNormalDirection();
            Feedback = string.Empty;
            Step = DialogStep.Direction;
        }
        else if (Step == DialogStep.Direction) { Quote = QuoteFor(Definition); Step = DialogStep.Confirm; }
        else Cancel();
    }

    public void SelectBranch(string branchId)
    {
        if (Step != DialogStep.Direction || string.IsNullOrWhiteSpace(branchId)) return;
        SelectedBranch = null;
        foreach (TrapUpgradeCatalog.Branch branch in Branches)
            if (string.Equals(branch.branchId, branchId, StringComparison.Ordinal)) { SelectedBranch = branch; break; }
        if (SelectedBranch == null) return;
        Quote = QuoteForBranch(branchId);
        Step = DialogStep.Branch;
        Feedback = Quote.CanUpgrade ? string.Empty : SelectedBranchUnavailableReason;
    }

    public void ChooseNormal()
    {
        if (Step != DialogStep.Direction) return;
        Quote = QuoteForNormalDirection();
        Feedback = Quote.CanUpgrade ? string.Empty : Quote.Message;
        Step = DialogStep.ConfirmNormal;
    }

    // The view submits identity/version only. Never accept a price or multiplier from a UI control.
    public bool Confirm(string trapId, int expectedLevel, long quoteVersion)
    {
        if (Quote == null || (Step != DialogStep.Confirm && Step != DialogStep.ConfirmNormal && Step != DialogStep.Branch)) return false;
        if (Quote.TrapId != trapId || Quote.Current.Level != expectedLevel || Quote.Version != quoteVersion) return false;
        if (!Quote.CanUpgrade) { Feedback = Quote.Message; return false; }
        if (Step == DialogStep.Confirm && Quote.Current.Level == 3)
        {
            Step = DialogStep.Direction;
            return false;
        }
        if (service.TryUpgrade(Quote, out TrapUpgradeFailure failure))
        {
            Feedback = $"升级成功：{Definition.DisplayName} · 等级 {Quote.Target.Level}";
            Step = DialogStep.Result;
            return true;
        }
        Feedback = FailureText(failure);
        // Replace a rejected snapshot only after the failure is shown, requiring a fresh confirmation.
        Quote = QuoteFor(Definition);
        if (Quote.Current.Level == 3)
        {
            Step = DialogStep.Direction;
            SelectedBranch = null;
        }
        return false;
    }

    public static string FailureText(TrapUpgradeFailure failure)
    {
        switch (failure)
        {
            case TrapUpgradeFailure.InsufficientFunds: return "金币不足，未扣款。";
            case TrapUpgradeFailure.StaleQuote: return "报价已过期，已刷新，请重新确认。";
            case TrapUpgradeFailure.MaxLevel: return "已满级。";
            case TrapUpgradeFailure.Busy: return "升级处理中，请稍后重试。";
            case TrapUpgradeFailure.RunUnavailable: return "本局尚未就绪。";
            case TrapUpgradeFailure.BranchUnavailable: return "该方向尚未接入。";
            default: return $"升级失败（{failure}），未扣款。";
        }
    }

    /// <summary>Read the actual authored level-one prefab values without instantiating combat components.</summary>
    public static string AttributePreview(TrapDefinition definition, TrapUpgradeQuote quote)
    {
        if (definition == null || definition.Prefab == null || quote == null) return "属性预览不可用";
        GameObject prefab = definition.Prefab;
        var text = new StringBuilder();
        void Row(string name, float value, float current, float target)
            => text.AppendLine($"{name}：{value * current:0.##} → {value * target:0.##}");
        void Damage(string name, float value) => Row(name, value, quote.CurrentMultipliers.Damage, quote.TargetMultipliers.Damage);
        if (prefab.TryGetComponent(out RepairStationTrap repair))
            Row("每次维修量", repair.BaseRepairAmount, quote.CurrentMultipliers.Repair, quote.TargetMultipliers.Repair);
        else if (prefab.TryGetComponent(out ThroatLampTrap lamp))
        { Damage("直击伤害", lamp.BaseDirectDamage); Damage("溅射伤害", lamp.BaseSplashDamage); }
        else if (prefab.TryGetComponent(out Betty7JumpMineTrap betty)) Damage("爆炸伤害", betty.BaseDamage);
        else if (prefab.TryGetComponent(out AutoSentryTurret sentry)) Damage("每发伤害", sentry.BaseDamagePerShot);
        else if (prefab.TryGetComponent(out GroundSpikeTrap spike)) Damage("刺击伤害", spike.BaseDamage);
        else if (prefab.TryGetComponent(out MissileLauncherWeapon missile))
        { Damage("导弹直击", missile.DamagePerMissile); Damage("溅射伤害", missile.DamagePerMissile * missile.SplashDamageRatio); }
        else if (prefab.TryGetComponent(out FlamethrowerTrap fire))
        { Damage("直击伤害", fire.BaseDirectDamage); Damage("燃烧每秒", fire.BaseBurnDps); }
        float health = prefab.TryGetComponent(out TrapInstance trap) ? trap.BaseMaxHealth : TrapInstance.DefaultBaseMaxHealth;
        Row("最大血量", health, quote.CurrentMultipliers.MaxHealth, quote.TargetMultipliers.MaxHealth);
        return text.ToString().TrimEnd();
    }
}
