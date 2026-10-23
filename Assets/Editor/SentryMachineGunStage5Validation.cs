using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// 哨戒机枪第五阶段验收：运行时开火反馈（后坐 + 枪口焰）、弹壳抛射与回收、
/// 激光瞄准线的截断/可见性，以及 <see cref="AutoSentryTurret"/> 的瞄准 + 扣血 +
/// 弹链节奏回归。全部为批处理内的确定性断言（固定步长 <see cref="AutoSentryTurret.Tick"/>），
/// 不保存场景、预制体或材质，也不依赖测试程序集。
///
/// 菜单：Tools/塔防/哨戒机枪/验收阶段 5 运行时动画
/// 批处理：-executeMethod SentryMachineGunStage5Validation.Run （-nographics 即可）
/// </summary>
public static class SentryMachineGunStage5Validation
{
    private const string ArtPrefabPath = "Assets/Resources/SentryMachineGun.prefab";
    private const string CasingPrefabPath = "Assets/Prefabs/SentryShellCasing.prefab";
    private const string ShippedTrapPrefabPath = "Assets/Prefabs/AutoSentryTurret.prefab";
    private const float FixedStep = 1f / 60f;

    private static readonly List<Object> owned = new List<Object>();
    private static int checks;
    private static int failures;
    private static int shotCount;

    [MenuItem("Tools/塔防/哨戒机枪/验收阶段 5 运行时动画")]
    public static int Run()
    {
        if (!Application.isBatchMode || Application.isPlaying)
            throw new InvalidOperationException("Run stage 5 acceptance in a batch editor outside Play mode.");

        checks = 0;
        failures = 0;
        try
        {
            ValidatePrefabWiring();
            Cleanup();

            ValidateTurretRegression();
            Cleanup();

            ValidateRecoil();
            Cleanup();

            ValidateShellEjector();
            Cleanup();

            ValidateLaserSight();
            Cleanup();

            if (failures > 0)
                throw new InvalidOperationException(
                    $"SENTRY_MG_STAGE5_FAIL: {failures}/{checks} assertions failed");

            Debug.Log($"SENTRY_MG_STAGE5_PASS: {checks} assertions");
            return checks;
        }
        finally
        {
            Cleanup();
        }
    }

    // --- 1. 预制体接线 -----------------------------------------------------

    private static void ValidatePrefabWiring()
    {
        GameObject art = AssetDatabase.LoadAssetAtPath<GameObject>(ArtPrefabPath);
        Check(art != null, "art prefab exists: " + ArtPrefabPath);
        if (art == null) return;

        GameObject loaded = Resources.Load<GameObject>("SentryMachineGun");
        Check(loaded != null && AssetDatabase.GetAssetPath(loaded) == ArtPrefabPath,
            "Resources.Load(\"SentryMachineGun\") resolves the generated art prefab");

        var feedback = art.GetComponent<SentryFireFeedback>();
        var ejector = art.GetComponent<SentryShellEjector>();
        var laser = art.GetComponent<SentryLaserSight>();
        Check(feedback != null, "art root carries SentryFireFeedback");
        Check(ejector != null, "art root carries SentryShellEjector");
        Check(laser != null, "art root carries SentryLaserSight");

        if (feedback != null)
        {
            Check(HasReference(feedback, "recoilRoot"), "fire feedback: recoilRoot wired");
            Check(HasReference(feedback, "muzzle"), "fire feedback: muzzle wired");
            Check(HasReference(feedback, "muzzleFlash"), "fire feedback: muzzleFlash wired");
            Check(Mathf.Approximately(feedback.RecoilDistance, 0.04f), "fire feedback: 0.04 m recoil travel");
            Check(Mathf.Approximately(feedback.RecoilLiftDegrees, 3f), "fire feedback: 3 degree muzzle lift");
            Check(Mathf.Approximately(feedback.SingleShotDuration, 0.11f), "fire feedback: 0.11 s single shot");
        }

        if (ejector != null)
        {
            Check(HasReference(ejector, "ejectionPort"), "shell ejector: ejectionPort wired");
            Check(HasReference(ejector, "casingPrefab"), "shell ejector: casingPrefab wired");
            Check(ejector.PoolSize == 32, "shell ejector: pool size is 32");
            Check(AssetDatabase.GetAssetPath(ejector.CasingPrefab) == CasingPrefabPath,
                "shell ejector: casing prefab is the shipped one");
        }

        if (laser != null)
        {
            Check(HasReference(laser, "laserEmitter"), "laser sight: laserEmitter wired");
            Check(HasReference(laser, "beamMaterial"), "laser sight: beamMaterial wired");
            Check(laser.MaxDistance > 0.1f, "laser sight: positive max distance");
        }

        foreach (string node in new[] { "Aim Pivot", "Muzzle", "Gun Recoil", "Ejection Port",
                     "Laser Emitter", "Muzzle Flash", "Leg_FL", "Leg_FR", "Leg_RL", "Leg_RR" })
        {
            Check(CountNamed(art.transform, node) == 1, "contract node present exactly once: " + node);
        }
    }

