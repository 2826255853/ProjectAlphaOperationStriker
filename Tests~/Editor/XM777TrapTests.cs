#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public sealed class XM777TrapTests
{
    private readonly List<GameObject> objects = new List<GameObject>();
    private readonly List<ScriptableObject> assets = new List<ScriptableObject>();

    [TearDown]
    public void TearDown()
    {
        foreach (GameObject item in objects)
            if (item != null) Object.DestroyImmediate(item);
        objects.Clear();
        foreach (ScriptableObject asset in assets)
            if (asset != null) Object.DestroyImmediate(asset);
        assets.Clear();
    }

    [TestCase(5f, true)]
    [TestCase(95f, true)]
    [TestCase(4.99f, false)]
    [TestCase(95.01f, false)]
    public void SelectsGroundTargetsOnlyInsideInclusiveRange(float distance, bool expected)
    {
        XM777Trap trap = CreateTrap(out _);
        EnemyInstance enemy = CreateGroundEnemy(new Vector3(0f, 0f, distance));
        trap.Tick(0f);
        Assert.That(trap.CurrentTarget == enemy, Is.EqualTo(expected));
    }

    [Test]
    public void KeepsLivingTargetWhileCloserEnemyArrivesBeforeShot()
    {
        XM777Trap trap = CreateTrap(out _);
        EnemyInstance original = CreateGroundEnemy(new Vector3(0f, 0f, 50f));
        SetFloat(trap, "nextShotTime", 100f);
        trap.Tick(0.1f);

        EnemyInstance closer = CreateGroundEnemy(new Vector3(0f, 0f, 20f));
        trap.Tick(0.1f);

        Assert.That(trap.CurrentTarget, Is.SameAs(original));
        Assert.That(trap.ShotCount, Is.Zero);
        original.gameObject.SetActive(false);
        trap.Tick(0.01f);
        Assert.That(trap.CurrentTarget, Is.SameAs(closer), "A new target may be selected after the locked target is lost.");
    }

    [Test]
    public void ReplacesTooCloseTargetWithAnotherEnemyInArtilleryRange()
    {
        XM777Trap trap = CreateTrap(out _);
        EnemyInstance original = CreateGroundEnemy(new Vector3(0f, 0f, 20f));
        EnemyInstance replacement = CreateGroundEnemy(new Vector3(0f, 0f, 40f));
        SetFloat(trap, "nextShotTime", 100f);

        trap.Tick(0.1f);
        Assert.That(trap.CurrentTarget, Is.SameAs(original));

        original.transform.position = new Vector3(0f, 0f, 4.99f);
        trap.Tick(0.1f);

        Assert.That(trap.CurrentTarget, Is.SameAs(replacement),
            "An in-range enemy must replace a living target that moved inside XM777's minimum range.");
    }

    [Test]
    public void PredictsLeadAndDerivesFlightTimeFromFiveHundredMetresPerSecond()
    {
        XM777Trap trap = CreateTrap(out Transform muzzle);
        EnemyInstance enemy = CreateGroundEnemy(new Vector3(0f, 0f, 50f));
        SetFloat(trap, "nextShotTime", 100f);
        trap.Tick(0.1f);
        enemy.transform.position += Vector3.right;
        trap.Tick(0.1f);
        Assert.That(trap.CurrentTarget, Is.EqualTo(enemy));
        Assert.That(trap.HasInterceptPoint, Is.True);
        Assert.That(trap.TimeToImpact, Is.EqualTo(Vector3.Distance(muzzle.position, trap.InterceptPoint) / 500f).Within(0.0001f));
        Assert.That(trap.TimeToImpact, Is.EqualTo(Vector3.Distance(muzzle.position, enemy.transform.position) / 500f).Within(0.05f));
        Assert.That(Vector3.Distance(trap.InterceptPoint, enemy.transform.position), Is.GreaterThan(0.05f));
    }

    [TestCase("Wall")]
    [TestCase("Ground")]
    [TestCase("Ceiling")]
    public void SolidOcclusionBlocksTargetAndBlastIgnoresOcclusion(string obstacleName)
    {
        XM777Trap trap = CreateTrap(out Transform muzzle);
        Vector3 targetPosition = obstacleName == "Ground" ? new Vector3(0f, -2f, 20f)
            : obstacleName == "Ceiling" ? new Vector3(0f, 3f, 20f)
            : new Vector3(0f, 0f, 20f);
        if (obstacleName == "Ground") muzzle.position = new Vector3(0f, 2f, 0f);
        EnemyInstance enemy = CreateGroundEnemy(targetPosition);
        GameObject wall = Track(new GameObject("Blocking " + obstacleName));
        BoxCollider obstacle = wall.AddComponent<BoxCollider>();
        if (obstacleName == "Ground")
        {
            wall.transform.position = new Vector3(0f, 0f, 10f);
            obstacle.size = new Vector3(10f, 0.2f, 30f);
        }
        else if (obstacleName == "Ceiling")
        {
            wall.transform.position = new Vector3(0f, 1.5f, 10f);
            obstacle.size = new Vector3(10f, 0.2f, 30f);
        }
        else
        {
            wall.transform.position = new Vector3(0f, 0.5f, 10f);
            obstacle.size = new Vector3(10f, 5f, 0.5f);
        }

        Physics.SyncTransforms();
        trap.Tick(0f);
        Assert.That(trap.CurrentTarget, Is.Null);
        wall.SetActive(false);
        Physics.SyncTransforms();
        trap.Tick(0.5f);
        Assert.That(trap.ShotCount, Is.EqualTo(1));
        wall.SetActive(true);
        Physics.SyncTransforms();
        trap.Tick(0.1f);
        Assert.That(enemy.GetComponent<EnemyHealth>().CurrentHealth, Is.EqualTo(55f).Within(0.001f));
    }

    [Test]
    public void FlyingEnemyIsIgnoredWhenGroundEnemyIsNotPresent()
    {
        XM777Trap trap = CreateTrap(out _);
        GameObject go = Track(new GameObject("Flying enemy"));
        go.transform.position = new Vector3(0f, 0f, 20f);
        go.AddComponent<EnemyInstance>();
        MonsterPathFollower follower = go.AddComponent<MonsterPathFollower>();
        follower.InitializeFlying(null, go.transform.position + Vector3.forward, 1f, 0f, null);
        EnemyHealth health = go.AddComponent<EnemyHealth>();
        Invoke(health, "Awake");

        trap.Tick(0.1f);
        Assert.That(trap.CurrentTarget, Is.Null);
        Assert.That(trap.ShotCount, Is.Zero);
    }

    [Test]
    public void FiresEveryThreeSecondsAndDoesNotCreateProjectileObjects()
    {
        Assert.That(XM777Trap.FireInterval, Is.EqualTo(3f));
        XM777Trap trap = CreateTrap(out _);
        CreateGroundEnemy(new Vector3(0f, 0f, 20f));
        trap.Tick(0.2f);
        Assert.That(trap.ShotCount, Is.EqualTo(1));
        Assert.That(trap.PendingImpactCount, Is.EqualTo(1));
        trap.Tick(2.98f);
        Assert.That(trap.ShotCount, Is.EqualTo(1));
        trap.Tick(0.02f);
        Assert.That(trap.ShotCount, Is.EqualTo(2));
        Assert.That(Object.FindObjectsByType<MissileProjectile>(FindObjectsInactive.Include), Is.Empty);
    }

    [TestCase(5f, true)]
    [TestCase(200f, true)]
    [TestCase(4.99f, false)]
    [TestCase(200.01f, false)]
    public void RG777SelectsTargetsInsideItsInclusiveFiveToTwoHundredMetreRange(float distance, bool expected)
    {
        XM777Trap trap = CreateTrap(out _);
        ActivateRG777(trap);
        EnemyInstance enemy = CreateGroundEnemy(new Vector3(0f, 0f, distance));
        SetFloat(trap, "nextShotTime", 100f);

        trap.Tick(0.1f);

        Assert.That(trap.CurrentTarget == enemy, Is.EqualTo(expected));
        Assert.That(trap.EffectiveMinimumRange, Is.EqualTo(5f));
        Assert.That(trap.EffectiveMaximumRange, Is.EqualTo(200f));
        Assert.That(trap.EffectiveShellSpeed, Is.EqualTo(1000f));
    }

    [TestCase(5f, 0f)]
    [TestCase(102.5f, 15f)]
    [TestCase(200f, 30f)]
    [TestCase(300f, 30f)]
    public void RG777BarrelElevationIsLinearFromZeroToThirtyDegrees(float distance, float expected)
    {
        XM777Trap trap = CreateTrap(out _);
        ActivateRG777(trap);
        Assert.That(trap.EffectiveElevationForDistance(distance), Is.EqualTo(expected).Within(0.0001f));
    }

    [Test]
    public void RG777UsesFiveTimesLevelOneDamageAndFiresEveryTwoSeconds()
    {
        XM777Trap trap = CreateTrap(out _);
        ActivateRG777(trap);
        Assert.That(trap.EffectiveExplosionDamage, Is.EqualTo(XM777Trap.BaseExplosionDamage * 5f));
        Assert.That(trap.EffectiveFireInterval, Is.EqualTo(2f));

        EnemyInstance target = CreateGroundEnemy(new Vector3(0f, 0f, 20f));
        SetFloat(target.GetComponent<EnemyHealth>(), "maxHealth", 10000f);
        Invoke(target.GetComponent<EnemyHealth>(), "Awake");
        trap.Tick(0.2f);
        Assert.That(trap.ShotCount, Is.EqualTo(1));
        trap.Tick(1.99f);
        Assert.That(trap.ShotCount, Is.EqualTo(1));
        Assert.That(target.GetComponent<EnemyHealth>().CurrentHealth, Is.EqualTo(9775f));
        trap.Tick(0.01f);
        Assert.That(trap.ShotCount, Is.EqualTo(2));
    }

    [Test]
    public void SwitchingFromXM777ToRG777AppliesTwoSecondCadenceToThePreviousShot()
    {
        XM777Trap trap = CreateTrap(out _);
        EnemyInstance target = CreateGroundEnemy(new Vector3(0f, 0f, 20f));
        SetFloat(target.GetComponent<EnemyHealth>(), "maxHealth", 10000f);
        Invoke(target.GetComponent<EnemyHealth>(), "Awake");
        trap.Tick(0.2f);
        Assert.That(trap.ShotCount, Is.EqualTo(1));

        ActivateRG777(trap);
        trap.Tick(1.9f);
        Assert.That(trap.ShotCount, Is.EqualTo(1));
        trap.Tick(0.1f);
        Assert.That(trap.ShotCount, Is.EqualTo(2));
    }

    [Test]
    public void FireAndImpactSpawnTheirAuthoredEffectsWithoutChangingDamageTiming()
    {
        XM777Trap trap = CreateTrap(out Transform muzzle);
        GameObject muzzlePrefab = CreateEffectPrefab("Test muzzle effect");
        GameObject impactPrefab = CreateEffectPrefab("Test impact effect");
        SetObject(trap, "muzzleEffectPrefab", muzzlePrefab);
        SetObject(trap, "impactEffectPrefab", impactPrefab);
        EnemyInstance enemy = CreateGroundEnemy(Vector3.forward * 20f);

        trap.Tick(0.2f);
        XM777VfxPlayback muzzleFx = FindPlayback(
            Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None), "Test muzzle effect");
        Assert.That(muzzleFx, Is.Not.Null);
        Track(muzzleFx.gameObject);
        Assert.That(muzzleFx.transform.position, Is.EqualTo(muzzle.position));
        Assert.That(Quaternion.Angle(muzzleFx.transform.rotation, muzzle.rotation), Is.LessThan(0.01f));
        Assert.That(trap.PendingImpactCount, Is.EqualTo(1));
        Assert.That(enemy.GetComponent<EnemyHealth>().CurrentHealth, Is.EqualTo(100f));
        Assert.That(FindPlayback(Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None), "Test impact effect"), Is.Null);

        trap.Tick(0.1f);
        Assert.That(trap.ExplosionCount, Is.EqualTo(1));
        Assert.That(trap.PendingImpactCount, Is.Zero);
        Assert.That(enemy.GetComponent<EnemyHealth>().CurrentHealth, Is.EqualTo(55f).Within(0.001f));
        XM777VfxPlayback impactFx = FindPlayback(
            Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None), "Test impact effect");
        Assert.That(impactFx, Is.Not.Null);
        Track(impactFx.gameObject);
        Assert.That(Vector3.Distance(impactFx.transform.position, trap.InterceptPoint), Is.LessThan(0.001f));
    }

    [Test]
    public void AuthoredEffectPlaybackCleansUpAfterItsParticleLifetime()
    {
        GameObject effect = Track(new GameObject("Finite XM777 effect"));
        XM777VfxPlayback playback = effect.AddComponent<XM777VfxPlayback>();
        ParticleSystem particles = effect.AddComponent<ParticleSystem>();
        ParticleSystem.MainModule main = particles.main;
        main.playOnAwake = false;
        main.loop = false;
        main.duration = 0.5f;
        main.startLifetime = 0.5f;
        playback.Play();

        playback.Advance(0.99f);
        Assert.That(effect == null, Is.False);
        playback.Advance(0.02f);
        Assert.That(effect == null, Is.True);
    }

    [Test]
    public void GeneratedEffectsAndXm777PrefabReferencesAreWired()
    {
        XM777VfxAuthoring.CreateEffects();
        XM777TrapAuthoring.CreateAssets();
        AssetDatabase.Refresh();
        GameObject muzzle = AssetDatabase.LoadAssetAtPath<GameObject>(XM777VfxAuthoring.MuzzlePrefabPath);
        GameObject generatedImpact = AssetDatabase.LoadAssetAtPath<GameObject>(XM777VfxAuthoring.ImpactPrefabPath);
        // The gameplay impact is resolved by one helper; the generated XM777_Impact is the
        // self-contained fallback and must stay valid even when the shipped explosion wins.
        GameObject wiredImpact = XM777VfxAuthoring.LoadImpactPrefab();
        GameObject trapPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(XM777TrapAuthoring.PrefabPath);
        Assert.That(muzzle, Is.Not.Null);
        Assert.That(generatedImpact, Is.Not.Null);
        Assert.That(generatedImpact.transform.Find("Ground Shockwave"), Is.Not.Null);
        Assert.That(muzzle.GetComponentsInChildren<ParticleSystem>(true).Length, Is.EqualTo(2));
        Assert.That(generatedImpact.GetComponentsInChildren<ParticleSystem>(true).Length, Is.EqualTo(4));
        XM777Trap trap = trapPrefab.GetComponent<XM777Trap>();
        Assert.That(trap.MuzzleEffectPrefab, Is.EqualTo(muzzle));
        Assert.That(wiredImpact, Is.Not.Null);
        Assert.That(trap.ImpactEffectPrefab, Is.EqualTo(wiredImpact));
        Assert.That(trap.ImpactEffectPrefab.GetComponentsInChildren<ParticleSystem>(true).Length,
            Is.GreaterThan(0));
        XM777VfxAuthoring.CreateEffects();
        Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>(XM777VfxAuthoring.ImpactPrefabPath), Is.EqualTo(generatedImpact));
    }

    [Test]
    public void PlacementPreviewKeepsXm777FiringEffectsDisabled()
    {
        GameObject source = Track(new GameObject("XM777 preview source with effects"));
        source.AddComponent<TrapInstance>();
        source.AddComponent<XM777Trap>();
        SetObject(source.GetComponent<XM777Trap>(), "muzzleEffectPrefab", CreateEffectPrefab("Preview muzzle effect"));
        SetObject(source.GetComponent<XM777Trap>(), "impactEffectPrefab", CreateEffectPrefab("Preview impact effect"));
        TrapDefinition definition = ScriptableObject.CreateInstance<TrapDefinition>();
        assets.Add(definition);
        SetObject(definition, "prefab", source);
        TrapPlacementController controller = Track(new GameObject("XM777 preview with effects"))
            .AddComponent<TrapPlacementController>();
        controller.EnterPlacementMode();
        controller.SelectedTrap = definition;
        GameObject preview = GetField<GameObject>(controller, "preview");
        Track(preview);
        XM777Trap artillery = preview.GetComponent<XM777Trap>();
        Assert.That(artillery.enabled, Is.False);
        artillery.Tick(1f);
        GameObject[] spawned = Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None);
        Assert.That(FindPlayback(spawned, "Preview muzzle effect"), Is.Null);
        Assert.That(FindPlayback(spawned, "Preview impact effect"), Is.Null);
    }

    private GameObject CreateEffectPrefab(string name)
    {
        GameObject prefab = Track(new GameObject(name));
        prefab.AddComponent<XM777VfxPlayback>();
        ParticleSystem particles = prefab.AddComponent<ParticleSystem>();
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        return prefab;
    }

    private static XM777VfxPlayback FindPlayback(GameObject[] objects, string name)
    {
        for (int i = 0; i < objects.Length; i++)
            if (objects[i] != null && objects[i].name == name + "(Clone)")
                return objects[i].GetComponent<XM777VfxPlayback>();
        return null;
    }

    [Test]
    public void TrackingSetsElevationFromCurrentEnemyHorizontalDistance()
    {
        XM777Trap trap = CreateTrap(out _);
        CreateGroundEnemy(new Vector3(20f, 0f, 20f));
        trap.Tick(0.5f);
        Assert.That(trap.BarrelElevationDegrees,
            Is.EqualTo(XM777Trap.ElevationForDistance(Mathf.Sqrt(800f))).Within(0.01f));
    }

    [Test]
    public void TraversesHorizontallyWhenTheImportedRigParentIsAxisRotated()
    {
        // Mirrors the generated prefab: the FBX keeps Blender's axis frame, so the
        // yaw pivot's parent ("XM777_Base_Root") is rotated -90 degrees around X and
        // its local up is a horizontal world axis. Using that axis as the yaw axis
        // rolled the gun sideways instead of traversing it.
        XM777Trap trap = CreateImportedAxisTrap(out Transform pivot, out Transform muzzle);

        Assert.That(Vector3.Dot(trap.YawAxisWorld, Vector3.up), Is.EqualTo(1f).Within(0.0001f),
            "The turret must traverse around a vertical axis, not the model import axis.");
        Assert.That(Vector3.Dot(pivot.parent.up, Vector3.up), Is.LessThan(0.5f),
            "Fixture guard: this rig reproduces the imported axis rotation.");

        Vector3 restAzimuth = Vector3.ProjectOnPlane(trap.BarrelDirectionWorld, Vector3.up).normalized;
        EnemyInstance enemy = CreateGroundEnemy(Vector3.zero
            + Quaternion.AngleAxis(80f, Vector3.up) * restAzimuth * 30f);
        Quaternion pivotBefore = pivot.rotation;

        for (int i = 0; i < 40; i++) trap.Tick(0.05f);

        Assert.That(trap.CurrentTarget, Is.EqualTo(enemy));
        Assert.That(trap.BarrelElevationDegrees, Is.EqualTo(12.5f).Within(0.05f),
            "At 30 metres elevation must follow the updated linear range, independently of imported axes.");

        // The applied pivot rotation must be a pure rotation about the vertical
        // axis; any tilt component means the gun rolled instead of traversing.
        Quaternion applied = pivot.rotation * Quaternion.Inverse(pivotBefore);
        applied.ToAngleAxis(out float appliedAngle, out Vector3 appliedAxis);
        Assert.That(Mathf.Abs(Vector3.Dot(appliedAxis.normalized, Vector3.up)), Is.EqualTo(1f).Within(0.001f),
            "The turret must not roll around its barrel axis.");
        Assert.That(appliedAngle, Is.InRange(70f, 90f));

        Vector3 barrelAzimuth = Vector3.ProjectOnPlane(trap.BarrelDirectionWorld, Vector3.up);
        Vector3 targetAzimuth = Vector3.ProjectOnPlane(enemy.transform.position - muzzle.position, Vector3.up);
        Assert.That(Vector3.Angle(barrelAzimuth, targetAzimuth), Is.LessThan(4f),
            "The barrel must swing onto the target in the horizontal plane.");
    }

    [Test]
    public void GeneratedPrefabTurnsAroundWorldUp()
    {
        XM777TrapAuthoring.CreateAssets();
        AssetDatabase.Refresh();
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(XM777TrapAuthoring.PrefabPath);
        Assert.That(prefab, Is.Not.Null);

        GameObject placed = Track(Object.Instantiate(prefab));
        placed.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        XM777Trap trap = placed.GetComponent<XM777Trap>();
        Invoke(trap, "Awake");

        Transform pivot = trap.YawPivot;
        Assert.That(pivot, Is.Not.Null);
        Assert.That(Vector3.Dot(trap.YawAxisWorld, Vector3.up), Is.EqualTo(1f).Within(0.0001f),
            "The generated prefab must turn around a vertical axis; the imported FBX "
            + "parent carries a -90 degree X rotation whose local up is horizontal.");

        // Drive the production aim path on the real prefab rig and check that the
        // applied pivot rotation is a pure rotation about the vertical axis.
        Quaternion before = pivot.rotation;
        Vector3 restAzimuth = Vector3.ProjectOnPlane(trap.BarrelDirectionWorld, Vector3.up).normalized;
        Vector3 point = placed.transform.position + Quaternion.AngleAxis(105f, Vector3.up) * restAzimuth * 40f;
        trap.AimAt(point, 5f);

        Quaternion applied = pivot.rotation * Quaternion.Inverse(before);
        applied.ToAngleAxis(out float appliedAngle, out Vector3 appliedAxis);
        Assert.That(Mathf.Abs(Vector3.Dot(appliedAxis.normalized, Vector3.up)), Is.EqualTo(1f).Within(0.001f),
            "The imported model axis frame must never become the traverse axis.");
        Assert.That(appliedAngle, Is.GreaterThan(90f), "The turret must actually have turned.");
        Assert.That(trap.BarrelElevationDegrees, Is.EqualTo(17.5f).Within(0.05f),
            "At 40 metres elevation must follow the updated linear range while yaw remains vertical.");
    }

    [Test]
    public void GeneratedPrefabAndResourcesDefinitionAreWired()
    {
        XM777TrapAuthoring.CreateAssets();
        AssetDatabase.Refresh();
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(XM777TrapAuthoring.PrefabPath);
        TrapDefinition definition = Resources.Load<TrapDefinition>("XM777");
        Assert.That(prefab, Is.Not.Null);
        Assert.That(definition, Is.Not.Null);
        Assert.That(definition.Prefab, Is.EqualTo(prefab));
        Assert.That(definition.TrapId, Is.EqualTo("xm777"));
        Assert.That(definition.Cost, Is.EqualTo(40));
        Assert.That(prefab.GetComponent<XM777Trap>(), Is.Not.Null);
        Assert.That(prefab.GetComponent<TrapInstance>(), Is.Not.Null);
        Assert.That(prefab.GetComponentInChildren<XM777Trap>(true).Muzzle, Is.Not.Null);
        Assert.That(prefab.GetComponent<XM777Trap>().PitchPivot, Is.Not.Null);
        Assert.That(prefab.GetComponentInChildren<MissileProjectile>(true), Is.Null);
        Assert.That(prefab.GetComponentsInChildren<Camera>(true), Is.Empty,
            "A placed XM777 must never introduce a rendering camera.");
        Assert.That(prefab.GetComponentsInChildren<Light>(true), Is.Empty);
        Assert.That(prefab.GetComponentsInChildren<AudioListener>(true), Is.Empty);
        var importer = (ModelImporter)AssetImporter.GetAtPath(XM777TrapAuthoring.ModelPath);
        Assert.That(importer.importCameras, Is.False);
        Assert.That(importer.importLights, Is.False);
        GameObject placed = Track(Object.Instantiate(prefab));
        Assert.That(placed.GetComponentsInChildren<Camera>(true), Is.Empty);
        Assert.That(placed.GetComponentsInChildren<Light>(true), Is.Empty);
    }

    [Test]
    public void PlacementPreviewDisablesArtilleryBehavior()
    {
        GameObject source = Track(new GameObject("XM777 preview source"));
        source.AddComponent<XM777Trap>();
        TrapDefinition definition = ScriptableObject.CreateInstance<TrapDefinition>();
        assets.Add(definition);
        var definitionSerialized = new SerializedObject(definition);
        definitionSerialized.FindProperty("prefab").objectReferenceValue = source;
        definitionSerialized.ApplyModifiedPropertiesWithoutUndo();

        TrapPlacementController controller = Track(new GameObject("XM777 placement preview test"))
            .AddComponent<TrapPlacementController>();
        controller.EnterPlacementMode();
        controller.SelectedTrap = definition;

        GameObject preview = GetField<GameObject>(controller, "preview");
        Assert.That(preview, Is.Not.Null);
        Track(preview);
        XM777Trap artillery = preview.GetComponent<XM777Trap>();
        Assert.That(artillery, Is.Not.Null);
        Assert.That(artillery.enabled, Is.False);
        Assert.That(artillery.ShotCount, Is.Zero);
    }

    [TestCase(true)]
    [TestCase(false)]
    public void ShowingPreviewCannotActivateImportedPresentationComponents(bool stagingInitiallyActive)
    {
        // Reproduce a legacy model with a Blender camera, including hidden
        // children that could be enabled after the ghost is first created.
        GameObject source = Track(new GameObject("Legacy XM777 model"));
        GameObject staging = new GameObject("PreviewCamera");
        staging.transform.SetParent(source.transform, false);
        staging.AddComponent<Camera>();
        staging.AddComponent<AudioListener>();
        staging.AddComponent<Light>();
        staging.SetActive(stagingInitiallyActive);
        TrapDefinition definition = ScriptableObject.CreateInstance<TrapDefinition>();
        assets.Add(definition);
        SetObject(definition, "prefab", source);

        Camera playerView = Track(new GameObject("Player view")).AddComponent<Camera>();
        Camera droneView = Track(new GameObject("Drone view")).AddComponent<Camera>();
        playerView.transform.SetPositionAndRotation(new Vector3(2f, 1.7f, -4f), Quaternion.Euler(25f, 0f, 0f));
        droneView.transform.SetPositionAndRotation(new Vector3(2f, 12f, -4f), Quaternion.Euler(65f, 0f, 0f));
        TrapPlacementController controller = Track(new GameObject("Presentation isolation test"))
            .AddComponent<TrapPlacementController>();
        controller.EnterPlacementMode();
        controller.SelectedTrap = definition;
        GameObject preview = Track(GetField<GameObject>(controller, "preview"));
        Assert.That(preview, Is.Not.Null);
        preview.transform.Find("PreviewCamera").gameObject.SetActive(true);

        // Both control modes use this preview visibility path after a grid hit.
        foreach (Camera view in new[] { playerView, droneView })
        {
            SetObject(controller, "placementCamera", view);
            Vector3 position = view.transform.position;
            Quaternion rotation = view.transform.rotation;
            for (int i = 0; i < 2; i++)
            {
                Invoke(controller, "SetPreviewVisible", true);
                Assert.That(preview.activeInHierarchy, Is.True);
                foreach (Camera camera in preview.GetComponentsInChildren<Camera>(true))
                    Assert.That(camera.isActiveAndEnabled, Is.False);
                foreach (AudioListener listener in preview.GetComponentsInChildren<AudioListener>(true))
                    Assert.That(listener.isActiveAndEnabled, Is.False);
                foreach (Light light in preview.GetComponentsInChildren<Light>(true))
                    Assert.That(light.isActiveAndEnabled, Is.False);
                Assert.That(controller.PlacementCamera, Is.SameAs(view));
                Assert.That(view.enabled, Is.True);
                Assert.That(view.transform.position, Is.EqualTo(position));
                Assert.That(view.transform.rotation, Is.EqualTo(rotation));
                Invoke(controller, "SetPreviewVisible", false);
            }
        }
        Assert.That(source.GetComponentInChildren<Camera>(true).enabled, Is.True,
            "Preview isolation must not mutate the source model.");
    }

    [TestCase(0f, 0f)]
    [TestCase(5f, 0f)]
    [TestCase(15f, 5f)]
    [TestCase(50f, 22.5f)]
    [TestCase(95f, 45f)]
    [TestCase(120f, 45f)]
    public void DistanceElevationMappingIsLinearAndClamped(float distance, float expected)
    {
        Assert.That(XM777Trap.ElevationForDistance(distance), Is.EqualTo(expected).Within(0.0001f));
    }

    [TestCase(-30f, true)]
    [TestCase(0f, true)]
    [TestCase(30f, true)]
    [TestCase(-30.01f, false)]
    [TestCase(30.01f, false)]
    public void ActualFiringElevationUsesMinusThirtyToPlusThirtyArc(float elevation, bool expected)
    {
        Assert.That(XM777Trap.IsWithinFiringElevation(elevation), Is.EqualTo(expected));
    }

    [TestCase(-30f, true)]
    [TestCase(30f, true)]
    [TestCase(-30.1f, false)]
    [TestCase(30.1f, false)]
    public void ActualFireArcGatesTargetsWithoutChangingModelPitch(float elevation, bool canFire)
    {
        XM777Trap trap = CreateTrap(out _);
        float horizontalDistance = 20f;
        float targetHeight = Mathf.Tan(elevation * Mathf.Deg2Rad) * horizontalDistance;
        CreateGroundEnemy(new Vector3(0f, targetHeight, horizontalDistance));

        trap.Tick(1f);

        Assert.That(trap.ShotCount, Is.EqualTo(canFire ? 1 : 0));
        Assert.That(trap.BarrelElevationDegrees,
            Is.EqualTo(XM777Trap.ElevationForDistance(horizontalDistance)).Within(0.01f),
            "The visual barrel keeps its existing distance-based 0..45 degree pitch.");
    }

    [TestCase(15f, 5f)]
    [TestCase(50f, 22.5f)]
    [TestCase(95f, 45f)]
    public void GeneratedRigKeepsDistanceElevationAcrossAllAzimuths(float distance, float expected)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(XM777TrapAuthoring.PrefabPath);
        XM777Trap trap = Track(Object.Instantiate(prefab)).GetComponent<XM777Trap>();
        trap.transform.SetPositionAndRotation(new Vector3(7f, 3f, 4f), Quaternion.Euler(0f, 37f, 0f));
        Invoke(trap, "Awake");
        foreach (float yaw in new[] { 0f, 45f, 90f, 180f, 270f })
        {
            Vector3 point = trap.transform.position + Quaternion.Euler(0f, yaw, 0f) * Vector3.forward * distance;
            for (int step = 0; step < 6; step++) trap.AimAt(point, 5f);
            Assert.That(trap.BarrelElevationDegrees, Is.EqualTo(expected).Within(0.05f), $"yaw {yaw}");
            Assert.That(Vector3.Angle(Vector3.ProjectOnPlane(trap.Muzzle.forward, Vector3.up),
                Vector3.ProjectOnPlane(point - trap.Muzzle.position, Vector3.up)), Is.LessThan(3f));
        }
    }

    [TestCase(5f, 80f)]
    [TestCase(95f, 80f)]
    [TestCase(4.99f, 0f)]
    [TestCase(95.01f, 0f)]
    public void HeightDoesNotChangeHorizontalRangeOrElevation(float distance, float height)
    {
        XM777Trap trap = CreateTrap(out _);
        EnemyInstance enemy = CreateGroundEnemy(new Vector3(0f, height, distance));
        trap.Tick(1f);
        bool inRange = distance >= 5f && distance <= 95f;
        Assert.That(trap.CurrentTarget == enemy, Is.EqualTo(inRange));
        float requiredElevation = Mathf.Atan2(height, distance) * Mathf.Rad2Deg;
        bool insideFiringArc = XM777Trap.IsWithinFiringElevation(requiredElevation);
        Assert.That(trap.ShotCount, Is.EqualTo(inRange && insideFiringArc ? 1 : 0));
        if (inRange) Assert.That(trap.BarrelElevationDegrees,
            Is.EqualTo(XM777Trap.ElevationForDistance(distance)).Within(0.01f));
    }

    [Test]
    public void PitchMovesSmoothlyBetweenNearAndFarAndHoldsWithoutTarget()
    {
        XM777Trap trap = CreateImportedAxisTrap(out _, out _);
        Assert.That(trap.PitchPivot, Is.Not.Null, "Legacy references resolve the existing model pitch node.");
        trap.AimAt(Vector3.forward * 5f, 1f);
        Assert.That(trap.BarrelElevationDegrees, Is.EqualTo(0f).Within(0.01f));
        trap.AimAt(Vector3.forward * 95f, 0.05f);
        Assert.That(trap.BarrelElevationDegrees, Is.EqualTo(12f).Within(0.01f));
        trap.AimAt(Vector3.forward * 95f, 1f);
        Assert.That(trap.BarrelElevationDegrees, Is.EqualTo(45f).Within(0.01f));
        trap.Tick(1f);
        Assert.That(trap.BarrelElevationDegrees, Is.EqualTo(45f).Within(0.01f));
        trap.AimAt(Vector3.forward * 50f, 1f);
        Assert.That(trap.BarrelElevationDegrees, Is.EqualTo(22.5f).Within(0.01f));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void BothPitchAndYawMustBeAlignedBeforeFiring(bool slowPitch)
    {
        XM777Trap trap = CreateTrap(out _);
        SetFloat(trap, "pitchSpeedDegrees", slowPitch ? 1f : 10000f);
        SetFloat(trap, "yawSpeedDegrees", slowPitch ? 10000f : 1f);
        CreateGroundEnemy(new Vector3(20f, 0f, 0f));
        trap.Tick(0.1f);
        Assert.That(trap.HasInterceptPoint, Is.True);
        Assert.That(trap.ShotCount, Is.Zero);
        trap.Tick(100f);
        Assert.That(trap.ShotCount, Is.EqualTo(1));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void UpperOrSideProbeAllowsFireAroundSmallObstacles(bool upperRoute)
    {
        XM777Trap trap = CreateTrap(out _);
        EnemyInstance enemy = CreateGroundEnemy(Vector3.forward * 20f);
        GameObject obstacle = Track(new GameObject("Small view blocker"));
        obstacle.transform.position = Vector3.forward * 19f;
        obstacle.AddComponent<BoxCollider>().size = upperRoute
            ? new Vector3(4f, 0.2f, 0.2f) : new Vector3(0.2f, 6f, 0.2f);
        Physics.SyncTransforms();
        Assert.That(Physics.Raycast(Vector3.zero, Vector3.forward, 20f), Is.True,
            "Fixture guard: the original centre ray must be blocked.");
        trap.Tick(0.5f);
        Assert.That(trap.CurrentTarget, Is.SameAs(enemy));
        Assert.That(trap.HasInterceptPoint, Is.True);
        Assert.That(trap.ShotCount, Is.EqualTo(1));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void PredictedImpactUsesTheSameMultipleProbeRule(bool fullyBlocked)
    {
        XM777Trap trap = CreateTrap(out _);
        SetFloat(trap, "velocitySmoothingSeconds", 0f);
        SetFloat(trap, "nextShotTime", 100f);
        EnemyInstance enemy = CreateGroundEnemy(new Vector3(-20f, 0f, 50f));
        trap.Tick(0.1f);
        enemy.transform.position = new Vector3(0f, 0f, 50f);
        GameObject obstacle = Track(new GameObject("Predicted path blocker"));
        obstacle.transform.position = new Vector3(21.4f, 0f, 49f);
        obstacle.AddComponent<BoxCollider>().size = new Vector3(fullyBlocked ? 5f : 0.2f, 10f, 0.2f);
        Physics.SyncTransforms();
        trap.Tick(0.1f);
        Assert.That(trap.CurrentTarget, Is.SameAs(enemy), "The enemy's current location is visible.");
        Assert.That(trap.HasInterceptPoint, Is.EqualTo(!fullyBlocked));
        if (!fullyBlocked)
        {
            Assert.That(trap.InterceptPoint.x, Is.GreaterThan(10f), "Fixture guard: there must be significant lead.");
            Assert.That(trap.BarrelElevationDegrees, Is.EqualTo(22.5f).Within(0.01f),
                "Elevation uses the current enemy's 50 metres, independently of lead distance.");
        }
    }

    [Test]
    public void PredictionOutsideHorizontalRangeCannotFire()
    {
        XM777Trap trap = CreateTrap(out _);
        SetFloat(trap, "velocitySmoothingSeconds", 0f);
        SetFloat(trap, "nextShotTime", 100f);
        EnemyInstance enemy = CreateGroundEnemy(new Vector3(0f, 0f, 90f));
        trap.Tick(0.1f);
        enemy.transform.position += Vector3.right * 40f;
        trap.Tick(0.1f);
        Assert.That(trap.CurrentTarget, Is.SameAs(enemy), "A living locked target remains selected outside the firing range.");
        enemy.transform.position = new Vector3(0f, 0f, 90f);
        trap.Tick(0.1f);
        enemy.transform.position += Vector3.right * 40f;
        trap.Tick(0.1f);
        Assert.That(trap.CurrentTarget, Is.SameAs(enemy));
        Assert.That(trap.HasInterceptPoint, Is.False, "The predicted point is outside the base's horizontal range.");
        Assert.That(trap.ShotCount, Is.Zero);
    }

    [Test]
    public void SelfAndTriggerCollidersDoNotBlockEnemyColliderOrNearestSelection()
    {
        XM777Trap trap = CreateTrap(out _);
        EnemyInstance near = CreateGroundEnemy(Vector3.forward * 20f);
        near.gameObject.AddComponent<BoxCollider>();
        CreateGroundEnemy(Vector3.forward * 30f);
        GameObject self = new GameObject("Own collider");
        self.transform.SetParent(trap.transform, false);
        self.transform.localPosition = Vector3.forward * 5f;
        self.AddComponent<BoxCollider>().size = Vector3.one * 4f;
        GameObject trigger = Track(new GameObject("Trigger view blocker"));
        trigger.transform.position = Vector3.forward * 10f;
        BoxCollider collider = trigger.AddComponent<BoxCollider>();
        collider.size = Vector3.one * 5f;
        collider.isTrigger = true;
        Physics.SyncTransforms();
        trap.Tick(0.5f);
        Assert.That(trap.CurrentTarget, Is.SameAs(near));
        Assert.That(trap.ShotCount, Is.EqualTo(1));
    }

    private XM777Trap CreateTrap(out Transform muzzle)
    {
        GameObject root = Track(new GameObject("XM777 test trap"));
        TrapInstance instance = root.AddComponent<TrapInstance>();
        Invoke(instance, "Awake");
        Transform pivot = new GameObject("Yaw Pivot").transform;
        pivot.SetParent(root.transform, false);
        Transform pitch = new GameObject("XM777_Pitch_Root").transform;
        pitch.SetParent(pivot, false);
        pitch.localRotation = Quaternion.Euler(-30f, 0f, 0f);
        muzzle = new GameObject("Muzzle").transform;
        muzzle.SetParent(pitch, false);
        muzzle.localPosition = Vector3.zero;
        muzzle.localRotation = Quaternion.identity;
        XM777Trap weapon = root.AddComponent<XM777Trap>();
        SetObject(weapon, "yawPivot", pivot);
        SetObject(weapon, "muzzle", muzzle);
        SetVector(weapon, "restBarrelForwardLocal", Vector3.forward);
        Invoke(weapon, "Awake");
        return weapon;
    }

    /// <summary>
    /// Builds a trap rig that mirrors the generated prefab's imported axis frame:
    /// the yaw pivot's parent carries the FBX's -90 degree X rotation, so its local
    /// up is a horizontal world axis.
    /// </summary>
    private XM777Trap CreateImportedAxisTrap(out Transform pivot, out Transform muzzle)
    {
        GameObject root = Track(new GameObject("XM777 imported axis trap"));
        TrapInstance instance = root.AddComponent<TrapInstance>();
        Invoke(instance, "Awake");

        Transform modelRoot = new GameObject("XM777_Base_Root").transform;
        modelRoot.SetParent(root.transform, false);
        modelRoot.localRotation = Quaternion.Euler(-90f, 0f, 0f);

        pivot = new GameObject("Yaw Pivot").transform;
        pivot.SetParent(modelRoot, false);
        pivot.localRotation = Quaternion.identity;

        Transform pitch = new GameObject("XM777_Pitch_Root").transform;
        pitch.SetParent(pivot, false);
        pitch.localRotation = Quaternion.Euler(45f, 0f, 0f);

        muzzle = new GameObject("Muzzle").transform;
        muzzle.SetParent(pitch, false);
        muzzle.localPosition = new Vector3(0f, 2.8f, 0f);
        muzzle.localRotation = Quaternion.identity;

        XM777Trap weapon = root.AddComponent<XM777Trap>();
        SetObject(weapon, "yawPivot", pivot);
        SetObject(weapon, "muzzle", muzzle);
        SetVector(weapon, "restBarrelForwardLocal",
            pivot.InverseTransformDirection(muzzle.forward).normalized);
        Invoke(weapon, "Awake");
        return weapon;
    }

    private EnemyInstance CreateGroundEnemy(Vector3 position)
    {
        GameObject go = Track(new GameObject("Ground enemy"));
        go.transform.position = position;
        EnemyInstance enemy = go.AddComponent<EnemyInstance>();
        go.AddComponent<MonsterPathFollower>();
        EnemyHealth health = go.AddComponent<EnemyHealth>();
        Invoke(health, "Awake");
        return enemy;
    }

    private static void ActivateRG777(XM777Trap weapon)
    {
        TrapInstance trap = weapon.GetComponent<TrapInstance>();
        FieldInfo field = typeof(TrapInstance).GetField("upgradeState", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null);
        field.SetValue(trap, new TrapUpgradeState(4, TrapUpgradeDirection.Branch, XM777Trap.RG777BranchId));
    }

    private GameObject Track(GameObject item)
    {
        objects.Add(item);
        return item;
    }

    private static void SetObject(Object target, string name, Object value)
    {
        var serialized = new SerializedObject(target);
        serialized.FindProperty(name).objectReferenceValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetVector(Object target, string name, Vector3 value)
    {
        var serialized = new SerializedObject(target);
        serialized.FindProperty(name).vector3Value = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetFloat(Object target, string name, float value)
    {
        var serialized = new SerializedObject(target);
        serialized.FindProperty(name).floatValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetBool(Object target, string name, bool value)
    {
        var serialized = new SerializedObject(target);
        serialized.FindProperty(name).boolValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static T GetField<T>(object target, string name)
    {
        FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, $"Missing field {name}");
        return (T)field.GetValue(target);
    }

    private static void Invoke(object target, string name, params object[] arguments)
    {
        MethodInfo method = target.GetType().GetMethod(name,
            BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic, null,
            System.Array.ConvertAll(arguments, argument => argument.GetType()), null);
        Assert.That(method, Is.Not.Null, $"Missing method {name}");
        method.Invoke(target, arguments);
    }
}
#endif
