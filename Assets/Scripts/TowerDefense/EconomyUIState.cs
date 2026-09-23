using System;
using UnityEngine.SceneManagement;

/// <summary>Lifecycle-bound wallet view shared by HUD, menu and placement feedback.</summary>
public sealed class EconomyUIState : IDisposable
{
    private EconomyManager wallet;
    private Guid runId;
    private bool active;
    public bool IsReady { get; private set; }
    public int Balance { get; private set; }
    public string BalanceText => IsReady ? $"金币：{Balance}" : "金币：--";
    public event Action Changed;

    public void Enable()
    {
        if (active) return;
        active = true;
        SceneManager.sceneLoaded += OnSceneLoaded;
        Refresh();
    }

    public void Refresh()
    {
        if (!active) return;
        EconomyManager current = EconomyManager.Instance;
        Guid currentRun = current != null ? current.RunId : Guid.Empty;
        if (!ReferenceEquals(wallet, current) || runId != currentRun)
        {
            if (!ReferenceEquals(wallet, null)) wallet.BalanceChanged -= OnBalanceChanged;
            wallet = current;
            runId = currentRun;
            if (wallet != null) wallet.BalanceChanged += OnBalanceChanged;
        }
        bool ready = wallet != null && wallet.IsInitialized;
        int balance = ready ? wallet.Balance : 0;
        if (IsReady == ready && Balance == balance) return;
        IsReady = ready;
        Balance = balance;
        Changed?.Invoke();
    }

    public bool CanAfford(TrapDefinition definition) => definition != null && wallet != null
        && wallet.CanAfford(definition.Cost);

    public string PurchaseHint(TrapDefinition definition)
    {
        if (definition == null) return string.Empty;
        if (definition.Cost < 0) return "价格配置无效";
        if (!IsReady || wallet == null) return "金币暂不可用";
        return CanAfford(definition) ? string.Empty
            : $"金币不足，还差 {Math.Max(0, definition.Cost - wallet.AvailableBalance)}";
    }

    private void OnBalanceChanged(int _) => Refresh();
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Refresh();

    public void Dispose()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (!ReferenceEquals(wallet, null)) wallet.BalanceChanged -= OnBalanceChanged;
        wallet = null;
        runId = Guid.Empty;
        active = false;
        IsReady = false;
        Balance = 0;
    }
}