    // --- 2. 炮塔回归：瞄准 + 扣血 + 弹链 -----------------------------------

    private static void ValidateTurretRegression()
    {
        AutoSentryTurret turret = CreateTurret(Vector3.zero);
        Check(turret != null, "turret component created");
        if (turret == null) return;

        Transform artRoot = turret.transform.Find("Sentry Machine Gun Art");
        Check(artRoot != null, "Awake instantiates the machine gun art under the turret");
        Check(turret.AimPivot != null && turret.Muzzle != null, "AimPivot / Muzzle resolved from the art");
        Check(turret.Muzzle != null && turret.AimPivot != null && turret.Muzzle.IsChildOf(turret.AimPivot),
            "Muzzle lives under Aim Pivot");

        EnemyHealth victim = CreateEnemy(new Vector3(0f, 0f, 2.5f), 10000f);
        Check(victim != null && Mathf.Approximately(victim.CurrentHealth, victim.MaxHealth),
            "test victim starts at full health");
        float startHealth = victim.CurrentHealth;

        // 3.05 s of a 3 s burst at 8 rounds/s: 24 rounds nominal, alignment costs no time
        // because the victim already sits on the pivot's forward axis.
        int burstShots = Advance(turret, 3.05f);
        Check(burstShots >= 22 && burstShots <= 26,
            "burst cadence is ~8 rounds/s (got " + burstShots + " rounds in 3.05 s)");
        Check(shotCount == burstShots, "ShotFired raised once per round (" + shotCount + ")");
        float dealt = startHealth - victim.CurrentHealth;
        Check(Mathf.Abs(dealt - burstShots * 12f) < 0.01f,
            "each round deals 12 damage (dealt " + dealt + " for " + burstShots + " rounds)");
        Check(turret.IsCoolingDown, "burst ends in cooldown after burstDuration");

        int beforeCooldown = shotCount;
        int cooldownShots = Advance(turret, 0.5f);
        Check(cooldownShots == 0, "cooldown blocks firing (" + cooldownShots + " rounds)");

        int resumed = Advance(turret, 0.7f);
        Check(resumed >= 2, "firing resumes after the cooldown (" + resumed + " rounds)");
        Check(shotCount > beforeCooldown, "ShotFired keeps firing after the cooldown");

        // Yaw tracking: move the victim to the right flank and let the pivot follow.
        victim.transform.position = new Vector3(2.5f, 0f, 0f);
        Advance(turret, 0.8f);
        float rightDot = Vector3.Dot(turret.AimPivot.forward, Vector3.right);
        Check(rightDot > 0.99f, "Aim Pivot yaw follows the target to the right flank (dot " + rightDot + ")");

        // Losing every target resets the burst instead of stalling in cooldown.
        Object.DestroyImmediate(victim.gameObject);
        Advance(turret, 0.1f);
        Check(turret.CurrentTarget == null, "turret drops a destroyed target");
        int afterLoss = shotCount;
        Advance(turret, 0.3f);
        Check(shotCount == afterLoss, "no rounds are fired without a target");

        // The shipped definition references a legacy prefab by GUID. That prefab imports
        // as BrokenPrefabAsset (pre-existing, committed and untouched by this stage), and
        // TrapPlacementGrid.CreateFallbackTrap is the runtime path for exactly that case:
        // it creates a placeholder root and lets AutoSentryTurret build the art in Awake.
        TrapDefinition definition = Resources.Load<TrapDefinition>("AutoSentryTurret");
        Check(definition != null, "shipped TrapDefinition loads from Resources");
        Check(definition != null && definition.TrapId == "auto_sentry_turret",
            "shipped definition keeps trapId auto_sentry_turret");
        Check(definition != null && definition.Footprint == new Vector2Int(2, 2),
            "shipped definition keeps the 2x2 footprint");

        GameObject trapPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ShippedTrapPrefabPath);
        Check(trapPrefab != null,
            "shipped trap prefab imports as a GameObject (see SentryTrapPrefabRepair): " + ShippedTrapPrefabPath);
        if (trapPrefab != null)
        {
            Check(definition != null && definition.Prefab == trapPrefab,
                "Resources/AutoSentryTurret.asset resolves the shipped prefab");

            var shipped = Object.Instantiate(trapPrefab);
            shipped.name = "Stage 5 Shipped Sentry";
            owned.Add(shipped);
            AutoSentryTurret shippedTurret = shipped.GetComponent<AutoSentryTurret>();
            Check(shippedTurret != null, "shipped prefab has a live AutoSentryTurret component");
            Check(shipped.GetComponent<TrapInstance>() != null, "shipped prefab keeps TrapInstance on the root");
            Check(trapPrefab.transform.childCount == 0, "shipped prefab is stored as a bare root (no baked art)");

            if (shippedTurret != null)
            {
                Invoke(shippedTurret, "Awake");
                int artCopies = 0;
                foreach (Transform child in shipped.GetComponentsInChildren<Transform>(true))
                    if (child.name == "Sentry Machine Gun Art") artCopies++;
                Check(artCopies == 1, "a placed sentry builds exactly one art copy (" + artCopies + ")");
                Check(shippedTurret.AimPivot != null && shippedTurret.Muzzle != null,
                    "placed sentry resolves Aim Pivot and Muzzle from its own art");

                EnemyHealth shippedVictim = CreateEnemy(new Vector3(0f, 0f, 2f), 10000f);
                float shippedStart = shippedVictim.CurrentHealth;
                shippedTurret.ShotFired += _ => shotCount++;
                int shippedShots = Advance(shippedTurret, 0.3f);
                float shippedDealt = shippedStart - shippedVictim.CurrentHealth;
                Debug.Log("[SentryMachineGunStage5] shipped sentry: shots=" + shippedShots
                          + " dealt=" + shippedDealt
                          + " cooling=" + shippedTurret.IsCoolingDown
                          + " forward=" + (shippedTurret.AimPivot == null ? "n/a" : shippedTurret.AimPivot.forward.ToString()));
                Check(shippedShots > 0 && shippedDealt > 0f,
                    "placed sentry actually fires and damages a target (" + shippedShots + " rounds, " + shippedDealt + " damage)");
                Check(Mathf.Abs(shippedDealt - shippedShots * 12f) < 0.01f,
                    "placed sentry keeps the 12 damage per round contract");
            }
        }

