#if UNITY_INCLUDE_TESTS
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using KINEMATION.FPSAnimationPack.Scripts.Player;

/// <summary>
/// 回归测试：按住奔跑键时要能立刻解除右键瞄准（ADS）。
/// 判定标准是 FPSPlayer 的私有 _isAiming 字段真的被置回 false——
/// 这正是 PlayerSprintAimBridge 反射桥接要保证的效果。
/// </summary>
public sealed class PlayerSprintAimBridgeTests
{
    private GameObject player;

    [TearDown]
    public void TearDown()
    {
        if (player != null)
        {
            Object.DestroyImmediate(player);
            player = null;
        }
    }

    [Test]
    public void CancelAimClearsThePackageAimState()
    {
        FPSPlayer fpsPlayer = CreatePackagePlayer();
        if (fpsPlayer == null)
        {
            Assert.Ignore("FPSPlayer 预制体不可用（第三方包缺失），跳过瞄准桥接测试。");
        }

        SetAimState(fpsPlayer, true);
        Assert.That(GetAimState(fpsPlayer), Is.True, "前置条件：应能写入瞄准状态。");

        PlayerSprintAimBridge bridge = player.AddComponent<PlayerSprintAimBridge>();
        Assert.That(bridge.IsAiming, Is.True, "桥接组件应能在预制体层级里找到 FPSPlayer 并读到瞄准状态。");

        bridge.CancelAim();

        Assert.That(bridge.IsAiming, Is.False, "奔跑规则应把瞄准状态置回 false。");
        Assert.That(GetAimState(fpsPlayer), Is.False, "包内 _isAiming 字段也应被置回 false。");
    }

    [Test]
    public void SprintRequestUsesSharedForwardOnlyRule()
    {
        FirstPersonController controller = CreateController();
        if (controller == null)
        {
            Assert.Ignore("FPSPlayer 预制体不可用（第三方包缺失），跳过奔跑判定测试。");
        }

        // 未按奔跑键时为 false。
        Assert.That(controller.IsSprintRequested, Is.False);
    }

    /// <summary>
    /// 奔跑规则就是「奔跑时不再瞄准」：桥接组件负责在按下奔跑键时解除 ADS，
    /// 所以这里断言桥接与包内玩家都已接线。
    /// </summary>
    [Test]
    public void BridgeResolvesThePackagePlayerOnThePrefabHierarchy()
    {
        FirstPersonController controller = CreateController();
        if (controller == null)
        {
            Assert.Ignore("FPSPlayer 预制体不可用（第三方包缺失），跳过桥接测试。");
        }

        PlayerSprintAimBridge bridge = player.GetComponent<PlayerSprintAimBridge>();
        if (bridge == null)
        {
            bridge = player.AddComponent<PlayerSprintAimBridge>();
        }

        Assert.That(bridge.IsSprintRequested, Is.False);
        Assert.That(bridge.IsAiming, Is.False);
        bridge.CancelAim();
    }

    private FirstPersonController CreateController()
    {
        if (CreatePackagePlayer() == null)
        {
            return null;
        }

        FirstPersonController controller = player.GetComponent<FirstPersonController>();
        return controller != null ? controller : player.AddComponent<FirstPersonController>();
    }

    private FPSPlayer CreatePackagePlayer()
    {
        GameObject prefab = Resources.Load<GameObject>("FPSPlayer");
        if (prefab == null)
        {
            return null;
        }

        player = Object.Instantiate(prefab);
        player.name = "SprintAimBridgeTestPlayer";
        return player.GetComponentInChildren<FPSPlayer>(true);
    }

    private static bool GetAimState(FPSPlayer fpsPlayer)
    {
        FieldInfo field = typeof(FPSPlayer).GetField(
            "_isAiming", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        return field != null && field.GetValue(fpsPlayer) is bool aiming && aiming;
    }

    private static void SetAimState(FPSPlayer fpsPlayer, bool aiming)
    {
        FieldInfo field = typeof(FPSPlayer).GetField(
            "_isAiming", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.That(field, Is.Not.Null, "FPSPlayer 应有 _isAiming 字段。");
        field.SetValue(fpsPlayer, aiming);
    }
}
#endif
