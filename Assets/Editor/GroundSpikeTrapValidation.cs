using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Deterministic batch smoke checks, independent of optional test assemblies.</summary>
public static class GroundSpikeTrapValidation
{
    private static readonly List<Object> owned = new List<Object>();
    private static int checks;

    public static void Run()
    {
        if (!Application.isBatchMode || Application.isPlaying)
            throw new InvalidOperationException("Run validation in a batch editor, outside Play mode.");
        UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
            UnityEditor.SceneManagement.NewSceneMode.Single);
        checks = 0;
        try
        {
            ValidateModel();
            ValidateAttack();
            Cleanup();
            ValidateSweeps();
            Cleanup();
            ValidatePlacement();
            Cleanup();
            ValidateCombatPermissions();
            Debug.Log($"GROUND_SPIKE_VALIDATION_PASS: {checks} assertions");
        }
        finally { Cleanup(); }
    }

    private static void ValidateModel()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(GroundSpikeTrapAuthoring.PrefabPath);
        Check(prefab != null && prefab.transform.localScale == Vector3.one, "model keeps metre scale");
        Transform plate = prefab.transform.Find("Base plate");
        Transform group = prefab.transform.Find("Spikes");
        Check(plate != null && group != null, "FBX exposes separate plate and animated spike group");
        Bounds plateBounds = plate.GetComponent<Renderer>().bounds;
        Check(Mathf.Abs(plateBounds.size.x - 2f) < 0.001f && Mathf.Abs(plateBounds.size.z - 2f) < 0.001f
            && Mathf.Abs(plateBounds.min.y) < 0.001f && plateBounds.size.y <= 0.081f,
            "plate is exactly 2x2m, grounded, and no taller than 8cm");
        MeshFilter[] spears = group.GetComponentsInChildren<MeshFilter>();
        Check(spears.Length == 16, "all sixteen spears belong to the animated group");
        foreach (MeshFilter spear in spears)
        {
            Check(AssetDatabase.GetAssetPath(spear.sharedMesh) == GroundSpikeTrapAuthoring.ModelPath,
                "prefab references final FBX meshes");
            Bounds bounds = spear.GetComponent<Renderer>().bounds;
            Check(bounds.max.y <= plateBounds.max.y && Mathf.Abs(bounds.max.y - 0.07f) < 0.001f,
                "ready spear tip is below the plate surface");
            Check(bounds.size.x < 0.26f && bounds.size.z < 0.26f, "spear fits its plate aperture");
        }
        var settings = new SerializedObject(prefab.GetComponent<GroundSpikeTrap>());
        Check(settings.FindProperty("spikeGroup").objectReferenceValue == group,
            "runtime animation references the imported group");
        foreach (Renderer renderer in prefab.GetComponentsInChildren<Renderer>())
            foreach (Material material in renderer.sharedMaterials)
                Check(material != null && material.shader != null && material.shader.isSupported,
                    "every model surface has a supported material");
    }

    private static void ValidateAttack()
    {
        GroundSpikeTrap spikes = CreateTrap();
        EnemyHealth a = Enemy(Vector3.zero);
        EnemyHealth b = Enemy(new Vector3(0.6f, 0f, 0f));
        EnemyHealth flying = Enemy(Vector3.zero);
        Set(flying.GetComponent<MonsterPathFollower>(), "flying", true);
        EnemyHealth upstairs = Enemy(new Vector3(0f, 3f, 0f));
        EnemyHealth downstairs = Enemy(new Vector3(0f, -3f, 0f));
        EnemyHealth corpse = Enemy(Vector3.zero);
        typeof(EnemyHealth).GetProperty("CurrentHealth").SetValue(corpse, 0f);
        EnemyHealth playerLike = New("Non-monster", Vector3.zero).AddComponent<EnemyHealth>();
        playerLike.ResetHealth();
        spikes.Tick(0.1f);
        Check(a.CurrentHealth == 60f && b.CurrentHealth == 60f, "multiple ground enemies hit once");
        Check(flying.CurrentHealth == 100f && upstairs.CurrentHealth == 100f && downstairs.CurrentHealth == 100f
            && corpse.CurrentHealth == 0f && playerLike.CurrentHealth == 100f, "flying, other floors, corpses and non-monsters ignored");
        spikes.Tick(0.1f);
        Check(a.CurrentHealth == 60f && spikes.State == GroundSpikeTrap.AttackState.Holding, "hold does not duplicate damage");
        EnemyHealth late = Enemy(new Vector3(-0.5f, 0f, 0f));
        spikes.Tick(0.09f);
        Check(late.CurrentHealth == 60f, "new contact during hold receives damage");
        spikes.Tick(0.02f);
        Check(spikes.State == GroundSpikeTrap.AttackState.Retracting, "retraction follows damage window");
        EnemyHealth retractArrival = Enemy(new Vector3(0f, 0f, 0.5f));
        spikes.Tick(0.2f);
        Check(retractArrival.CurrentHealth == 100f && spikes.State == GroundSpikeTrap.AttackState.Cooldown,
            "retract stops damage and cooldown starts after full retraction");
        spikes.Tick(2.98f);
        Check(a.CurrentHealth == 60f && retractArrival.CurrentHealth == 100f, "full three second cooldown");
        spikes.Tick(0.02f);
        Check(a.CurrentHealth == 20f && b.CurrentHealth == 20f && retractArrival.CurrentHealth == 60f,
            "stationary enemies retrigger without re-entering");
        spikes.enabled = false;
        Invoke(spikes, "OnDisable"); // Edit mode does not dispatch runtime lifecycle callbacks.
        spikes.Tick(4f);
        Check(a.CurrentHealth == 20f && spikes.State == GroundSpikeTrap.AttackState.Ready, "disabled previews cannot attack");
    }

    private static void ValidateSweeps()
    {
        GroundSpikeTrap spikes = CreateTrap();
        EnemyHealth fast = Enemy(new Vector3(-4f, 0f, 0f));
        spikes.Tick(0.02f);
        fast.transform.position = new Vector3(4f, 0f, 0f);
        spikes.Tick(0.2f);
        Check(fast.CurrentHealth == 60f, "high speed crossing with neither endpoint inside hits");
        spikes.Tick(0.4f);
        EnemyHealth cooldownCrossing = Enemy(new Vector3(-4f, 0f, 0f));
        spikes.Tick(2.8f);
        cooldownCrossing.transform.position = new Vector3(4f, 0f, 0f);
        spikes.Tick(0.2f);
        Check(cooldownCrossing.CurrentHealth == 100f, "crossing before cooldown ends is not retroactively hit");
    }

    private static void ValidatePlacement()
    {
        TrapDefinition definition = AssetDatabase.LoadAssetAtPath<TrapDefinition>(GroundSpikeTrapAuthoring.DefinitionPath);
        Check(definition != null && definition.WalkableFloorTrap && definition.Prefab.GetComponent<GroundSpikeTrap>() != null,
            "resource definition is connected to an attacking prefab");
        foreach (Collider collider in definition.Prefab.GetComponentsInChildren<Collider>(true))
            Check(collider.isTrigger, "prefab has no movement-blocking collider");
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        owned.Add(floor);
        floor.transform.position = new Vector3(1.5f, -0.5f, 1.5f);
        floor.transform.localScale = new Vector3(8f, 1f, 8f);
        TrapPlacementGrid grid = New("TrapPlacementGrid_Road", Vector3.zero).AddComponent<TrapPlacementGrid>();
        bool[] mask = new bool[64];
        for (int i = 0; i < mask.Length; i++) mask[i] = true;
        grid.ConfigureLayout(8, 8, 1f, 0.02f, mask);
        Physics.SyncTransforms();
        Check(grid.TryGetFootprint(definition, out Vector2Int size) && size == new Vector2Int(2, 2), "2m plate reserves 2x2 at 1m");
        Check(grid.TryPlaceTrap(Vector2Int.zero, definition, out TrapInstance placed, out string failure), "road placement: " + failure);
        Check(!grid.CanPlaceTrap(Vector2Int.one, definition, out _), "overlap rejected");
        TrapPlacementGrid duplicate = New("Duplicate floor grid", Vector3.zero).AddComponent<TrapPlacementGrid>();
        duplicate.ConfigureLayout(8, 8, 1f, 0.02f, mask);
        Check(!duplicate.CanPlaceTrap(Vector2Int.zero, definition, out _), "overlap across grids rejected");
        grid.RebuildOccupancy();
        Check(grid.IsOccupied(Vector2Int.one) && !grid.CanPlaceTrap(Vector2Int.zero, definition, out _), "reload reconstructs full footprint");
        TrapDefinition ordinary = ScriptableObject.CreateInstance<TrapDefinition>();
        owned.Add(ordinary);
        Check(grid.CanPlaceTrap(new Vector2Int(3, 0), ordinary, out _), "ordinary traps support road placement");
        Check(!grid.CanPlaceTrap(new Vector2Int(6, 6), definition, out _), "unsupported plate rejected");
        foreach (Collider collider in placed.GetComponentsInChildren<Collider>(true))
            Check(collider.isTrigger, "placed trap remains walkable");
        ValidateDestruction(grid, placed);
        grid.ConfigureLayout(8, 8, 0.5f, 0.02f, mask);
        Check(grid.TryGetFootprint(definition, out size) && size == new Vector2Int(4, 4), "2m plate reserves 4x4 at 0.5m");
        grid.ConfigureLayout(8, 8, 2f, 0.02f, mask);
        Check(grid.TryGetFootprint(definition, out size) && size == Vector2Int.one, "2m plate reserves one cell at 2m");
        grid.ConfigureLayout(8, 8, 1.5f, 0.02f, mask);
        Check(grid.TryGetFootprint(definition, out size) && size == new Vector2Int(2, 2)
            && grid.CanPlaceTrap(Vector2Int.zero, definition, out _), "coarse 1.5m grid reserves covered cells without scaling art");
        Bounds bounds = definition.Prefab.transform.Find("Base plate").GetComponent<Renderer>().bounds;
        Check(Mathf.Abs(bounds.size.x - 2f) < 0.001f && Mathf.Abs(bounds.size.z - 2f) < 0.001f, "authored plate remains 2x2 metres");
    }

    private static void ValidateDestruction(TrapPlacementGrid grid, TrapInstance placed)
    {
        // Edit mode does not dispatch runtime Awake/OnEnable callbacks.
        Invoke(placed, "Awake");
        Invoke(placed, "OnEnable");
        GroundSpikeTrap spikes = placed.GetComponent<GroundSpikeTrap>();
        Invoke(spikes, "Awake");
        Invoke(spikes, "OnEnable");
        Transform group = placed.transform.Find("Spikes") ?? placed.transform.Find("Spike Group");
        Check(group != null, "prefab exposes its animated spike group");
        Vector3 rest = group.localPosition;
        EnemyHealth victim = Enemy(placed.transform.position);
        spikes.Tick(0.1f);
        Check(victim.CurrentHealth == 60f && spikes.State == GroundSpikeTrap.AttackState.Extending,
            "destruction test starts during active damage window");
        placed.TakeDamage(25f);
        Check(placed.MaxHealth == 100f && placed.CurrentHealth == 75f,
            "spikes use shared trap health and accept direct damage without targeting permission");
        Ray aim = new Ray(placed.transform.position + Vector3.up * 3f, Vector3.down);
        Check(TrapHealthBarUI.FindTrapNearRay(aim, 4f) == placed, "spikes use existing crosshair health targeting");
        int destroyedEvents = 0;
        placed.Destroyed += _ =>
        {
            destroyedEvents++;
            Check(spikes.State == GroundSpikeTrap.AttackState.Ready && group.localPosition == rest,
                "lethal damage immediately stops attack and retracts spikes");
            spikes.Tick(4f);
            Check(victim.CurrentHealth == 60f, "destroyed trap cannot deal damage before object removal");
            Check(TrapHealthBarUI.FindTrapNearRay(aim, 4f) == null, "destroyed spikes leave health targeting immediately");
        };
        placed.TakeDamage(75f);
        Check(destroyedEvents == 1, "lethal damage raises shared destruction event once");
        Check(grid.PlacedTraps.Count == 0 && !grid.IsOccupied(Vector2Int.zero) && !grid.IsOccupied(Vector2Int.one),
            "lethal damage immediately releases entire 2x2 footprint");
        Check(grid.TryPlaceTrap(Vector2Int.zero,
            AssetDatabase.LoadAssetAtPath<TrapDefinition>(GroundSpikeTrapAuthoring.DefinitionPath),
            out TrapInstance replacement, out _), "released footprint accepts replacement immediately");
        grid.RemoveTrap(replacement);
        Object.DestroyImmediate(victim.gameObject);
    }

    private static void ValidateCombatPermissions()
    {
        MonsterPathGrid road = New("Combat road", Vector3.zero).AddComponent<MonsterPathGrid>();
        for (int x = 1; x < 15; x++) road.SetOpen(new Vector2Int(x, 1), true);
        GroundSpikeTrap spikes = CreateTrap();
        spikes.transform.position = new Vector3(2.5f, 0f, 1.5f);
        TrapInstance instance = spikes.GetComponent<TrapInstance>();
        Invoke(instance, "OnEnable");
        EnemyHealth health = Enemy(new Vector3(1.5f, 0f, 1.5f));
        MonsterPathFollower follower = health.GetComponent<MonsterPathFollower>();
        GroundEnemyCombat combat = health.gameObject.AddComponent<GroundEnemyCombat>();
        Invoke(combat, "Awake");
        Vector3 destination = new Vector3(14.5f, 0f, 1.5f);
        follower.Initialize(road, null, destination, Vector3.right, 2f);

        Invoke(combat, "Update");
        Check(combat.CurrentTarget == null && !follower.IsFollowingCombatPath && !follower.CombatMovementPaused,
            "ordinary monster ignores spikes and continues mission");
        spikes.Tick(0.1f);
        Check(health.CurrentHealth == 60f, "ordinary monster actually receives spike damage");
        Invoke(combat, "Update");
        Check(combat.CurrentTarget == null && !follower.CombatMovementPaused,
            "spike damage does not trigger retaliation or stop ordinary monster");

        TrapInstance ordinary = New("Ordinary trap", new Vector3(1.5f, 0f, 2.5f)).AddComponent<TrapInstance>();
        Invoke(ordinary, "Awake");
        Invoke(ordinary, "OnEnable");
        CombatTick(combat);
        Check(combat.CurrentTarget == ordinary.transform && ordinary.CurrentHealth == 90f,
            "ordinary traps remain valid melee targets when spike permission is disabled");
        Invoke(ordinary, "OnDisable");
        Object.DestroyImmediate(ordinary.gameObject);

        Set(combat, "allowAttackGroundSpikes", true);
        CombatTick(combat);
        Check(combat.CurrentTarget == instance.transform && instance.CurrentHealth == 90f
            && follower.CombatMovementPaused, "explicitly enabled special monster targets and damages spikes");
        SetNumber(combat, "nextRepathTime", Time.time + 100f);
        SetNumber(combat, "nextAttackTime", 0f);
        Set(combat, "allowAttackGroundSpikes", false);
        Invoke(combat, "Update");
        Check(combat.CurrentTarget == null && !combat.IsAttacking && instance.CurrentHealth == 90f,
            "revoking permission invalidates current target before next scan or melee hit");
        Check(!follower.IsFollowingCombatPath && !follower.CombatMovementPaused
            && follower.DestinationPosition == destination, "revoked target resumes original mission immediately");
    }

    private static void CombatTick(GroundEnemyCombat combat)
    {
        SetNumber(combat, "nextRepathTime", 0f);
        SetNumber(combat, "nextAttackTime", 0f);
        Invoke(combat, "Update");
    }

    private static void SetNumber(Object target, string field, float value)
    {
        target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    }

    private static GroundSpikeTrap CreateTrap()
    {
        GameObject root = New("Test spikes", Vector3.zero);
        TrapInstance trap = root.AddComponent<TrapInstance>();
        Invoke(trap, "Awake");
        GroundSpikeTrap spikes = root.AddComponent<GroundSpikeTrap>();
        Invoke(spikes, "Awake");
        return spikes;
    }

    private static EnemyHealth Enemy(Vector3 position)
    {
        GameObject root = New("Test ground enemy", position);
        root.AddComponent<MonsterPathFollower>();
        EnemyHealth health = root.AddComponent<EnemyHealth>();
        health.ResetHealth();
        return health;
    }

    private static GameObject New(string name, Vector3 position)
    {
        var root = new GameObject(name);
        root.transform.position = position;
        owned.Add(root);
        return root;
    }

    private static void Set(Object target, string field, bool value)
    {
        var settings = new SerializedObject(target);
        settings.FindProperty(field).boolValue = value;
        settings.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void Invoke(object target, string method) => target.GetType()
        .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null);

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("GroundSpike validation failed: " + message);
        checks++;
    }

    private static void Cleanup()
    {
        for (int i = owned.Count - 1; i >= 0; i--) if (owned[i] != null) Object.DestroyImmediate(owned[i]);
        owned.Clear();
    }
}
