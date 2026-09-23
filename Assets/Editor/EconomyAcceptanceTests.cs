#if UNITY_INCLUDE_TESTS
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

/// <summary>Cross-system acceptance using shipped definitions and the actual gameplay scene.</summary>
public sealed class EconomyAcceptanceTests
{
    private const string ScenePath = "Assets/Scenes/THREE_ROUTE_MERGE_MAP.unity";

    [UnityTest]
    public IEnumerator MainSceneCompletesEconomyLoopAndReloadsWithFreshFunds()
    {
        yield return new EnterPlayMode();
        yield return VerifyMainScene();
    }

    private static IEnumerator VerifyMainScene()
    {
        Time.timeScale = 0f;
        AsyncOperation loading = EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
        while (!loading.isDone) yield return null;
        yield return null;
        Assert.That(SceneManager.GetSceneByPath(ScenePath).isLoaded, Is.True);
        var owned = new List<GameObject>();
        try
        {
            EnemySpawnPoint[] entrances = Object.FindObjectsByType<EnemySpawnPoint>();
            foreach (EnemySpawnPoint entrance in entrances) entrance.SpawningEnabled = false;
            Assert.That(entrances.Length, Is.GreaterThan(1), "Use the actual multi-entrance map.");
            EnemySpawnPoint ground = Array.Find(entrances, entrance => !entrance.IsFlyingEntrance
                && entrance.MonsterType == MonsterType.Ground);
            EnemySpawnPoint flying = Array.Find(entrances, entrance => entrance.IsFlyingEntrance);
            Assert.That(ground != null, Is.True);
            Physics.SyncTransforms();
            // Run the project's numeric grid alignment/surface diagnostic on the saved map.
            typeof(TrapPlacementGridEditor).GetMethod("ValidateTrapGrids", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, null);
            MonsterPathGrid pathGrid = Object.FindAnyObjectByType<MonsterPathGrid>();
            TrapPlacementGrid roadGrid = Array.Find(Object.FindObjectsByType<TrapPlacementGrid>(),
                candidate => candidate.name == "TrapPlacementGrid_Road");
            Assert.That(roadGrid.PlacementHeight, Is.EqualTo(TrapGridAuthoring.RoadPlacementHeight(pathGrid)).Within(0.001f),
                "Saved road height must agree with the generator's physical surface height.");
            EnemyCore core = ground.Core;
            Assert.That(core != null, Is.True);
            if (flying == null)
            {
                // The saved map has three ground entrances; exercise the shipped flying
                // prefab through a temporary runtime entrance without modifying that map.
                var flyingHost = new GameObject("Acceptance flying entrance");
                owned.Add(flyingHost);
                flyingHost.transform.position = ground.transform.position + Vector3.up * 12f;
                flying = flyingHost.AddComponent<EnemySpawnPoint>();
                flying.IsFlyingEntrance = true;
                flying.Core = core;
                flying.SpawningEnabled = false;
            }
            EconomyManager wallet = EconomyManager.GetOrCreate();
            Assert.That(wallet.Balance, Is.EqualTo(100));
            Guid originalRun = wallet.RunId;
            using (var view = new EconomyUIState())
            {
                view.Enable();
                EnemyInstance killed = ground.SpawnEnemy();
                owned.Add(killed.gameObject);
                Assert.That(killed.Resolution, Is.EqualTo(EnemyInstance.ResolutionState.Alive));
                // Hit a real spawned enemy through the same physics/damage bridge used by the FPS weapon.
                killed.transform.position = new Vector3(10000f, 1000f, 10000f);
                Physics.SyncTransforms();
                Collider body = Array.Find(killed.GetComponentsInChildren<Collider>(), collider => collider.enabled && !collider.isTrigger);
                Assert.That(body != null, Is.True, "The shipped ground enemy must be shootable.");
                var rig = new GameObject("Economy acceptance hitscan");
                owned.Add(rig);
                Camera camera = rig.AddComponent<Camera>();
                camera.enabled = false;
                rig.transform.position = body.bounds.center + Vector3.back * 5f;
                rig.transform.LookAt(body.bounds.center);
                var shooter = rig.AddComponent<FPSHitscanShooter>();
                Set(shooter, "observedStats", new WeaponDamageStats(WeaponClass.Unknown, 1000f, 100f, 200f, 1f, 1f, 1f, 1f));
                Invoke(shooter, "FireHitscan");
                Assert.That(killed.Resolution, Is.EqualTo(EnemyInstance.ResolutionState.Killed));
                Assert.That(wallet.Balance, Is.EqualTo(110));
                Assert.That(view.Balance, Is.EqualTo(110));

                EnemyInstance leaked = ground.SpawnEnemy();
                owned.Add(leaked.gameObject);
                float coreBeforeLeak = core.CurrentHealth;
                leaked.transform.position = core.Position;
                // Complete the final route leg through the real arrival event, without waiting for traversal.
                leaked.PathFollower.Initialize(null, core.transform, core.Position, Vector3.forward, 0f, core);
                Assert.That(leaked.Resolution, Is.EqualTo(EnemyInstance.ResolutionState.Leaked));
                Assert.That(core.CurrentHealth, Is.EqualTo(coreBeforeLeak - 2f));
                leaked.GetComponent<EnemyHealth>().TakeDamage(1000f);
                Assert.That(wallet.Balance, Is.EqualTo(110));

                TrapDefinition definition = Resources.Load<TrapDefinition>("AutoSentryTurret");
                var placed = new List<TrapInstance>();
                for (int i = 0; i < 2; i++)
                {
                    FindPlacement(definition, out TrapPlacementGrid grid, out Vector2Int cell);
                    Assert.That(TrapPurchaseService.TryPurchase(grid, cell, definition, out TrapInstance trap, out string failure),
                        Is.True, failure);
                    placed.Add(trap);
                    Assert.That(trap.GetComponent<AutoSentryTurret>() != null, Is.True,
                        "The built-in sentry fallback must produce a working weapon.");
                    Assert.That(grid.TryGetTrapAtCell(cell, out TrapInstance occupied) && occupied == trap, Is.True);
                    Assert.That(wallet.Balance, Is.EqualTo(i == 0 ? 70 : 30));
                }
                FindPlacement(definition, out TrapPlacementGrid rejectedGrid, out Vector2Int rejectedCell);
                int trapCount = rejectedGrid.PlacedTraps.Count;
                Assert.That(TrapPurchaseService.TryPurchase(rejectedGrid, rejectedCell, definition, out _, out string rejected), Is.False);
                Assert.That(rejected, Does.Contain("还差 10"));
                Assert.That(rejectedGrid.PlacedTraps.Count, Is.EqualTo(trapCount));
                Assert.That(wallet.Balance, Is.EqualTo(30));
                Assert.That(view.Balance, Is.EqualTo(30));
                Assert.That(Object.FindAnyObjectByType<EconomyHUD>().BalanceText, Is.EqualTo("金币：30"));
                Assert.That(Object.FindAnyObjectByType<TrapSelectionMenu>().BalanceText, Is.EqualTo("金币：30"));

                // Exercise an actual wave transition and pause without touching the wallet.
                ground.RestartWaves();
                var waveSettings = (List<EnemySpawnPoint.WaveSpawnSettings>)Get(ground, "waveSpawnSettings");
                waveSettings.Clear();
                Set(ground, "enemiesPerWave", 1);
                Assert.That(ground.TotalWaves, Is.GreaterThan(1));
                Invoke(ground, "SpawnNextWaveEnemy");
                ground.SpawningEnabled = false;
                Assert.That(ground.CurrentWave, Is.EqualTo(2));
                Assert.That(wallet.Balance, Is.EqualTo(30));
                yield return null;
                Assert.That(wallet.Balance, Is.EqualTo(30), "Pause and wave transition must not refill funds.");

                // One direct and one splash kill, found through colliders and the live-instance fallback.
                EnemyInstance direct = flying.SpawnEnemy();
                EnemyInstance splash = flying.SpawnEnemy();
                owned.Add(direct.gameObject);
                owned.Add(splash.gameObject);
                direct.transform.position = new Vector3(11000f, 1000f, 10000f);
                splash.transform.position = direct.transform.position + Vector3.right;
                direct.GetComponent<EnemyHealth>().TakeDamage(direct.GetComponent<EnemyHealth>().MaxHealth - 1f);
                splash.GetComponent<EnemyHealth>().TakeDamage(splash.GetComponent<EnemyHealth>().MaxHealth - 1f);
                var missileHost = new GameObject("Economy acceptance missile");
                owned.Add(missileHost);
                missileHost.transform.position = direct.transform.position;
                var missile = missileHost.AddComponent<MissileProjectile>();
                MissileLauncherWeapon tuning = Resources.Load<TrapDefinition>("DualMissileLauncherLoaded")
                    .Prefab.GetComponent<MissileLauncherWeapon>();
                missile.ConfigureTuning(tuning.DamagePerMissile, tuning.SplashRadius, tuning.SplashDamageRatio,
                    60f, 180f, 18f, MissileGuidanceMode.ContinuousLeadRefine, 0.1f);
                missile.Initialize(null, direct, direct.transform.position, Vector3.forward);
                Physics.SyncTransforms();
                missile.Detonate();
                missile.Detonate();
                Assert.That(direct.Resolution, Is.EqualTo(EnemyInstance.ResolutionState.Killed));
                Assert.That(splash.Resolution, Is.EqualTo(EnemyInstance.ResolutionState.Killed));
                Assert.That(wallet.Balance, Is.EqualTo(60), "Two flying kills grant exactly 15 each.");

                // Buying the shipped launcher and spikes also exercises their actual prefab/footprint setup.
                foreach (string name in new[] { "DualMissileLauncherLoaded", "GroundSpike" })
                {
                    wallet.BeginRun(40);
                    TrapDefinition actual = Resources.Load<TrapDefinition>(name);
                    FindPlacement(actual, out TrapPlacementGrid grid, out Vector2Int cell);
                    Assert.That(TrapPurchaseService.TryPurchase(grid, cell, actual, out TrapInstance trap, out string failure),
                        Is.True, name + ": " + failure);
                    Assert.That(trap.Definition, Is.SameAs(actual));
                    Assert.That(wallet.Balance, Is.Zero);
                    if (name == "GroundSpike")
                    {
                        camera.enabled = true;
                        camera.depth = 100;
                        rig.transform.position = trap.transform.position + new Vector3(4f, 3f, -4f);
                        rig.transform.LookAt(trap.transform.position);
                        yield return (IEnumerator)typeof(EconomyUITests).GetMethod("CapturePreview",
                            BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { "main-scene-ground-spike.png" });
                        camera.enabled = false;
                    }
                    Assert.That(grid.RemoveTrap(trap), Is.True);
                    Assert.That(wallet.Balance, Is.Zero, "Dismantling never refunds.");
                }
            }

            loading = EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
            while (!loading.isDone) yield return null;
            foreach (EnemySpawnPoint entrance in Object.FindObjectsByType<EnemySpawnPoint>()) entrance.SpawningEnabled = false;
            yield return null;
            EconomyManager restarted = EconomyManager.GetOrCreate();
            Assert.That(restarted.RunId, Is.Not.EqualTo(originalRun));
            Assert.That(restarted.Balance, Is.EqualTo(100));
            Assert.That(Object.FindAnyObjectByType<EconomyHUD>().BalanceText, Is.EqualTo("金币：100"));
            Assert.That(Object.FindAnyObjectByType<TrapSelectionMenu>().BalanceText, Is.EqualTo("金币：100"));
            Assert.That(Object.FindObjectsByType<EconomyHUD>().Length, Is.EqualTo(1));
            Assert.That(Object.FindObjectsByType<TrapSelectionMenu>().Length, Is.EqualTo(1));
            Debug.Log("ECONOMY_ACCEPTANCE_PASS: main scene, real hitscan kill, leak, 2 purchases, rejected third, wave/pause, missile splash rewards, all 3 shipped traps, scene restart.");
        }
        finally
        {
            Time.timeScale = 1f;
            for (int i = owned.Count - 1; i >= 0; i--)
                if (owned[i] != null) Object.Destroy(owned[i]);
        }
    }

