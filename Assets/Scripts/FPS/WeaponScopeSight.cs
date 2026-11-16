using System.Collections.Generic;
using System.Reflection;
using KINEMATION.FPSAnimationPack.Scripts.Player;
using UnityEngine;

/// <summary>
/// 为自带瞄准镜的武器（默认 MGX5）提供两类 ADS 表现：
///   1. 镜内放大：按住右键瞄准时接管第一人称相机 FOV，平滑收缩到
///      <see cref="aimFieldOfView"/>；松开后平滑还原。腰射 FOV 不写死，
///      进入 / 退出瞄准时都以相机当时的 FOV 为混合起点，避免和包内相机
///      自带的 ADS FOV 动画互相拉扯出现跳变。
///   2. 十字分划：镜内状态在屏幕中心叠加一个带中心间隙的十字，线条 / 描边
///      均可序列化配置。腰射准心（<see cref="HipFireCrosshairUI"/>）在 ADS
///      时本就隐藏，两者不会重叠。
///
/// 只对其他武器无影响：当前手持武器名不匹配 <see cref="scopeWeaponName"/>
/// 时组件完全放行相机 FOV。
/// </summary>
[DefaultExecutionOrder(1010)]
public sealed class WeaponScopeSight : MonoBehaviour
{
    [Header("武器匹配")]
    [SerializeField, Tooltip("匹配当前手持武器对象名的子串（不区分大小写）。默认只对 MGX5 生效。")]
    private string scopeWeaponName = "mgx5";

    [Header("镜内放大")]
    [SerializeField, Min(5f)] private float aimFieldOfView = 22f;
    [SerializeField, Min(0.1f), Tooltip("每秒推进的放大进度，12 表示约 1/12 秒完成。")]
    private float zoomInSpeed = 12f;
    [SerializeField, Min(0.1f)] private float zoomOutSpeed = 10f;

    [Header("十字分划")]
    [SerializeField] private Color reticleColor = new Color(0f, 0f, 0f, 0.9f);
    [SerializeField, Min(1f)] private float lineThickness = 2f;
    [SerializeField, Min(0f)] private float centerGap = 8f;
    [SerializeField, Range(0f, 0.25f)] private float edgeMarginFraction = 0.04f;
    [SerializeField, Min(0f)] private float outlineThickness = 0.6f;
    [SerializeField] private Color outlineColor = new Color(1f, 1f, 1f, 0.35f);

    private PlayerSprintAimBridge aimBridge;
    private Camera viewCamera;
    private FPSPlayer fpsPlayer;
    private MethodInfo getActiveWeaponMethod;
    private readonly List<Transform> weaponNameCandidates = new List<Transform>();
    private bool candidatesBuilt;

    private float zoomProgress;
    private bool wasAiming;
    private float zoomStartFieldOfView;
    private float zoomExitBaseFieldOfView;

    /// <summary>0 = 完全腰射视角，1 = 完全镜内视角。</summary>
    public float ZoomProgress => zoomProgress;

    /// <summary>当前是否处于「手持瞄准镜武器 + ADS」状态。</summary>
    public bool IsScoping => wasAiming;

    private void Awake()
    {
        aimBridge = GetComponent<PlayerSprintAimBridge>();
        viewCamera = GetComponentInChildren<Camera>(true);
        fpsPlayer = GetComponentInChildren<FPSPlayer>(true);
    }

    private void OnDisable()
    {
        zoomProgress = 0f;
        wasAiming = false;
    }

    private void LateUpdate()
    {
        if (!EnsureRefs())
        {
            return;
        }

        bool aiming = aimBridge != null && aimBridge.isActiveAndEnabled && aimBridge.IsAiming
                      && !TrapSelectionMenu.CursorOwned && !GameStartMenu.CursorOwned
                      && IsScopeWeaponActive();
        float deltaTime = Time.deltaTime;

        if (aiming)
        {
            if (!wasAiming)
            {
                // 以进入瞄准时相机的实际 FOV 为起点（包内 ADS 动画可能已经改过）。
                zoomStartFieldOfView = viewCamera.fieldOfView;
            }

            wasAiming = true;
            zoomProgress = Mathf.MoveTowards(zoomProgress, 1f, zoomInSpeed * deltaTime);
            viewCamera.fieldOfView = BlendFieldOfView(zoomStartFieldOfView, aimFieldOfView, zoomProgress);
            return;
        }

        if (wasAiming)
        {
            // 退出瞄准的第一帧，相机可能已被包内逻辑还原成腰射 FOV；以它为起点淡出。
            zoomExitBaseFieldOfView = viewCamera.fieldOfView;
            wasAiming = false;
        }

        if (zoomProgress > 0f)
        {
            zoomProgress = Mathf.MoveTowards(zoomProgress, 0f, zoomOutSpeed * deltaTime);
            viewCamera.fieldOfView = BlendFieldOfView(zoomExitBaseFieldOfView, aimFieldOfView, zoomProgress);
        }
    }

    /// <summary>FOV 混合曲线（SmoothStep），供运行时与测试共用。</summary>
    public static float BlendFieldOfView(float baseFieldOfView, float aimFieldOfView, float progress)
    {
        float t = Mathf.Clamp01(progress);
        float smooth = t * t * (3f - 2f * t);
        return Mathf.Lerp(baseFieldOfView, aimFieldOfView, smooth);
    }

