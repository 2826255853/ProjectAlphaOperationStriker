using System;
using System.Collections.Generic;
using UnityEngine;

public enum TrapUpgradeFailure
{
    None,
    InvalidDefinition,
    RunUnavailable,
    InvalidConfiguration,
    ConflictingDefinition,
    MaxLevel,
    InvalidDirection,
    BranchUnavailable,
    StaleQuote,
    Busy,
    InsufficientFunds,
    ReservationUnavailable,
    ServiceDisposed
}

/// <summary>An immutable snapshot; the originating service revalidates it before changing state.</summary>
public sealed class TrapUpgradeQuote
{
    internal TrapUpgradeService Owner { get; }
    internal TrapDefinition Definition { get; }
    internal int BaseCost { get; }
    public string TrapId { get; }
    public Guid RunId { get; }
    public TrapUpgradeState Current { get; }
    public TrapUpgradeState Target { get; }
    public TrapUpgradeMultipliers CurrentMultipliers { get; }
    public TrapUpgradeMultipliers TargetMultipliers { get; }
    public int Cost { get; }
    public long Version { get; }
    public TrapUpgradeFailure Failure { get; }
    public string Message { get; }
    public bool CanUpgrade => Failure == TrapUpgradeFailure.None;

    internal TrapUpgradeQuote(TrapUpgradeService owner, TrapDefinition definition, Guid runId,
        TrapUpgradeState current, TrapUpgradeState target, TrapUpgradeMultipliers currentMultipliers,
        TrapUpgradeMultipliers targetMultipliers, int cost, long version,
        TrapUpgradeFailure failure = TrapUpgradeFailure.None, string message = "")
    {
        Owner = owner;
        Definition = definition;
        BaseCost = definition != null ? definition.Cost : 0;
        TrapId = definition != null ? definition.TrapId : string.Empty;
        RunId = runId;
        Current = current;
        Target = target;
        CurrentMultipliers = currentMultipliers;
        TargetMultipliers = targetMultipliers;
        Cost = cost;
        Version = version;
        Failure = failure;
        Message = message;
    }
}

/// <summary>
/// Per-run upgrade rules. Use ForWallet/TrapUpgradeRuntime for paid gameplay.
/// The Func-based constructor remains an isolated, payment-free rule/test harness.
/// </summary>
public sealed class TrapUpgradeService : IDisposable
{
    private readonly Func<Guid> getRunId;
    private readonly TrapUpgradeCatalog catalog;
    private readonly Func<string, string, bool> branchEffectAvailable;
    private readonly Dictionary<string, TrapUpgradeState> states = new Dictionary<string, TrapUpgradeState>(StringComparer.Ordinal);
    private readonly Dictionary<string, long> versions = new Dictionary<string, long>(StringComparer.Ordinal);
    private readonly Dictionary<string, int> basePrices = new Dictionary<string, int>(StringComparer.Ordinal);
    private Guid runId;
    private bool upgrading;
    private readonly EconomyManager wallet;
    private readonly HashSet<UpgradeReservation> reservations = new HashSet<UpgradeReservation>();
    public bool IsDisposed { get; private set; }

    public event Action<string, TrapUpgradeState> Upgraded;
    /// <summary>Instances have been refreshed to level one before this UI-facing notification.</summary>
    public event Action<Guid> RunReset;

    public TrapUpgradeService(Func<Guid> getRunId, TrapUpgradeCatalog catalog = null,
        Func<string, string, bool> branchEffectAvailable = null)
    {
        this.getRunId = getRunId ?? throw new ArgumentNullException(nameof(getRunId));
        this.catalog = catalog;
        this.branchEffectAvailable = branchEffectAvailable;
    }

    private TrapUpgradeService(EconomyManager wallet, TrapUpgradeCatalog catalog,
        Func<string, string, bool> branchEffectAvailable) : this(() => wallet.RunId, catalog, branchEffectAvailable)
    {
        this.wallet = wallet;
        runId = wallet.RunId;
        wallet.RunStarted += HandleRunStarted;
        wallet.WalletDestroyed += Dispose;
    }