    private static void FindPlacement(TrapDefinition definition, out TrapPlacementGrid grid, out Vector2Int cell)
    {
        var failures = new Dictionary<string, int>();
        foreach (TrapPlacementGrid candidate in Object.FindObjectsByType<TrapPlacementGrid>())
            for (int y = 0; y < candidate.Rows; y++)
                for (int x = 0; x < candidate.Columns; x++)
                {
                    var origin = new Vector2Int(x, y);
                    if (!candidate.CanPlaceTrap(origin, definition, out string failure))
                    {
                        string key = candidate.name + ": " + failure;
                        failures.TryGetValue(key, out int count);
                        failures[key] = count + 1;
                        if (count == 0 && failure.Contains("supporting floor"))
                        {
                            candidate.TryGetFootprint(definition, out Vector2Int footprint);
                            Vector3 point = candidate.GetTrapWorldPosition(origin, footprint);
                            string surfaces = string.Empty;
                            foreach (RaycastHit hit in Physics.RaycastAll(point + Vector3.up * 2f, Vector3.down, 4f))
                                surfaces += $" {hit.collider.name}@{hit.point.y:0.###}";
                            Debug.Log($"Placement support diagnostic: {candidate.name} {origin}, center={point}, surfaces:{surfaces}");
                        }
                        continue;
                    }
                    grid = candidate;
                    cell = origin;
                    return;
                }
        throw new AssertionException("No valid authored placement location for " + definition.DisplayName
            + ": " + string.Join("; ", failures));
    }

