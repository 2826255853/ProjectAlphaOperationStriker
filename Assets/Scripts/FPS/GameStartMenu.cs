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
    private bool started;
    private bool loading;
    private bool restoreInputPending;
    private bool previousCursorVisible;
    private CursorLockMode previousCursorLock;
    private float previousTimeScale = 1f;
    private static int cursorReleasedFrame = -1;
    private static bool skipMenuOnNextSceneLoad;

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
        if (mode == LoadSceneMode.Single && skipMenuOnNextSceneLoad)
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
        if (loading || Keyboard.current == null || !Keyboard.current.escapeKey.wasPressedThisFrame) return;
        if (open)
        {
            if (settings) ShowSettings(false);
            else if (mapSelection) CloseMapSelection();
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
        if (!open || loading || settings || mapSelection) return;
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

        if (buildIndex == MapSceneCatalog.GetCurrentBuildIndex()) Close();
        else LoadMap(map);
    }

    public void OpenSettings() => ShowSettings(true);
    public void CloseSettings() => ShowSettings(false);
    public void SetMasterVolume(float value) => AudioListener.volume = Mathf.Clamp01(value);
    public void SetFullscreen(bool value) => Screen.fullScreen = value;

    public void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
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
        if (!open || loading || settings == show) return;
        settings = show;
        if (show) mapSelection = false;
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
                loading, MapSceneCatalog.GetMaps());
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