    /// <summary>One paid state owner per wallet; never create independent paid levels for the same run.</summary>
    public static TrapUpgradeService ForWallet(EconomyManager wallet, TrapUpgradeCatalog catalog = null,
        Func<string, string, bool> branchEffectAvailable = null)
    {
        if (wallet == null) throw new ArgumentNullException(nameof(wallet));
        if (wallet.UpgradeService != null && !wallet.UpgradeService.IsDisposed)
        {
            if (wallet.UpgradeService.catalog != catalog || wallet.UpgradeService.branchEffectAvailable != branchEffectAvailable)
                throw new InvalidOperationException("This wallet already owns an upgrade service with different configuration.");
            return wallet.UpgradeService;
        }
        return wallet.UpgradeService = new TrapUpgradeService(wallet, catalog, branchEffectAvailable);
    }

    private void SynchronizeRun()
    {
        if (IsDisposed) return;
        Guid current = getRunId();
        if (current == runId) return;
        runId = current;
        states.Clear();
        versions.Clear();
        basePrices.Clear();
    }

    private void HandleRunStarted(Guid current)
    {
        if (IsDisposed) return;
        bool previous = upgrading;
        upgrading = true;
        try
        {
            SynchronizeRun();
            foreach (UpgradeReservation reservation in new List<UpgradeReservation>(reservations)) reservation.Dispose();
            RefreshInstances(null, current);
            if (RunReset != null)
                foreach (Action<Guid> listener in RunReset.GetInvocationList())
                {
                    if (!IsCurrentRun(current)) break;
                    try { listener(current); }
                    catch (Exception exception) { Debug.LogException(exception); }
                }
        }
        finally { upgrading = previous; }
    }

    private bool IsCurrentRun(Guid expected) => !IsDisposed && runId == expected && getRunId() == expected;

    private void RefreshInstances(string trapId, Guid expected)
    {
        // Include inactive survivors and bypass the static active set: with scene/domain reload
        // disabled that set may have been cleared without OnEnable running again.
        foreach (TrapInstance trap in UnityEngine.Object.FindObjectsByType<TrapInstance>(
            FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!IsCurrentRun(expected)) break;
            if (trap == null || trap.Definition == null || !TrapUpgradeCatalog.IsValidId(trap.Definition.TrapId)) continue;
            if (trapId == null || trap.Definition.TrapId == trapId)
                trap.RefreshUpgradeState(GetState(trap.Definition.TrapId), GetMultipliers(trap.Definition.TrapId),
                    () => IsCurrentRun(expected));
        }
    }

    public void Dispose()
    {
        if (IsDisposed) return;
        IsDisposed = true;
        foreach (UpgradeReservation reservation in new List<UpgradeReservation>(reservations)) reservation.Dispose();
        if (!ReferenceEquals(wallet, null))
        {
            wallet.RunStarted -= HandleRunStarted;
            wallet.WalletDestroyed -= Dispose;
            if (wallet.UpgradeService == this) wallet.UpgradeService = null;
        }
        states.Clear();
        versions.Clear();
        basePrices.Clear();
        runId = Guid.Empty;
        Upgraded = null;
        RunReset = null;
    }

    public TrapUpgradeState GetState(string trapId)
    {
        if (!TrapUpgradeCatalog.IsValidId(trapId)) throw new ArgumentException("A valid TrapId is required.", nameof(trapId));
        SynchronizeRun();
        return states.TryGetValue(trapId, out TrapUpgradeState state) ? state : TrapUpgradeState.Initial;
    }

    public IReadOnlyList<TrapUpgradeCatalog.Branch> GetBranches(string trapId) =>
        catalog != null ? catalog.GetBranches(trapId) : Array.Empty<TrapUpgradeCatalog.Branch>();

    public TrapUpgradeMultipliers GetMultipliers(string trapId) => ResolveMultipliers(trapId, GetState(trapId));

    private TrapUpgradeMultipliers ResolveMultipliers(string trapId, TrapUpgradeState state) =>
        catalog != null ? catalog.GetMultipliers(trapId, state) : TrapUpgradeCatalog.DefaultMultipliers(state.Level);

