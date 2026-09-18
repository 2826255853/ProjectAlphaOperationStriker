#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public sealed class TrapHealthBarUITests
{
    private readonly List<GameObject> objects = new List<GameObject>();

    [TearDown]
    public void TearDown()
    {
        for (int i = objects.Count - 1; i >= 0; i--)
        {
            if (objects[i] == null) continue;
            foreach (TrapInstance trap in objects[i].GetComponentsInChildren<TrapInstance>()) Invoke(trap, "OnDisable");
            Object.DestroyImmediate(objects[i]);
        }
        objects.Clear();
    }

    [Test]
    public void NearestTrapOnTheAimRayIsSelected()
    {
        TrapInstance onRay = CreateTrap("OnRay", new Vector3(0f, 0f, 10f));
        CreateTrap("OffRay", new Vector3(0.5f, 0f, 10f));

        TrapInstance aimed = TrapHealthBarUI.FindTrapNearRay(new Ray(Vector3.zero, Vector3.forward), float.PositiveInfinity);

        Assert.That(aimed, Is.EqualTo(onRay));
    }

    [Test]
    public void TrapsBehindTheViewerAreIgnored()
    {
        CreateTrap("Behind", new Vector3(0f, 0f, -5f));

        TrapInstance aimed = TrapHealthBarUI.FindTrapNearRay(new Ray(Vector3.zero, Vector3.forward), float.PositiveInfinity);

        Assert.That(aimed, Is.Null);
    }

    [Test]
    public void TrapsFartherThanAnOccluderAreIgnored()
    {
        CreateTrap("Far", new Vector3(0f, 0f, 30f));

        TrapInstance aimed = TrapHealthBarUI.FindTrapNearRay(new Ray(Vector3.zero, Vector3.forward), 12f);

        Assert.That(aimed, Is.Null);
    }

    [Test]
    public void DamagedTrapReportsAReducedHealthRatio()
    {
        TrapInstance trap = CreateTrap("Damaged", new Vector3(0f, 0f, 5f));

        trap.TakeDamage(trap.MaxHealth * 0.75f);

        Assert.That(trap.CurrentHealth, Is.EqualTo(trap.MaxHealth * 0.25f).Within(0.01f));
        Assert.That(trap.CurrentHealth / trap.MaxHealth, Is.LessThan(0.3f), "Bar must switch to the low-health color.");
    }

    private TrapInstance CreateTrap(string name, Vector3 position)
    {
        var obj = new GameObject(name);
        obj.transform.position = position;
        objects.Add(obj);
        TrapInstance trap = obj.AddComponent<TrapInstance>();
        Invoke(trap, "Awake");
        Invoke(trap, "OnEnable");
        return trap;
    }

    private static void Invoke(object target, string name) =>
        target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null);
}
#endif
