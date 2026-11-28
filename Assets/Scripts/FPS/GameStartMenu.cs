using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

public sealed class GameStartMenu : MonoBehaviour
{
    private const string ViewPrefabPath = "UI/GameStartMenu";
    private readonly List<InputAction> pausedActions = new List<InputAction>();
    private GameStartMenuView view;
    private bool open;
    private bool settings;
    private bool mapSelection;
    private bool statistics;
    private bool configuringWeapons;
    private PlayerWeaponCategorySwitcher weaponSwitcher;
    private bool started;
    private bool loading;
    private bool attemptedAutomaticReturn;
    private bool restoreInputPending;
    private bool previousCursorVisible;
    private CursorLockMode previousCursorLock;
    private float previousTimeScale = 1f;
    private static int cursorReleasedFrame = -1;
    private static bool skipMenuOnNextSceneLoad;
    private static string entryScenePath;

    public static bool CursorOwned { get; private set; }
    public static bool BlocksPlayerCursor => CursorOwned || cursorReleasedFrame == Time.frameCount;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InitializeForGame()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        CreateForScene();
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode == LoadSceneMode.Single)
            CreateForScene();
    }

    private static void CreateForScene()
    {
        if (FindAnyObjectByType<GameStartMenu>() == null)
            new GameObject("Game Start Menu").AddComponent<GameStartMenu>();
    }

    private void Awake()
    {
        bool skipMenu = skipMenuOnNextSceneLoad;
        skipMenuOnNextSceneLoad = false;
        started = skipMenu;
        GameObject prefab = Resources.Load<GameObject>(ViewPrefabPath);
        view = prefab != null ? Instantiate(prefab).GetComponent<GameStartMenuView>() : GameStartMenuView.CreateFallback();
        view.Initialize(this);
        view.SetVisible(false);
        if (!skipMenu) Open();
    }

    private void Update()
    {
        RestoreInputAfterClick();
        if (loading) return;

        Keyboard keyboard = Keyboard.current;
        GameFlowManager flow = GameFlowManager.Instance;
        if (started && flow != null && flow.CurrentResult != GameFlowManager.Result.Playing)
        {
            bool timerExpired = flow.ReturnSecondsRemaining <= 0;
            if ((timerExpired && !attemptedAutomaticReturn) || keyboard?.gKey.wasPressedThisFrame == true)
            {
                attemptedAutomaticReturn = timerExpired;
                ReturnToMainMenu();
                return;
            }
        }

        if (keyboard == null || !keyboard.escapeKey.wasPressedThisFrame) return;
        if (open)
        {
            if (settings) ShowSettings(false);
            else if (mapSelection) CloseMapSelection();
            else if (statistics) CloseStatistics();
            else if (configuringWeapons) CloseWeaponLoadout();
            else if (started) Close();
        }
        else if (!TrapSelectionMenu.CursorOwned) Open();
    }

    private void LateUpdate()
    {
        if (!open) return;
        PausePlayerInput();
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public void BeginOrResume()
    {
        if (!open || loading || settings || mapSelection || statistics || configuringWeapons) return;
        if (started) Close();
        else OpenMapSelection();
    }

    private void OpenMapSelection()
    {
        if (!open || started || loading) return;
        settings = false;
        mapSelection = true;
        RefreshView();
    }

    public void CloseMapSelection()
    {
        if (!open || !mapSelection || loading) return;
        mapSelection = false;
        RefreshView();
    }

    public void SelectMap(int buildIndex)
    {
        if (!open || !mapSelection || loading || !MapSceneCatalog.TryGet(buildIndex, out MapSceneCatalog.Entry map))
            return;

        entryScenePath = SceneManager.GetActiveScene().path;
        if (buildIndex == MapSceneCatalog.GetCurrentBuildIndex())
        {
            weaponSwitcher?.ApplyLoadout();
            Close();
        }
        else LoadMap(map);
    }

    public void OpenSettings() => ShowSettings(true);
    public void CloseSettings() => ShowSettings(false);
    public void OpenStatistics()
    {
        if (!open || started || loading || settings || mapSelection || statistics || configuringWeapons) return;
        statistics = true;
        RefreshView();
    }

    public void CloseStatistics()
    {
        if (!open || !statistics) return;
        statistics = false;
        RefreshView();
    }

    public void SetMasterVolume(float value) => AudioListener.volume = Mathf.Clamp01(value);

    public void OpenWeaponLoadout()
    {
        if (!open || started || loading || settings || mapSelection || statistics) return;
        weaponSwitcher = FindAnyObjectByType<PlayerWeaponCategorySwitcher>();
        configuringWeapons = true;
        RefreshView();
    }

    public void CloseWeaponLoadout()
    {
        if (!configuringWeapons) return;
        configuringWeapons = false;
        RefreshView();
    }

    public void CycleWeapon(int slot, int direction)
    {
        if (!configuringWeapons || weaponSwitcher == null || weaponSwitcher.Loadout == null) return;
        var loadout = weaponSwitcher.Loadout;
        if (loadout.TryChoose(slot, loadout.NextOption(slot, direction)))
            view.ShowWeapons(loadout);
    }
    public void SetFullscreen(bool value) => Screen.fullScreen = value;

    public void QuitGame()
    {
        if (loading) return;
        if (started)
        {
            ReturnToMainMenu();
            return;
        }
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void ReturnToMainMenu()
    {
        loading = true;
        skipMenuOnNextSceneLoad = false;
        RefreshView();
        try
        {
            string scenePath = string.IsNullOrEmpty(entryScenePath)
                ? SceneManager.GetActiveScene().path : entryScenePath;
#if UNITY_EDITOR
            if (SceneUtility.GetBuildIndexByScenePath(scenePath) < 0)
            {
                if (UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(scenePath,
                    new LoadSceneParameters(LoadSceneMode.Single)) != null) return;
            }
            else
#endif
            if (SceneManager.LoadSceneAsync(scenePath, LoadSceneMode.Single) != null) return;
        }
        catch (System.Exception exception)
        {
            Debug.LogException(exception);
        }
        loading = false;
        RefreshView();
    }

    private void PausePlayerInput()
    {
        foreach (PlayerInput input in FindObjectsByType<PlayerInput>())
        {
            if (input.actions == null) continue;
            foreach (InputAction action in input.actions)
            {
                if (!action.enabled) continue;
                action.Disable();
                pausedActions.Add(action);
            }
        }
    }

    private void Open()
    {
        if (open) return;
        restoreInputPending = false;
        previousTimeScale = Time.timeScale;
        previousCursorLock = Cursor.lockState;
        previousCursorVisible = Cursor.visible;
        open = true;
        CursorOwned = true;
        Time.timeScale = 0f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        PausePlayerInput();
        RefreshView();
    }

    private void Close()
    {
        if (!open) return;
        open = false;
        settings = false;
        mapSelection = false;
        statistics = false;
        configuringWeapons = false;
        started = true;
        CursorOwned = false;
        cursorReleasedFrame = Time.frameCount;
        Time.timeScale = previousTimeScale;
        restoreInputPending = true;
        RefreshView();
        RestoreInputAfterClick();
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void ShowSettings(bool show)
    {
        if (!open || loading || configuringWeapons || settings == show) return;
        settings = show;
        if (show)
        {
            mapSelection = false;
            statistics = false;
        }
        RefreshView();
    }

    private void LoadMap(MapSceneCatalog.Entry map)
    {
        loading = true;
        skipMenuOnNextSceneLoad = true;
        RefreshView();
        try
        {
            AsyncOperation operation = SceneManager.LoadSceneAsync(map.BuildIndex, LoadSceneMode.Single);
            if (operation == null)
            {
                loading = false;
                skipMenuOnNextSceneLoad = false;
                RefreshView();
            }
        }
        catch (System.Exception exception)
        {
            loading = false;
            skipMenuOnNextSceneLoad = false;
            Debug.LogException(exception);
            RefreshView();
        }
    }

    private void RefreshView()
    {
        if (view == null) return;
        view.SetVisible(open);
        if (open)
            view.SetState(started, settings, mapSelection, AudioListener.volume, Screen.fullScreen,
                loading, MapSceneCatalog.GetMaps(), statistics, configuringWeapons);
        if (configuringWeapons) view.ShowWeapons(weaponSwitcher != null ? weaponSwitcher.Loadout : null);
    }

    private void RestoreInputAfterClick()
    {
        if (!restoreInputPending || (Mouse.current != null && Mouse.current.leftButton.isPressed)) return;
        restoreInputPending = false;
        foreach (InputAction action in pausedActions)
            if (action != null) action.Enable();
        pausedActions.Clear();
    }

    private void OnDestroy()
    {
        if (open) Close();
        restoreInputPending = false;
        foreach (InputAction action in pausedActions)
            if (action != null) action.Enable();
        pausedActions.Clear();
    }
}