    /// <summary>None is required for levels 1/2; choose Normal or an available Branch at level 3.</summary>
    public TrapUpgradeQuote GetQuote(TrapDefinition definition,
        TrapUpgradeDirection direction = TrapUpgradeDirection.None, string branchId = null)
    {
        SynchronizeRun();
        TrapUpgradeQuote Reject(TrapUpgradeFailure failure, string message) => new TrapUpgradeQuote(
            this, definition, runId, TrapUpgradeState.Initial, TrapUpgradeState.Initial,
            TrapUpgradeCatalog.DefaultMultipliers(1), TrapUpgradeCatalog.DefaultMultipliers(1), 0, 0, failure, message);
        if (IsDisposed) return Reject(TrapUpgradeFailure.ServiceDisposed, "升级服务已结束。");
        if (definition == null || !TrapUpgradeCatalog.IsValidId(definition.TrapId) || definition.Cost < 0)
            return Reject(TrapUpgradeFailure.InvalidDefinition, "陷阱标识无效或基础价格为负数。");
        if (runId == Guid.Empty) return Reject(TrapUpgradeFailure.RunUnavailable, "本局尚未初始化。");
        string id = definition.TrapId;
        if (basePrices.TryGetValue(id, out int price) && price != definition.Cost)
            return Reject(TrapUpgradeFailure.ConflictingDefinition, $"同一 TrapId '{id}' 的基础价格不一致。");
        basePrices[id] = definition.Cost;
        TrapUpgradeState current = GetState(id);
        if (current.Level == 4) return Reject(TrapUpgradeFailure.MaxLevel, "陷阱已满级，不能更换方向。");
        if (current.Level < 3 && direction != TrapUpgradeDirection.None ||
            current.Level == 3 && direction != TrapUpgradeDirection.Normal && direction != TrapUpgradeDirection.Branch ||
            direction != TrapUpgradeDirection.Branch && !string.IsNullOrEmpty(branchId) ||
            direction == TrapUpgradeDirection.Branch && !TrapUpgradeCatalog.IsValidId(branchId))
            return Reject(TrapUpgradeFailure.InvalidDirection, "仅三级升四级时可选择普通或分支方向。");
        try
        {
            if (catalog != null) catalog.Validate();
            TrapUpgradeCatalog.Branch branch = direction == TrapUpgradeDirection.Branch && catalog != null
                ? catalog.GetBranch(id, branchId) : null;
            if (direction == TrapUpgradeDirection.Branch && branch == null)
                return Reject(TrapUpgradeFailure.BranchUnavailable, "该四级分支尚未配置。");

            var target = new TrapUpgradeState(current.Level + 1, direction, branchId);
            int cost = catalog != null ? catalog.GetCost(id, definition.Cost, target) :
                TrapUpgradeCatalog.CalculateCost(definition.Cost, TrapUpgradeCatalog.DefaultCostMultiplier(target.Level));
            versions.TryGetValue(id, out long version);
            TrapUpgradeMultipliers currentMultipliers = ResolveMultipliers(id, current);
            TrapUpgradeMultipliers targetMultipliers = ResolveMultipliers(id, target);
            if (branch != null && (!branch.integrated || branchEffectAvailable == null || !branchEffectAvailable(id, branchId)))
                return new TrapUpgradeQuote(this, definition, runId, current, target, currentMultipliers,
                    targetMultipliers, cost, version, TrapUpgradeFailure.BranchUnavailable,
                    !branch.integrated ? "该四级分支尚未接入。" : "该四级分支的效果适配尚未接入。");
            return new TrapUpgradeQuote(this, definition, runId, current, target,
                currentMultipliers, targetMultipliers, cost, version);
        }
        catch (Exception exception) when (exception is ArgumentException || exception is InvalidOperationException || exception is OverflowException)
        {
            return Reject(TrapUpgradeFailure.InvalidConfiguration, exception.Message);
        }
    }

    /// <summary>Wallet services always pay. Rule/test services retain their payment-free transition.</summary>
    public bool TryUpgrade(TrapUpgradeQuote quote, out TrapUpgradeFailure failure)
    {
        if (ReferenceEquals(wallet, null)) return CommitUpgrade(quote, null, out failure);
        if (!TryReserveUpgrade(quote, out UpgradeReservation reservation, out failure)) return false;
        using (reservation) return reservation.Commit(out failure);
    }

