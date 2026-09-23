using System;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>One wallet per run, shared by all enemy entrances and player purchases.</summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-1100)]
public sealed class EconomyManager : MonoBehaviour
{
    // TODO(确认): 首版开局 100 金币，后续根据首波和陷阱价格调整。
    [SerializeField, Min(0), Tooltip("每局的初始金币；波次切换不会重新发放。")]
    private int startingMoney = 100;

    public static EconomyManager Instance { get; private set; }
    public int Balance { get; private set; }
    public bool IsInitialized { get; private set; }
    public Guid RunId { get; private set; }
    public event Action<int> BalanceChanged;
    private int reservedMoney;
    public int AvailableBalance => Balance - reservedMoney;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
        EnsureInitialized();
    }

    private void OnDestroy()
    {
        BalanceChanged = null;
        if (Instance == this) Instance = null;
    }

    /// <summary>Explicit new-run boundary. Never call this for a wave transition.</summary>
    public void BeginRun(int initialMoney)
    {
        if (initialMoney < 0) throw new ArgumentOutOfRangeException(nameof(initialMoney));

        RunId = Guid.NewGuid();
        reservedMoney = 0;
        Balance = initialMoney;
        IsInitialized = true;
        BalanceChanged?.Invoke(Balance);
    }

    public bool CanAfford(int amount) => IsInitialized && amount >= 0 && amount <= AvailableBalance;

    // Reservations affect purchasing power, but publish no intermediate balance events.
    internal bool TryReserve(int amount, out SpendReservation reservation)
    {
        reservation = null;
        if (!CanAfford(amount)) return false;
        reservedMoney += amount;
        reservation = new SpendReservation(this, amount);
        return true;
    }

    internal sealed class SpendReservation : IDisposable
    {
        private EconomyManager wallet;
        private readonly Guid runId;
        private readonly int amount;

        internal SpendReservation(EconomyManager wallet, int amount)
        {
            this.wallet = wallet;
            runId = wallet.RunId;
            this.amount = amount;
        }

        internal bool Commit()
        {
            EconomyManager owner = wallet;
            if (owner == null || !owner.IsInitialized || owner.RunId != runId) return false;
            wallet = null;
            owner.reservedMoney -= amount;
            owner.Balance -= amount;
            if (amount != 0) owner.NotifyBalanceChanged();
            return true;
        }

        public void Dispose()
        {
            if (wallet != null && wallet.RunId == runId) wallet.reservedMoney -= amount;
            wallet = null;
        }
    }

    private void NotifyBalanceChanged()
    {
        if (BalanceChanged == null) return;
        // A broken HUD listener must not turn a committed purchase into a rollback.
        foreach (Action<int> listener in BalanceChanged.GetInvocationList())
        {
            try { listener(Balance); }
            catch (Exception exception) { Debug.LogException(exception, this); }
        }
    }

    /// <summary>Invalid or unaffordable requests leave the wallet unchanged.</summary>
    public bool TrySpend(int amount)
    {
        if (!CanAfford(amount)) return false;
        if (amount == 0) return true;

        Balance -= amount;
        BalanceChanged?.Invoke(Balance);
        return true;
    }

    /// <summary>Reject invalid rewards instead of silently losing money to overflow.</summary>
    public void Grant(int amount)
    {
        if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
        if (!IsInitialized) throw new InvalidOperationException("The economy has not started a run.");
        if (amount > int.MaxValue - Balance) throw new OverflowException("The wallet balance would exceed Int32.MaxValue.");
        if (amount == 0) return;

        Balance += amount;
        BalanceChanged?.Invoke(Balance);
    }

    /// <summary>Idempotent startup; multiple entrances must share the same balance.</summary>
    public static EconomyManager GetOrCreate()
    {
        if (Instance == null)
        {
            Instance = FindAnyObjectByType<EconomyManager>();
            if (Instance == null)
                Instance = new GameObject("Economy Manager").AddComponent<EconomyManager>();
        }

        Instance.EnsureInitialized();
        return Instance;
    }

    private void EnsureInitialized()
    {
        if (!IsInitialized) BeginRun(startingMoney);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRuntimeState()
    {
        // Also reset when both domain reload and scene reload are disabled.
        if (Instance != null)
        {
            Instance.BalanceChanged = null;
            Instance.IsInitialized = false;
            Instance.Balance = 0;
            Instance.reservedMoney = 0;
            Instance.RunId = Guid.Empty;
        }
        Instance = null;
        SceneManager.sceneLoaded -= HandleSceneLoaded;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InitializeRuntime()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
        GetOrCreate();
    }

    private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode) => GetOrCreate();
}
