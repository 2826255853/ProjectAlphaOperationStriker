#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Edit-mode coverage for the launch plumes bolted onto the dual missile launcher.
/// Guards the two things the eye cannot check reliably: each rail gets its own
/// plume stack aligned with the tube, and the effect only burns while the weapon
/// is actually firing.
/// </summary>
public sealed class MissileExhaustFxTests
{
    private const float Tolerance = 0.15f;
    private readonly List<GameObject> objects = new List<GameObject>();

    [TearDown]
    public void TearDown()
    {
        for (int i = objects.Count - 1; i >= 0; i--)
            if (objects[i] != null) Object.DestroyImmediate(objects[i]);
        objects.Clear();
    }

    [Test]
    public void AuthoredPrefabHasOnePlumeStackPerRail()
    {
        GameObject prefab = LoadPrefab();
        var instance = (GameObject)Object.Instantiate(prefab);
        objects.Add(instance);

        MissileExhaustFx fx = instance.GetComponent<MissileExhaustFx>();
        Assert.That(fx, Is.Not.Null, "预制体上应有 MissileExhaustFx。");
        Assert.That(fx.Weapon, Is.Not.Null, "尾焰应连着 MissileLauncherWeapon。");
        Assert.That(fx.Plumes.Length, Is.EqualTo(2), "两根管子各要一组尾焰。");

        MissileLauncherTurret turret = instance.GetComponent<MissileLauncherTurret>();
        Assert.That(turret, Is.Not.Null);
        Assert.That(turret.HasRig, Is.True);

        // Everything below is measured in Pitch Pivot local space so the world
        // scale of the prefab cannot skew the comparison.
        Vector3 forwardLocal = turret.PitchPivot.InverseTransformDirection(turret.RestBarrelDirectionWorld).normalized;
        var rails = new List<float>();
        for (int i = 0; i < fx.Plumes.Length; i++)
        {
            Transform plume = fx.Plumes[i];
            Assert.That(plume, Is.Not.Null, $"第 {i + 1} 组尾焰引用不能为空。");
            Assert.That(plume.IsChildOf(turret.PitchPivot), Is.True,
                "尾焰必须挂在 Pitch Pivot 下，才能跟着炮管一起俯仰。");

            // The plumes are named "... 01" / "... 02", one per rail.
            Transform barrel = FindDeepChild(instance.transform,
                plume.name.EndsWith("01") ? "Missile_01" : "Missile_02");
            Assert.That(barrel, Is.Not.Null);

            // The barrel points out of the nose, so the tail - and therefore the
            // nozzle - must end up on the opposite side of the missile centre.
            Vector3 barrelCenter = turret.PitchPivot.InverseTransformPoint(RendererCenter(barrel));
            Vector3 plumePosition = plume.localPosition;
            float alongBarrel = Vector3.Dot(plumePosition - barrelCenter, forwardLocal);
            Assert.That(alongBarrel, Is.LessThan(-0.1f),
                $"第 {i + 1} 组尾焰必须在导弹尾部（炮口反方向），实际沿炮口投影 {alongBarrel:F3}。");

            // Each nozzle also has to line up with its own tube, not the neighbour's.
            Vector3 lateral = Vector3.Cross(Vector3.up, forwardLocal).normalized;
            float railOffset = Vector3.Dot(plumePosition - barrelCenter, lateral);
            Assert.That(Mathf.Abs(railOffset), Is.LessThan(0.25f),
                $"第 {i + 1} 组尾焰应正对本管子的轴心，实际横向偏移 {railOffset:F3}。");
            rails.Add(Vector3.Dot(plumePosition, lateral));
        }

        Assert.That(Mathf.Abs(rails[0] - rails[1]), Is.GreaterThan(0.3f),
            "两组尾焰必须分别对着左右两根导轨，不能挤在同一个管子上。");
    }

    [Test]
    public void PlumesBurnOnlyWhileTheWeaponIsFiring()
    {
        GameObject prefab = LoadPrefab();
        var instance = (GameObject)Object.Instantiate(prefab);
        objects.Add(instance);

        MissileExhaustFx fx = instance.GetComponent<MissileExhaustFx>();
        var weapon = instance.GetComponent<MissileLauncherWeapon>();
        Assert.That(fx, Is.Not.Null);
        Assert.That(weapon, Is.Not.Null);

        // Reset to the cold rest state the runtime starts from.
        fx.SetPreviewIntensity(0f);
        Assert.That(fx.Intensity, Is.EqualTo(0f).Within(0.01f), "静止时尾焰应是熄灭的。");
        Assert.That(RenderersEnabled(fx), Is.False, "强度为 0 时尾焰网格必须隐藏。");

        fx.SetPreviewIntensity(1f);
        Assert.That(fx.Intensity, Is.EqualTo(1f).Within(0.01f));
        Assert.That(RenderersEnabled(fx), Is.True, "喷射时尾焰网格必须显示。");

        // A partly faded plume is still visible, so the fade-out reads as a taper.
        fx.SetPreviewIntensity(0.4f);
        Assert.That(RenderersEnabled(fx), Is.True, "半强度时尾焰仍应可见。");

        // Handing control back to the weapon leaves the plumes cold, because the
        // weapon is idle in edit mode and nothing has fired yet.
        fx.SetPreviewIntensity(-1f);
        Assert.That(weapon.State, Is.EqualTo(MissileLauncherState.Idle));
    }

    [Test]
    public void IdlePrefabShipsColdPlumes()
    {
        GameObject prefab = LoadPrefab();
        var instance = (GameObject)Object.Instantiate(prefab);
        objects.Add(instance);

        MissileExhaustFx fx = instance.GetComponent<MissileExhaustFx>();
        Assert.That(fx, Is.Not.Null);
        // Awake zeroes the strength for play mode, but a freshly instantiated prefab
        // in edit mode still reports the authored (hidden) look, i.e. not burning.
        Assert.That(fx.Intensity, Is.LessThan(0.001f), "装配好的预制体不应一进场就喷火。");
    }

    private static GameObject LoadPrefab()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            MissileLauncherTurretAuthoring.LauncherPrefabPath);
        Assert.That(prefab, Is.Not.Null, "应先运行 Tools/塔防/生成导弹发射器预制体。");
        return prefab;
    }

    private static bool RenderersEnabled(MissileExhaustFx fx)
    {
        for (int i = 0; i < fx.Plumes.Length; i++)
        {
            Renderer[] renderers = fx.Plumes[i].GetComponentsInChildren<Renderer>(true);
            for (int r = 0; r < renderers.Length; r++)
                if (renderers[r] != null && renderers[r].enabled) return true;
        }
        return false;
    }

    private static Vector3 RendererCenter(Transform root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        bool any = false;
        Bounds bounds = new Bounds(Vector3.zero, Vector3.zero);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] == null) continue;
            if (!any) { bounds = renderers[i].bounds; any = true; }
            else bounds.Encapsulate(renderers[i].bounds);
        }
        return any ? bounds.center : root.position;
    }

    private static Transform FindDeepChild(Transform root, string name)
    {
        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            if (child.name == name) return child;
            Transform nested = FindDeepChild(child, name);
            if (nested != null) return nested;
        }
        return null;
    }
}
#endif



