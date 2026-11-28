#if ENABLE_DRONE_CODE
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public sealed class PlayerDroneCrosshairPlayTests
{
    [UnityTearDown]
    public IEnumerator LeaveGame()
    {
        if (Application.isPlaying) yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator PlayerAndDroneAimAtOpenCellsAndLoseTheGhostOverAir()
    {
        yield return new EnterPlayMode();
        yield return null;
        var menu = Object.FindAnyObjectByType<GameStartMenu>();
        if (menu != null && GameStartMenu.CursorOwned) menu.SendMessage("Close");
        yield return null;
        var placement = Object.FindAnyObjectByType<TrapPlacementController>();
        var session = DroneControlSession.ActiveSession;
        Assert.That(session, Is.Not.Null);
        var grid = new GameObject("Crosshair pad").AddComponent<TrapPlacementGrid>();
        grid.transform.position = new Vector3(1000f, 30f, 1000f);
        grid.ConfigureLayout(2, 2, 1f, 0f, new[] { true, true, true, true });
        placement.EnterPlacementMode();
        try
        {
            foreach (bool drone in new[] { false, true })
            {
                if (drone) session.RequestToggle();
                Assert.That(session.IsDroneControlled, Is.EqualTo(drone));
                var camera = session.ControlCamera;
                Assert.That(placement.PlacementCamera, Is.SameAs(camera));
                grid.GetPlacementPose(Vector2Int.zero, placement.SelectedTrap, out var position, out _);
                camera.transform.SetPositionAndRotation(position + Vector3.up * 4f, Quaternion.Euler(90f, 0f, 0f));
                placement.SendMessage("Update");
                var ghost = (GameObject)typeof(TrapPlacementController).GetField("preview",
                    BindingFlags.NonPublic | BindingFlags.Instance).GetValue(placement);
                Assert.That(placement.ActiveGrid, Is.SameAs(grid));
                Assert.That(ghost.activeSelf, Is.True);
                Assert.That(ghost.transform.position, Is.EqualTo(position));
                Assert.That(grid.TryGetFootprint(placement.SelectedTrap, out var size), Is.True);
                var cornerOrigin = Vector2Int.one - size;
                var cornerPosition = grid.GetTrapWorldPosition(cornerOrigin, size);
                camera.transform.position = cornerPosition + Vector3.up * 4f;
                placement.SendMessage("Update");
                Assert.That(ghost.activeSelf, Is.True);
                Assert.That(ghost.transform.position, Is.EqualTo(cornerPosition));
                Assert.That(placement.OnProjectionGunPrimaryAction(), Is.False);
                camera.transform.rotation = Quaternion.identity;
                placement.SendMessage("Update");
                Assert.That(ghost.activeSelf, Is.False);
                Assert.That(placement.OnProjectionGunPrimaryAction(), Is.False);
            }
        }
        finally
        {
            Object.Destroy(grid.gameObject);
        }
        yield return new ExitPlayMode();
    }
}
#endif