    /// <summary>Reserve available funds without publishing a balance or changing levels; Dispose cancels.</summary>
    public bool TryReserveUpgrade(TrapUpgradeQuote quote, out UpgradeReservation reservation,
        out TrapUpgradeFailure failure)
    {
        reservation = null;
        if (IsDisposed) { failure = TrapUpgradeFailure.ServiceDisposed; return false; }
        if (upgrading || wallet != null && wallet.IsBeginningRun) { failure = TrapUpgradeFailure.Busy; return false; }
        if (wallet == null) { failure = TrapUpgradeFailure.RunUnavailable; return false; }
        upgrading = true;
        try
        {
            if (!ValidateQuote(quote, out failure)) return false;
            if (!wallet.TryReserve(quote.Cost, out EconomyManager.SpendReservation payment))
            { failure = TrapUpgradeFailure.InsufficientFunds; return false; }
            reservation = new UpgradeReservation(this, quote, payment);
            reservations.Add(reservation);
            return true;
        }
        finally { upgrading = false; }
    }

    public sealed class UpgradeReservation : IDisposable
    {
        private readonly TrapUpgradeService owner;
        private readonly TrapUpgradeQuote quote;
        private EconomyManager.SpendReservation payment;

        internal UpgradeReservation(TrapUpgradeService owner, TrapUpgradeQuote quote, EconomyManager.SpendReservation payment)
        { this.owner = owner; this.quote = quote; this.payment = payment; }

        public bool Commit(out TrapUpgradeFailure failure)
        {
            if (payment == null) { failure = TrapUpgradeFailure.ReservationUnavailable; return false; }
            EconomyManager.SpendReservation pending = payment;
            payment = null;
            owner.reservations.Remove(this);
            using (pending) return owner.CommitUpgrade(quote, pending, out failure);
        }

        public void Dispose()
        {
            payment?.Dispose();
            payment = null;
            owner.reservations.Remove(this);
        }
    }

    private bool ValidateQuote(TrapUpgradeQuote quote, out TrapUpgradeFailure failure)
    {
        failure = TrapUpgradeFailure.StaleQuote;
        if (IsDisposed) { failure = TrapUpgradeFailure.ServiceDisposed; return false; }
        SynchronizeRun();
        if (quote == null || quote.Owner != this || quote.RunId != runId) return false;
        if (!quote.CanUpgrade) { failure = quote.Failure; return false; }
        TrapUpgradeQuote fresh = GetQuote(quote.Definition, quote.Target.Direction, quote.Target.BranchId);
        if (!fresh.CanUpgrade) { failure = fresh.Failure; return false; }
        if (!IsCurrentRun(quote.RunId) || quote.RunId != fresh.RunId || quote.TrapId != fresh.TrapId ||
            quote.BaseCost != fresh.BaseCost || quote.Version != fresh.Version ||
            !quote.Current.Equals(fresh.Current) || !quote.Target.Equals(fresh.Target) ||
            quote.Cost != fresh.Cost || !quote.CurrentMultipliers.Equals(fresh.CurrentMultipliers) ||
            !quote.TargetMultipliers.Equals(fresh.TargetMultipliers) ||
            !GetState(quote.TrapId).Equals(quote.Current)) return false;
        failure = TrapUpgradeFailure.None;
        return true;
    }

    private bool CommitUpgrade(TrapUpgradeQuote quote, EconomyManager.SpendReservation payment,
        out TrapUpgradeFailure failure)
    {
        failure = TrapUpgradeFailure.Busy;
        if (upgrading || wallet != null && wallet.IsBeginningRun) return false;
        upgrading = true;
        try
        {
            if (!ValidateQuote(quote, out failure)) return false;
            // No user callbacks between the last validation, silent debit and committed state.
            if (payment != null && !payment.Commit(false))
            { failure = TrapUpgradeFailure.ReservationUnavailable; return false; }
            states[quote.TrapId] = quote.Target;
            versions[quote.TrapId] = quote.Version + 1;
            failure = TrapUpgradeFailure.None;
            if (!ReferenceEquals(wallet, null)) RefreshInstances(quote.TrapId, quote.RunId);
            if (wallet != null && IsCurrentRun(quote.RunId)) wallet.NotifyBalanceChanged();
            if (Upgraded != null)
                foreach (Action<string, TrapUpgradeState> listener in Upgraded.GetInvocationList())
                {
                    if (!IsCurrentRun(quote.RunId)) break;
                    try { listener(quote.TrapId, quote.Target); }
                    catch (Exception exception) { Debug.LogException(exception); }
                }
            return true;
        }
        finally { upgrading = false; }
    }
}
