using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Start and pause menu for the gameplay scene.</summary>
public sealed class GameStartMenu : MonoBehaviour
{
    private readonly List<InputAction> pausedActions = new List<InputAction>();
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
        Open();
    }

    private void Update()
    {
        RestoreInputAfterClick();
        if (Keyboard.current == null || !Keyboard.current.escapeKey.wasPressedThisFrame) return;
        if (open)
        {
            if (settings) settings = false;
            else if (started) Close();
        }
        else if (!TrapSelectionMenu.CursorOwned)
        {
            Open();
        }
    }

    private void LateUpdate()
    {
        if (!open) return;
        // Players bootstrapped later in the same scene load must be paused too.
        PausePlayerInput();
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
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
        RestoreInputAfterClick();
        Cursor.lockState = started ? CursorLockMode.Locked : previousCursorLock;
        Cursor.visible = started ? false : previousCursorVisible;
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

    private void OnGUI()
    {
        if (!open) return;
        GUI.depth = -100;
        float width = Mathf.Min(360f, Screen.width - 24f);
        float height = settings ? 290f : 276f;
        Rect panel = new Rect((Screen.width - width) * .5f, (Screen.height - height) * .5f, width, height);
        GUI.color = new Color(0f, 0f, 0f, .78f);
        GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Box(panel, settings ? "设置" : "行动打击");

        float x = panel.x + 24f;
        float contentWidth = width - 48f;
        if (settings)
        {
            GUI.Label(new Rect(x, panel.y + 56f, contentWidth, 24f), "主音量");
            AudioListener.volume = GUI.HorizontalSlider(
                new Rect(x, panel.y + 91f, contentWidth, 20f), AudioListener.volume, 0f, 1f);
            bool fullscreen = GUI.Toggle(new Rect(x, panel.y + 135f, contentWidth, 28f),
                Screen.fullScreen, "全屏模式");
            if (fullscreen != Screen.fullScreen) Screen.fullScreen = fullscreen;
            if (GUI.Button(new Rect(x, panel.y + 206f, contentWidth, 42f), "返回")) settings = false;
            return;
        }

        if (GUI.Button(new Rect(x, panel.y + 60f, contentWidth, 44f), started ? "继续游戏" : "开始游戏")) Close();
        if (GUI.Button(new Rect(x, panel.y + 120f, contentWidth, 44f), "设置")) settings = true;
        if (GUI.Button(new Rect(x, panel.y + 180f, contentWidth, 44f), "退出游戏"))
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
