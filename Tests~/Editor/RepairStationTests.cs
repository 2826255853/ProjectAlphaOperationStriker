#if UNITY_INCLUDE_TESTS
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public sealed class RepairStationTests
{
    private readonly List<Object> cleanup = new List<Object>();
    private TrapPlacementGrid grid;
    private TrapDefinition definition, targetDefinition;
    private TrapInstance station;
    private RepairStationTrap repair;
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [SetUp]
    public void SetUp()
    {
        grid = Grid(new Vector3(10000f, 0f, 10000f));
        definition = AssetDatabase.LoadAssetAtPath<TrapDefinition>(RepairStationAuthoring.DefinitionPath);
        Assert.That(definition, Is.Not.Null);
        targetDefinition = Definition(1, 1);
        station = Place(grid, new Vector2Int(4, 4), definition);
        repair = station.GetComponent<RepairStationTrap>();
    }

    [TearDown]
    public void TearDown()
    {
        Time.timeScale = 1f;
        for (int i = cleanup.Count - 1; i >= 0; i--)
            if (cleanup[i] != null) Object.DestroyImmediate(cleanup[i]);
        cleanup.Clear();
    }

    private TrapPlacementGrid Grid(Vector3 position, float size = 1f, Quaternion? rotation = null)
    {
        var root = new GameObject("Repair Test Grid"); cleanup.Add(root);
        root.transform.SetPositionAndRotation(position, rotation ?? Quaternion.identity);
        var result = root.AddComponent<TrapPlacementGrid>();
        result.ConfigureLayout(16, 16, size, 0f, Enumerable.Repeat(true, 256).ToArray(), TrapPlacementGrid.SurfaceKind.Ground);
        return result;
    }

    private TrapDefinition Definition(int width, int height)
    {
        var result = ScriptableObject.CreateInstance<TrapDefinition>(); cleanup.Add(result);
        var so = new SerializedObject(result);
        so.FindProperty("footprintWidth").intValue = width;
        so.FindProperty("footprintHeight").intValue = height;
        so.ApplyModifiedPropertiesWithoutUndo();
        return result;
    }

    private TrapInstance Place(TrapPlacementGrid owner, Vector2Int cell, TrapDefinition def = null, float damage = 0f)
    {
        Assert.That(owner.TryPlaceTrap(cell, def ?? targetDefinition, out TrapInstance trap, out string failure), Is.True, failure);
        if (trap.IsDestroyed) typeof(TrapInstance).GetMethod("Awake", Private).Invoke(trap, null);
        typeof(TrapInstance).GetMethod("OnEnable", Private).Invoke(trap, null);
        if (damage > 0f) trap.TakeDamage(damage);
        return trap;
    }

    [Test]
    public void RepairClampsAndRejectsInvalidAmountsWithoutDamageEvents()
    {
        int damageEvents = 0;
        station.Damaged += _ => damageEvents++;
        station.TakeDamage(10f);
        Assert.That(station.Repair(25f), Is.EqualTo(10f));
        foreach (float value in new[] { -1f, 0f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            Assert.That(station.Repair(value), Is.Zero);
        Assert.That(station.CurrentHealth, Is.EqualTo(100f));
        Assert.That(damageEvents, Is.EqualTo(1));
        var dead = new GameObject("Dead trap"); cleanup.Add(dead);
        var deadTrap = dead.AddComponent<TrapInstance>();
        typeof(TrapInstance).GetProperty("CurrentHealth").SetValue(deadTrap, 0f);
        Assert.That(deadTrap.Repair(100f), Is.Zero);
    }

    [Test]
    public void AllFourArmsHealImmediatelyAndWaitExactlyFourSeconds()
    {
        var cells = new[] { new Vector2Int(4, 6), new Vector2Int(6, 4), new Vector2Int(4, 3), new Vector2Int(3, 4) };
        var targets = cells.Select(c => Place(grid, c, damage: 80f)).ToArray();
        repair.Tick(0f);
        Assert.That(targets.All(t => t.CurrentHealth == 45f), Is.True);
        for (int i = 0; i < 4; i++) Assert.That(repair.GetRepairCount(i), Is.EqualTo(1));
        repair.Tick(3.999f);
        Assert.That(targets.All(t => t.CurrentHealth == 45f), Is.True);
        repair.Tick(4f);
        Assert.That(targets.All(t => t.CurrentHealth == 70f), Is.True);
    }

    [Test]
    public void FullHealthDoesNotConsumeCooldownAndEachArmIsIndependent()
    {
        TrapInstance front = Place(grid, new Vector2Int(4, 6));
        TrapInstance right = Place(grid, new Vector2Int(6, 4), damage: 70f);
        repair.Tick(0f);
        Assert.That(repair.GetRepairCount(0), Is.Zero);
        front.TakeDamage(80f);
        repair.Tick(1f);
        Assert.That(front.CurrentHealth, Is.EqualTo(45f));
        repair.Tick(4f);
        Assert.That(front.CurrentHealth, Is.EqualTo(45f));
        Assert.That(right.CurrentHealth, Is.EqualTo(80f));
        repair.Tick(5f);
        Assert.That(front.CurrentHealth, Is.EqualTo(70f));
    }

    [Test]
    public void SwitchingTargetsAndDisablingComponentCannotResetCooldown()
    {
        TrapInstance first = Place(grid, new Vector2Int(4, 6), damage: 80f);
        TrapInstance second = Place(grid, new Vector2Int(5, 6), damage: 60f);
        repair.Tick(0f);
        Assert.That(first.CurrentHealth, Is.EqualTo(45f));
        first.Repair(100f);
        repair.enabled = false; repair.enabled = true;
        repair.Tick(1f);
        Assert.That(second.CurrentHealth, Is.EqualTo(40f));
        repair.Tick(4f);
        Assert.That(second.CurrentHealth, Is.EqualTo(65f));
    }

    [Test]
    public void LowestHealthRatioThenDistanceWins()
    {
        TrapInstance near = Place(grid, new Vector2Int(4, 6), damage: 50f);
        TrapInstance far = Place(grid, new Vector2Int(5, 6), Definition(1, 2), damage: 70f);
        repair.Tick(0f);
        Assert.That(far.CurrentHealth, Is.EqualTo(55f));
        Assert.That(near.CurrentHealth, Is.EqualTo(50f));
        Assert.That(repair.GetTarget(0), Is.EqualTo(far));
        near.Repair(5f);
        repair.Tick(4f);
        Assert.That(near.CurrentHealth, Is.EqualTo(80f));
        Assert.That(far.CurrentHealth, Is.EqualTo(55f));
    }

    [Test]
    public void SelfDiagonalAndSeparatedTrapsAreNotRepairable()
    {
        station.TakeDamage(50f);
        TrapInstance diagonal = Place(grid, new Vector2Int(6, 6), damage: 50f);
        TrapInstance distant = Place(grid, new Vector2Int(4, 8), damage: 50f);
        repair.Tick(0f);
        Assert.That(station.CurrentHealth, Is.EqualTo(50f));
        Assert.That(diagonal.CurrentHealth, Is.EqualTo(50f));
        Assert.That(distant.CurrentHealth, Is.EqualTo(50f));
    }

    [Test]
    public void CoplanarAdjacentGridIsSupportedButDifferentHeightIsRejected()
    {
        TrapPlacementGrid other = Grid(grid.transform.position + Vector3.forward * 6f);
        TrapInstance target = Place(other, new Vector2Int(4, 0), damage: 80f);
        repair.Tick(0f);
        Assert.That(target.CurrentHealth, Is.EqualTo(45f));
        other.transform.position += Vector3.up;
        other.RebuildOccupancy();
        repair.Tick(4f);
        Assert.That(target.CurrentHealth, Is.EqualTo(45f));
    }

    [Test]
    public void RotatedGridSharedEdgesKeepCorrectArmAssignment()
    {
        grid.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
        grid.RebuildOccupancy();
        TrapInstance front = Place(grid, new Vector2Int(4, 6), damage: 60f);
        repair.Tick(0f);
        Assert.That(front.CurrentHealth, Is.EqualTo(65f));
        // Ground traps retain world rotation; this grid's local front is world right.
        Assert.That(repair.GetTarget(1), Is.EqualTo(front));
    }

    [Test]
    public void LargerTargetReceivesOneRepairAndCoarseGridOccupancyIsUsed()
    {
        TrapDefinition wide = Definition(3, 1);
        TrapInstance target = Place(grid, new Vector2Int(3, 6), wide, 80f);
        repair.Tick(0f);
        Assert.That(target.CurrentHealth, Is.EqualTo(45f));
        Assert.That(Enumerable.Range(0, 4).Sum(repair.GetRepairCount), Is.EqualTo(1));
        Assert.That(definition.TryGetFootprint(1.5f, out Vector2Int footprint), Is.True);
        Assert.That(footprint, Is.EqualTo(new Vector2Int(2, 2)));
    }

    [TestCase(TrapMountType.Wall)]
    [TestCase(TrapMountType.Ceiling)]
    public void MountedTargetsAreRejected(TrapMountType type)
    {
        TrapInstance target = Place(grid, new Vector2Int(4, 6), damage: 50f);
        var so = new SerializedObject(targetDefinition);
        so.FindProperty("mountType").enumValueIndex = (int)type;
        so.ApplyModifiedPropertiesWithoutUndo();
        repair.Tick(0f);
        Assert.That(target.CurrentHealth, Is.EqualTo(50f));
    }

    [Test]
    public void PreviewSourceAndTargetCannotHealOrBeHealed()
    {
        TrapInstance target = Place(grid, new Vector2Int(4, 6), damage: 80f);
        Object.DestroyImmediate(station.gameObject.AddComponent<TrapPlacementPreview>());
        target.gameObject.AddComponent<TrapPlacementPreview>();
        repair.Tick(0f);
        Assert.That(target.CurrentHealth, Is.EqualTo(20f));
        Object.DestroyImmediate(target.GetComponent<TrapPlacementPreview>());
        station.gameObject.AddComponent<TrapPlacementPreview>();
        repair.Tick(1f);
        Assert.That(target.CurrentHealth, Is.EqualTo(20f));
    }

    [Test]
    public void DisabledDisplacedAndRemovedTargetsStopImmediately()
    {
        TrapInstance target = Place(grid, new Vector2Int(4, 6), damage: 80f);
        repair.Tick(0f);
        target.transform.position += Vector3.forward;
        repair.Tick(4f);
        Assert.That(target.CurrentHealth, Is.EqualTo(45f));
        grid.RebuildOccupancy();
        target.enabled = false;
        repair.Tick(5f);
        Assert.That(target.CurrentHealth, Is.EqualTo(45f));
        target.enabled = true;
        grid.RemoveTrap(target);
        Assert.DoesNotThrow(() => repair.Tick(6f));
        Assert.That(repair.GetTarget(0), Is.Null);
    }

    [Test]
    public void OtherRepairStationsCanBeRepairedAndHealthIsCapped()
    {
        TrapInstance target = Place(grid, new Vector2Int(4, 6), definition, 10f);
        repair.Tick(0f);
        Assert.That(target.CurrentHealth, Is.EqualTo(100f));
        repair.Tick(4f);
        Assert.That(repair.GetRepairCount(0), Is.EqualTo(1));
    }

    [Test]
    public void ArmActionUsesBoundJointsAndReturnsToRest()
    {
        Place(grid, new Vector2Int(4, 6), damage: 80f);
        Assert.That(repair.HasBoundArms, Is.True);
        var rigs = (RepairStationTrap.ArmRig[])typeof(RepairStationTrap).GetField("arms", Private).GetValue(repair);
        Quaternion rest = rigs[0].yaw.localRotation;
        Vector3 scale = rigs[0].elbow.localScale;
        repair.Tick(0f); repair.Tick(.3f);
        Assert.That(Quaternion.Angle(rest, rigs[0].yaw.localRotation), Is.GreaterThan(80f));
        Assert.That(rigs[0].elbow.localScale, Is.EqualTo(scale));
        repair.Tick(1.01f);
        Assert.That(Quaternion.Angle(rest, rigs[0].yaw.localRotation), Is.LessThan(.01f));
    }

    [Test]
    public void PrefabMaterialsDefinitionAndCatalogueAreWired()
    {
        RepairStationAuthoring.ValidateBatch();
        Assert.That(Resources.LoadAll<TrapDefinition>(""), Does.Contain(definition));
        Assert.That(repair.RepairAmount, Is.EqualTo(25f));
        Assert.That(repair.CooldownSeconds, Is.EqualTo(4f));
        Assert.That(station.MaxHealth, Is.EqualTo(100f));
    }

    [UnityTest]
    public IEnumerator PauseDoesNotTriggerInitialRepairOrAdvanceAction()
    {
        TearDown();
        yield return new EnterPlayMode();
        var runtimeRoot = new GameObject("Runtime Repair Pause Test");
        runtimeRoot.transform.position = new Vector3(20000f, 0f, 20000f);
        var runtimeGrid = runtimeRoot.AddComponent<TrapPlacementGrid>();
        runtimeGrid.ConfigureLayout(12, 12, 1f, 0f, Enumerable.Repeat(true, 144).ToArray(), TrapPlacementGrid.SurfaceKind.Ground);
        var runtimeDefinition = Resources.Load<TrapDefinition>("RepairStation");
        Assert.That(runtimeGrid.TryPlaceTrap(new Vector2Int(4, 4), runtimeDefinition, out TrapInstance runtimeSource, out string failure), Is.True, failure);
        Assert.That(runtimeGrid.TryPlaceTrap(new Vector2Int(4, 6), runtimeDefinition, out TrapInstance runtimeTarget, out failure), Is.True, failure);
        runtimeTarget.TakeDamage(80f);
        Time.timeScale = 0f;
        float health = runtimeTarget.CurrentHealth;
        yield return null;
        yield return null;
        Assert.That(runtimeTarget.CurrentHealth, Is.EqualTo(health));
        Time.timeScale = 1f;
        yield return null;
        yield return null;
        Assert.That(runtimeTarget.CurrentHealth, Is.EqualTo(health + 25f));
        Time.timeScale = 0f;
        Quaternion pose = runtimeSource.GetComponentsInChildren<Transform>().First(t => t.name == "Shoulder_Yaw").localRotation;
        yield return new WaitForSecondsRealtime(.15f);
        Assert.That(runtimeSource.GetComponentsInChildren<Transform>().First(t => t.name == "Shoulder_Yaw").localRotation, Is.EqualTo(pose));
        Time.timeScale = 1f;
        Object.Destroy(runtimeRoot);
        yield return null;
        yield return new ExitPlayMode();
    }
}
#endif