    private static object Get(object target, string name) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
    private static void Set(object target, string name, object value) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    private static object Invoke(object target, string name, params object[] args) =>
        target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);

    [Test]
    public void ShippedDefinitionsHaveConsistentPricesAndSupportedVisuals()
    {
        foreach (string name in new[] { "AutoSentryTurret", "DualMissileLauncherLoaded", "GroundSpike" })
        {
            TrapDefinition definition = Resources.Load<TrapDefinition>(name);
            Assert.That(definition != null, Is.True, name);
            Assert.That(definition.Cost, Is.EqualTo(40), name);
            Assert.That(definition.Prefab != null || definition.TrapId == "auto_sentry_turret", Is.True, name);
            TrapDefinition editorCopy = AssetDatabase.LoadAssetAtPath<TrapDefinition>("Assets/TrapDefinitions/" + name + ".asset");
            if (editorCopy != null)
            {
                Assert.That(editorCopy.TrapId, Is.EqualTo(definition.TrapId));
                Assert.That(editorCopy.Cost, Is.EqualTo(definition.Cost));
                Assert.That(editorCopy.Prefab, Is.EqualTo(definition.Prefab));
            }
        }
    }

    [UnityTearDown]
    public IEnumerator LeavePlayMode()
    {
        if (Application.isPlaying)
        {
            Time.timeScale = 1f;
            yield return new ExitPlayMode();
        }
    }
}
#endif
