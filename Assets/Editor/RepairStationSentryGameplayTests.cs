#if UNITY_INCLUDE_TESTS
using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

/// <summary>
/// 游戏内（Play Mode）验收：维修站能不能维修受损的哨戒机枪。
/// 使用随包预制体 RepairStation / AutoSentryTurret、真实运行时组件、真实 Update 循环，
/// 不手动调用 Tick，冷却按游戏时间 4 秒走。
/// </summary>
public sealed class RepairStationSentryGameplayTests
{
    private GameObject root;
    private TrapPlacementGrid grid;
    private TrapInstance station, sentry, diagonal;
    private RepairStationTrap repair;
    private TrapDefinition stationDefinition, sentryDefinition;

    [TearDown]
    public void TearDown()
    {
        Time.timeScale = 1f;
        if (root != null) Object.Destroy(root);
    }

    private TrapInstance Place(string label, Vector2Int cell, TrapDefinition definition)
    {
        Debug.Log("[repair-sentry] placing " + label + " at " + cell);
        try
        {
            if (!grid.TryPlaceTrap(cell, definition, out TrapInstance trap, out string failure))
                Assert.Fail($"placement of {label} at {cell} failed: {failure}");
            return trap;
        }
        catch (Exception exception) when (!(exception is AssertionException))
        {
            Assert.Fail($"placement of {label} at {cell} threw: {exception}");
            return null;
        }
    }

    [UnityTest, Timeout(180000)]
    public IEnumerator DamagedSentryGunIsRepairedByAdjacentRepairStation()
    {
        yield return new EnterPlayMode();

        root = new GameObject("Repair Station Sentry Acceptance");
        root.transform.position = new Vector3(20000f, 0f, 20000f);
        grid = root.AddComponent<TrapPlacementGrid>();
        grid.ConfigureLayout(16, 16, 1f, 0f, Enumerable.Repeat(true, 256).ToArray(),
            TrapPlacementGrid.SurfaceKind.Ground);

        stationDefinition = Resources.Load<TrapDefinition>("RepairStation");
        sentryDefinition = Resources.Load<TrapDefinition>("AutoSentryTurret");
        Assert.That(stationDefinition, Is.Not.Null, "缺少 Resources/RepairStation.asset");
        Assert.That(sentryDefinition, Is.Not.Null, "缺少 Resources/AutoSentryTurret.asset");

        // 2x2 维修站占 (4,4)..(5,5)；2x2 哨戒机枪占 (4,6)..(5,7) 与之正面共享整条边；
        // 第三座放在 (6,6)，只与维修站角接触，作为反例。
        station = Place("repair station", new Vector2Int(4, 4), stationDefinition);
        sentry = Place("sentry gun", new Vector2Int(4, 6), sentryDefinition);
        diagonal = Place("diagonal sentry gun", new Vector2Int(6, 6), sentryDefinition);

        repair = station.GetComponent<RepairStationTrap>();
        Assert.That(repair, Is.Not.Null, "维修站预制体缺少 RepairStationTrap");
        Assert.That(repair.HasBoundArms, Is.True, "维修站预制体应绑定四条机械臂");
        Assert.That(repair.RepairAmount, Is.EqualTo(25f));
        Assert.That(sentry.GetComponent<AutoSentryTurret>(), Is.Not.Null, "哨戒机枪预制体应带 AutoSentryTurret");
        Assert.That(sentry.Definition.DisplayName, Is.EqualTo("哨戒机枪"));

        float max = sentry.MaxHealth;
        Assert.That(max, Is.GreaterThan(80f));
        sentry.TakeDamage(80f);
        diagonal.TakeDamage(80f);
        Assert.That(sentry.CurrentHealth, Is.EqualTo(max - 80f));
        Assert.That(diagonal.CurrentHealth, Is.EqualTo(max - 80f));

        // 两帧交给游戏自己的 Update：维修站应立即展开并修一次。
        yield return null;
        yield return null;
        Assert.That(sentry.CurrentHealth, Is.EqualTo(max - 55f), "相邻哨戒机枪应在首帧获得 25 点维修");
        Assert.That(diagonal.CurrentHealth, Is.EqualTo(max - 80f), "仅斜角接触的哨戒机枪不应被维修");

        int side = -1;
        for (int i = 0; i < 4; i++) if (repair.GetTarget(i) == sentry) side = i;
        Assert.That(side, Is.GreaterThanOrEqualTo(0), "维修站应把受损哨戒机枪选为维修目标");
        Assert.That(repair.GetRepairCount(side), Is.EqualTo(1));
        Assert.That(Enumerable.Range(0, 4).Count(i => repair.GetTarget(i) == diagonal), Is.Zero,
            "斜角哨戒机枪不应占用维修臂");

        // 真实 4 秒冷却：到点前不再回血，到点后再回 25。
        yield return new WaitForSeconds(3.5f);
        Assert.That(sentry.CurrentHealth, Is.EqualTo(max - 55f), "冷却未结束前不应再次维修");
        yield return new WaitForSeconds(0.7f);
        Assert.That(sentry.CurrentHealth, Is.EqualTo(max - 30f), "冷却结束后应再修 25 点");
        Assert.That(repair.GetRepairCount(side), Is.EqualTo(2));

        // 被摧毁的目标失效，维修臂回到满血目标逻辑。
        diagonal.TakeDamage(1000f);
        yield return null;
        yield return null;
        Assert.That(diagonal == null || diagonal.IsDestroyed, Is.True);
        Assert.That(repair.GetTarget(side), Is.EqualTo(sentry));

        // 满血目标不消耗冷却、不重复计数，且不会修到超过上限。
        sentry.Repair(1000f);
        Assert.That(sentry.CurrentHealth, Is.EqualTo(max));
        yield return new WaitForSeconds(4.2f);
        Assert.That(sentry.CurrentHealth, Is.EqualTo(max));
        Assert.That(repair.GetRepairCount(side), Is.EqualTo(2), "满血目标不应消耗冷却");

        if (root != null) Object.Destroy(root);
        root = null;
        yield return new ExitPlayMode();
    }
}
#endif