        // Fallback path used in game when the definition prefab is unavailable.
        GameObject fallbackRoot = GameObject.CreatePrimitive(PrimitiveType.Cube);
        fallbackRoot.name = "Stage 5 Fallback Sentry";
        owned.Add(fallbackRoot);
        var fallbackTurret = fallbackRoot.AddComponent<AutoSentryTurret>();
        Invoke(fallbackTurret, "Awake");
        Check(fallbackRoot.transform.Find("Sentry Machine Gun Art") != null,
            "fallback turret loads the generated art through Resources.Load");
        Check(fallbackTurret.AimPivot != null && fallbackTurret.Muzzle != null,
            "fallback turret resolves Aim Pivot and Muzzle from the art");
        Check(fallbackRoot.transform.GetComponentsInChildren<SentryFireFeedback>(true).Length == 1
              && fallbackRoot.transform.GetComponentsInChildren<SentryShellEjector>(true).Length == 1
              && fallbackRoot.transform.GetComponentsInChildren<SentryLaserSight>(true).Length == 1,
            "fallback turret art carries one of each stage 5 component");
    }

    // --- 3. 后坐与枪口焰 ---------------------------------------------------

    private static void ValidateRecoil()
    {
        AutoSentryTurret turret = CreateTurret(Vector3.zero);
        Transform artRoot = turret != null ? turret.transform.Find("Sentry Machine Gun Art") : null;
        var feedback = artRoot != null ? artRoot.GetComponent<SentryFireFeedback>() : null;
        Check(feedback != null, "recoil: feedback component available on the art instance");
        if (feedback == null) return;

        Invoke(feedback, "Awake");
        Invoke(feedback, "OnEnable");

        Check(feedback.EvaluateRecoil01(0f) == 0f, "recoil curve is zero at progress 0");
        Check(feedback.EvaluateRecoil01(1f) == 0f, "recoil curve is zero at progress 1");
        float peak = 0f;
        float peakAt = -1f;
        for (int i = 0; i <= 100; i++)
        {
            float progress = i / 100f;
            float value = feedback.EvaluateRecoil01(progress);
            if (value > peak)
            {
                peak = value;
                peakAt = progress;
            }
        }
        Check(peak > 0.99f, "recoil curve reaches full amplitude (peak " + peak + ")");
        Check(peakAt >= 0.1f && peakAt <= 0.4f, "recoil peak lands between 0.1 and 0.4 (at " + peakAt + ")");

        Transform recoil = feedback.RecoilRoot;
        Check(recoil != null, "recoil: Gun Recoil node wired");
        if (recoil == null) return;
        Vector3 restPosition = recoil.localPosition;
        Quaternion restRotation = recoil.localRotation;

        GameObject flash = feedback.MuzzleFlash;
        Check(flash != null, "recoil: muzzle flash object wired");
        Vector3 flashRestScale = flash != null ? flash.transform.localScale : Vector3.one;
        Check(flash != null && !flash.activeSelf, "muzzle flash starts hidden");

        feedback.PlayShot();
        Check(feedback.IsRecoiling, "PlayShot starts the recoil cycle");
        Check(flash != null && flash.activeSelf, "PlayShot shows the muzzle flash");

        // 0.18 progress = the authored kick-in peak: -0.04 m and +3 degrees.
        feedback.Tick(feedback.SingleShotDuration * 0.18f);
        Vector3 expectedKick = restPosition + Vector3.back * feedback.RecoilDistance;
        Check(Vector3.Distance(recoil.localPosition, expectedKick) < 1e-4f,
            "recoil kicks 0.04 m along local -Z at the peak");
        float lift = Quaternion.Angle(recoil.localRotation, restRotation);
        Check(Mathf.Abs(lift - feedback.RecoilLiftDegrees) < 0.05f,
            "recoil lifts the muzzle by 3 degrees (got " + lift + ")");
        Check(flash != null && flash.activeSelf, "muzzle flash is still visible inside 0.045 s");

        feedback.Tick(0.05f);
        Check(!feedback.IsMuzzleFlashVisible, "muzzle flash clears after ~0.045 s");
        Check(flash != null && !flash.activeSelf, "muzzle flash object is hidden again");
        Check(flash != null && Vector3.Distance(flash.transform.localScale, flashRestScale) < 1e-4f,
            "muzzle flash scale is restored after the flash");

        feedback.Tick(feedback.SingleShotDuration);
        Check(!feedback.IsRecoiling, "recoil cycle ends");
        Check(recoil.localPosition == restPosition, "recoil returns to the exact rest position");
        Check(Quaternion.Angle(recoil.localRotation, restRotation) < 1e-4f,
            "recoil returns to the exact rest rotation");

        // The feedback is driven by the turret event, not by a private timer.
        CreateEnemy(new Vector3(0f, 0f, 2f), 10000f);
        int beforeLiveShot = shotCount;
        Advance(turret, 0.25f);
        Check(shotCount > beforeLiveShot, "turret fires at the victim");
        Check(feedback.IsRecoiling, "a real shot triggers the recoil through ShotFired");
    }

    // --- 4. 弹壳抛射 / 抛物线 / 回收 ---------------------------------------

    private static void ValidateShellEjector()
    {
        AutoSentryTurret turret = CreateTurret(Vector3.zero);
        Transform artRoot = turret != null ? turret.transform.Find("Sentry Machine Gun Art") : null;
        var ejector = artRoot != null ? artRoot.GetComponent<SentryShellEjector>() : null;
        Check(ejector != null, "casings: ejector component available on the art instance");
        if (ejector == null) return;

        Invoke(ejector, "Awake");
        Invoke(ejector, "OnEnable");
        Check(ejector.ActiveCasingCount == 0, "casings: pool starts empty");

        Vector3 origin = new Vector3(0f, 1f, 0f);
        Vector3 velocity = new Vector3(1.5f, 2.2f, -0.4f);
        Check(SentryShellEjector.SamplePosition(origin, velocity, -9.81f, 0f) == origin,
            "casings: SamplePosition starts at the origin");
        float oneSecondY = SentryShellEjector.SamplePosition(origin, velocity, -9.81f, 1f).y;
        Check(Mathf.Abs(oneSecondY - (1f + 2.2f - 4.905f)) < 1e-3f,
            "casings: SamplePosition applies -9.81 m/s^2 gravity");
        Check(Quaternion.Angle(
                  SentryShellEjector.SampleRotation(Quaternion.identity, Vector3.up, 900f, 0f),
                  Quaternion.identity) < 1e-3f,
            "casings: SampleRotation is identity at time 0");

        Check(ejector.EjectionPort != null && ejector.EjectionPort.name == "Ejection Port",
            "casings: Ejection Port is the spawn node");
        Vector3 portRight = ejector.EjectionPort.right;
        Check(portRight.x > 0.9f, "casings: ejection port faces the turret's right flank (+X)");

        ejector.Eject();
        Check(ejector.ActiveCasingCount == 1, "casings: one shot spawns one casing");

        Transform poolRoot = FindNamed(artRoot, "Shell Casings");
        Check(poolRoot != null && poolRoot.parent == artRoot,
            "casings: pool hangs under the art root, not under the yaw pivot");
        Check(poolRoot != null && turret.AimPivot != null && !poolRoot.IsChildOf(turret.AimPivot),
            "casings: pool does not rotate with the gun yaw");
        if (poolRoot == null || poolRoot.childCount == 0) return;

        Transform casing = poolRoot.GetChild(0);
        Check(Vector3.Distance(casing.position, ejector.EjectionPort.position) < 1e-3f,
            "casings: casing spawns at the ejection port");
        Vector3 start = casing.position;
        Quaternion startRotation = casing.rotation;

        // Diagnostics for the arc assertions below: the serialized flight parameters
        // and the actual per-step heights, so a failure says which number is off.
        var ejectorSerialized = new SerializedObject(ejector);
        Vector3 authoredVelocity = ejectorSerialized.FindProperty("localVelocity").vector3Value;
        float authoredGravity = ejectorSerialized.FindProperty("gravity").floatValue;
        Vector3 worldVelocity = ejector.EjectionPort.TransformDirection(authoredVelocity);
        Debug.Log("[SentryMachineGunStage5] casing diagnostics: start=" + start
                  + " port=" + ejector.EjectionPort.position
                  + " up=" + ejector.EjectionPort.up
                  + " localVelocity=" + authoredVelocity
                  + " worldVelocity=" + worldVelocity
                  + " gravity=" + authoredGravity
                  + " groundY=" + artRoot.position.y);
        float[] probeTimes = { 0.1f, 0.25f, 0.5f, 1.0f };
        int probeIndex = 0;
        float elapsed = 0f;

        float maxY = start.y;
        Vector3 lastActive = start;
        bool recycled = false;
        float flightSeconds = 0f;
        bool spun = false;
        for (int i = 0; i < 120; i++)
        {
            ejector.Step(FixedStep);
            elapsed += FixedStep;
            if (ejector.ActiveCasingCount == 0)
            {
                recycled = true;
                flightSeconds = (i + 1) * FixedStep;
                Debug.Log("[SentryMachineGunStage5] casing recycled after " + flightSeconds
                          + " s at " + lastActive);
                break;
            }
            lastActive = casing.position;
            maxY = Mathf.Max(maxY, lastActive.y);
            if (probeIndex < probeTimes.Length && elapsed >= probeTimes[probeIndex])
            {
                Debug.Log("[SentryMachineGunStage5] casing t=" + elapsed.ToString("0.00")
                          + " y=" + lastActive.y.ToString("0.000") + " pos=" + lastActive);
                probeIndex++;
            }
            if (!spun && Quaternion.Angle(casing.rotation, startRotation) > 5f) spun = true;
        }

        Check(recycled, "casings: casing is recycled instead of living forever");
        Check(flightSeconds > 0.4f && flightSeconds < 1.0f,
            "casings: ground contact recycles the casing (" + flightSeconds.ToString("0.00") + " s)");
        Check(maxY > start.y + 0.15f,
            "casings: the arc rises before it falls (apex +" + (maxY - start.y).ToString("0.000") + " m)");
        Check(lastActive.y < maxY - 0.05f, "casings: the casing falls back down after the apex");
        Check(lastActive.x - start.x > 0.1f, "casings: the casing travels to the throw side (+X)");
        Check(spun, "casings: the casing spins in flight");

        for (int i = 0; i < 40; i++) ejector.Eject();
        Check(ejector.ActiveCasingCount == 32, "casings: pool caps live casings at 32");
        Check(poolRoot.childCount == 32, "casings: the pool never grows past 32 objects");

        ejector.RecycleAll();
        Check(ejector.ActiveCasingCount == 0, "casings: RecycleAll empties the pool");
        Check(!poolRoot.GetChild(0).gameObject.activeSelf, "casings: recycled objects are deactivated");

        // Timeout path: a casing that never hits the floor still expires at 1.6 s.
        ejector.Eject();
        int liveAfterSpawn = ejector.ActiveCasingCount;
        Check(liveAfterSpawn == 1, "casings: a new casing spawns after recycling");
        ejector.Step(2f);
        Check(ejector.ActiveCasingCount == 0, "casings: lifetime expires an airborne casing");
    }

    // --- 5. 激光瞄准线 -----------------------------------------------------

    private static void ValidateLaserSight()
    {
        AutoSentryTurret turret = CreateTurret(Vector3.zero);
        Transform artRoot = turret != null ? turret.transform.Find("Sentry Machine Gun Art") : null;
        var laser = artRoot != null ? artRoot.GetComponent<SentryLaserSight>() : null;
        Check(laser != null, "laser: sight component available on the art instance");
        if (laser == null) return;

        Check(laser.LaserEmitter != null && laser.LaserEmitter.name == "Laser Emitter",
            "laser: Laser Emitter is the beam origin");
        Check(laser.BeamMaterial != null, "laser: beam material wired");

        laser.EnsureBeam();
        var line = laser.GetComponentInChildren<LineRenderer>(true);
        Check(line != null && line.positionCount == 2, "laser: runtime beam is a 2-point LineRenderer");
        Check(line != null && line.useWorldSpace, "laser: beam uses world space points");

        laser.UpdateBeam();
        Check(laser.IsBeamVisible, "laser: beam is visible while the turret is enabled");
        Check(laser.CurrentBeamLength > 0f && laser.CurrentBeamLength <= laser.MaxDistance + 1e-3f,
            "laser: default beam length is capped by max distance");

        Vector3 origin = laser.LaserEmitter.position;
        Vector3 direction = laser.LaserEmitter.forward;
        GameObject blocker = GameObject.CreatePrimitive(PrimitiveType.Cube);
        blocker.name = "Stage5 Laser Blocker";
        blocker.transform.position = origin + direction * 3f;
        blocker.transform.localScale = new Vector3(0.2f, 0.2f, 0.2f);
        owned.Add(blocker);
        Physics.SyncTransforms();
        laser.UpdateBeam();
        Check(Mathf.Abs(laser.CurrentBeamLength - 3f) < 0.2f,
            "laser: the beam is truncated by geometry (" + laser.CurrentBeamLength.ToString("0.00") + " m)");
        Check(laser.CurrentBeamLength < laser.MaxDistance,
            "laser: truncated beam is shorter than the maximum");

        turret.enabled = false;
        laser.UpdateBeam();
        Check(!laser.IsBeamVisible, "laser: disabled turret hides the beam");
        Check(laser.CurrentBeamLength == 0f, "laser: hidden beam has zero length");

        turret.enabled = true;
        laser.UpdateBeam();
        Check(laser.IsBeamVisible, "laser: re-enabled turret restores the beam");
    }

    // --- 工具 --------------------------------------------------------------

    private static AutoSentryTurret CreateTurret(Vector3 position)
    {
        var root = new GameObject("Stage 5 Sentry Turret");
        root.transform.position = position;
        owned.Add(root);
        AutoSentryTurret turret = root.AddComponent<AutoSentryTurret>();
        // Count every round of every turret this harness builds.
        turret.ShotFired += _ => shotCount++;
        Invoke(turret, "Awake");
        return turret;
    }

    private static EnemyHealth CreateEnemy(Vector3 position, float maxHealth)
    {
        var enemy = new GameObject("Stage 5 Sentry Victim");
        enemy.transform.position = position;
        owned.Add(enemy);
        enemy.AddComponent<EnemyInstance>();
        EnemyHealth health = enemy.AddComponent<EnemyHealth>();

        var serialized = new SerializedObject(health);
        SerializedProperty property = serialized.FindProperty("maxHealth");
        if (property != null)
        {
            property.floatValue = maxHealth;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
        health.ResetHealth();
        return health;
    }

    /// <summary>Advances the turret by fixed steps and returns the rounds fired.</summary>
    private static int Advance(AutoSentryTurret turret, float seconds)
    {
        int before = shotCount;
        int steps = Mathf.Max(1, Mathf.CeilToInt(seconds / FixedStep));
        for (int i = 0; i < steps; i++) turret.Tick(FixedStep);
        return shotCount - before;
    }

    private static int CountNamed(Transform root, string name)
    {
        int count = 0;
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            if (child.name == name) count++;
        return count;
    }

    private static Transform FindNamed(Transform root, string name)
    {
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            if (child.name == name) return child;
        return null;
    }

    private static bool HasReference(Object target, string propertyName)
    {
        var serialized = new SerializedObject(target);
        SerializedProperty property = serialized.FindProperty(propertyName);
        return property != null && property.objectReferenceValue != null;
    }

    private static void Invoke(object target, string methodName)
    {
        if (target == null) return;
        MethodInfo method = target.GetType().GetMethod(methodName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (method == null) return;
        method.Invoke(target, null);
    }

    private static void Cleanup()
    {
        for (int i = owned.Count - 1; i >= 0; i--)
            if (owned[i] != null)
                Object.DestroyImmediate(owned[i]);
        owned.Clear();
    }

    private static void Check(bool condition, string message)
    {
        checks++;
        if (condition) return;
        failures++;
        Debug.LogError("[SentryMachineGunStage5] FAILED: " + message);
    }
}