    /// <summary>手持武器名是否命中瞄准镜武器（忽略 ".prefab" 后缀与大小写）。</summary>
    public static bool MatchesScopeWeapon(string weaponName, string filter)
    {
        if (string.IsNullOrEmpty(weaponName) || string.IsNullOrEmpty(filter))
        {
            return false;
        }

        string name = weaponName.Trim().ToLowerInvariant();
        if (name.EndsWith(".prefab"))
        {
            name = name.Substring(0, name.Length - ".prefab".Length);
        }

        return name.Contains(filter.Trim().ToLowerInvariant());
    }

    private bool EnsureRefs()
    {
        if (aimBridge == null)
        {
            aimBridge = GetComponent<PlayerSprintAimBridge>();
        }

        if (viewCamera == null)
        {
            viewCamera = GetComponentInChildren<Camera>(true);
        }

        if (fpsPlayer == null)
        {
            fpsPlayer = GetComponentInChildren<FPSPlayer>(true);
        }

        return viewCamera != null;
    }

    /// <summary>
    /// 优先通过包内 <c>GetActiveWeapon()</c> 读当前手持武器（反射，兼容包版本）；
    /// 取不到时退回「层级里名字命中且处于激活状态」的兜底判断。
    /// </summary>
    private bool IsScopeWeaponActive()
    {
        string activeName = ResolveActiveWeaponName();
        if (!string.IsNullOrEmpty(activeName))
        {
            return MatchesScopeWeapon(activeName, scopeWeaponName);
        }

        if (!candidatesBuilt)
        {
            BuildWeaponNameCandidates();
        }

        for (int i = 0; i < weaponNameCandidates.Count; i++)
        {
            Transform candidate = weaponNameCandidates[i];
            if (candidate != null && candidate.gameObject.activeInHierarchy)
            {
                return true;
            }
        }

        return false;
    }

    private string ResolveActiveWeaponName()
    {
        if (fpsPlayer == null)
        {
            return null;
        }

        if (getActiveWeaponMethod == null)
        {
            getActiveWeaponMethod = fpsPlayer.GetType().GetMethod(
                "GetActiveWeapon", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        }

        if (getActiveWeaponMethod == null)
        {
            return null;
        }

        object weapon = getActiveWeaponMethod.Invoke(fpsPlayer, null);
        switch (weapon)
        {
            case GameObject go:
                return go.name;
            case Component component:
                return component.gameObject.name;
            default:
                return null;
        }
    }

    private void BuildWeaponNameCandidates()
    {
        candidatesBuilt = true;
        weaponNameCandidates.Clear();

        Transform[] all = GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < all.Length; i++)
        {
            if (MatchesScopeWeapon(all[i].name, scopeWeaponName))
            {
                weaponNameCandidates.Add(all[i]);
            }
        }
    }

    private void OnGUI()
    {
        if (zoomProgress <= 0.001f || !IsScoping)
        {
            return;
        }

        if (TrapSelectionMenu.CursorOwned || GameStartMenu.CursorOwned)
        {
            return;
        }

        float alpha = Mathf.Clamp01(zoomProgress);
        Color tint = reticleColor;
        Color outline = outlineColor;
        tint.a *= alpha;
        outline.a *= alpha;

        float scale = Mathf.Max(0.5f, Screen.height / 1080f);
        float thickness = Mathf.Max(1f, lineThickness * scale);
        float gap = centerGap * scale;
        float marginX = Screen.width * edgeMarginFraction;
        float marginY = Screen.height * edgeMarginFraction;
        float cx = Screen.width * 0.5f;
        float cy = Screen.height * 0.5f;

        Texture2D pixel = Texture2D.whiteTexture;
        DrawRect(pixel, new Rect(marginX, cy - thickness * 0.5f, Mathf.Max(0f, cx - gap - marginX), thickness),
            tint, outline, scale);
        DrawRect(pixel, new Rect(cx + gap, cy - thickness * 0.5f, Mathf.Max(0f, Screen.width - marginX - (cx + gap)), thickness),
            tint, outline, scale);
        DrawRect(pixel, new Rect(cx - thickness * 0.5f, marginY, thickness, Mathf.Max(0f, cy - gap - marginY)),
            tint, outline, scale);
        DrawRect(pixel, new Rect(cx - thickness * 0.5f, cy + gap, thickness, Mathf.Max(0f, Screen.height - marginY - (cy + gap))),
            tint, outline, scale);
    }

    private static void DrawRect(Texture2D pixel, Rect rect, Color tint, Color outline, float scale)
    {
        if (rect.width <= 0f || rect.height <= 0f)
        {
            return;
        }

        float outlineWidth = outline.a > 0f ? Mathf.Max(1f, outline.a * scale) : 0f;
        if (outlineWidth > 0f)
        {
            GUI.color = outline;
            GUI.DrawTexture(new Rect(rect.x - outlineWidth, rect.y - outlineWidth,
                rect.width + outlineWidth * 2f, rect.height + outlineWidth * 2f), pixel);
        }

        GUI.color = tint;
        GUI.DrawTexture(rect, pixel);
        GUI.color = Color.white;
    }
}
