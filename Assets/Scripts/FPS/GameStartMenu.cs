using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public sealed class GameStartMenu : MonoBehaviour
{
    private const string ViewPrefabPath = "UI/GameStartMenu";
    private readonly List<InputAction> pausedActions = new List<InputAction>();
    private GameStartMenuView view;
    private bool open;
    private bool settings;
    private bool started;
    private bool restoreInputPending;
    private bool previousCursorVisible;
    private CursorLockMode previousCursorLock;
    private float previousTimeScale = 1f;
    private static int cursorReleasedFrame = -1;

    public static bool CursorOwned { get; private set; }
    public static bool BlocksPlayerCursor => CursorOwned || cursorReleasedFrame == Time.frameCount;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CreateForScene()
    {
        if (FindAnyObjectByType<GameStartMenu>() == null)
            new GameObject("Game Start Menu").AddComponent<GameStartMenu>();
    }

    private void Awake()
    {
        GameObject prefab = Resources.Load<GameObject>(ViewPrefabPath);
        view = prefab != null ? Instantiate(prefab).GetComponent<GameStartMenuView>() : GameStartMenuView.CreateFallback();
        view.Initialize(this);
        view.SetVisible(false);
        Open();
    }

    private void Update()
    {
        RestoreInputAfterClick();
        if (Keyboard.current == null || !Keyboard.current.escapeKey.wasPressedThisFrame) return;
        if (open)
        {
            if (settings) ShowSettings(false);
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

    public void BeginOrResume() => Close();
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
        if (!open || settings == show) return;
        settings = show;
        RefreshView();
    }

    private void RefreshView()
    {
        if (view == null) return;
        view.SetVisible(open);
        if (open) view.SetState(started, settings, AudioListener.volume, Screen.fullScreen);
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
