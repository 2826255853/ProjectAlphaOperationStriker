using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Step 6 timing checks with real Unity objects and controlled frame deltas.</summary>
[InitializeOnLoad]
public static class FlamethrowerTimingValidation
{
    private const string PlayModePending = "FlamethrowerTimingValidation.PlayModePending";
    private static readonly List<GameObject> owned = new List<GameObject>();
    private static int checks;

    static FlamethrowerTimingValidation()
    {
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    public static void Run()
    {
        RequireBatchEditor();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        RunChecks();
        Debug.Log($"FLAMETHROWER_STEP6_PASS: {checks} assertions");
        FlamethrowerAttackVolumeValidation.Run();
    }

    // Launch without -quit: the callback exits only after Play mode checks complete.
    public static void RunPlayMode()
    {
        RequireBatchEditor();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        SessionState.SetBool(PlayModePending, true);
        EditorApplication.EnterPlaymode();
    }

    private static void OnPlayModeChanged(PlayModeStateChange change)
    {
        if (change != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(PlayModePending, false)) return;
        SessionState.SetBool(PlayModePending, false);
        try
        {
            RunChecks();
            Debug.Log($"FLAMETHROWER_STEP6_PLAYMODE_PASS: {checks} assertions (controlled Tick)");
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }

    private static void RequireBatchEditor()
    {
        if (!Application.isBatchMode || Application.isPlaying)
            throw new InvalidOperationException("Run in a batch editor outside Play mode.");
    }

    private static void RunChecks()
    {
        checks = 0;
        try
        {
            ValidatePrefab();
            ValidateTimingAndTargets();
            Cleanup();
            ValidateCommittedDelay();
            Cleanup();
            ValidateLongFrames();
            Cleanup();
            ValidateFramePartitions();
            Cleanup();
            ValidateLifecycle();
        }
        finally { Cleanup(); }
    }

    private static void ValidatePrefab()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FlamethrowerTrapAuthoring.PrefabPath);
        Check(prefab != null, "formal graybox prefab exists");
        FlamethrowerTrap component = prefab.GetComponent<FlamethrowerTrap>();
        Check(component != null, "formal prefab has the runtime state machine");
        Near(component.TriggerDelay, 0.1f, "prefab trigger delay");
        Near(component.SprayDuration, 2f, "prefab spray duration");
        Near(component.CooldownDuration, 1.9f, "prefab cooldown duration");
        Near(component.Range, 8f, "prefab retains the current eight-metre range");
        var settings = new SerializedObject(component);
        Near(settings.FindProperty("burnDuration").floatValue, 1f, "prefab burn duration");
        Near(settings.FindProperty("burnDamagePerSecond").floatValue, 5f, "prefab burn rate");
    }

    private static void ValidateTimingAndTargets()
    {
        FlamethrowerTrap trap = Trap();
        trap.Tick(10f);
        State(trap, FlamethrowerTrap.AttackState.Ready, "idle without targets");
        EnemyHealth first = Enemy("First target", Vector3.forward * 3f);
        EnemyHealth second = Enemy("Second target", new Vector3(0.5f, 0f, 6f));
        EnemyHealth late = Enemy("Late target", Vector3.forward * 9f);
        int firstHits = 0;
        first.TrapDamaged += (_, amount, type) =>
        {
            Check(type == TrapDamageType.Fire, "direct and burning damage use Fire");
            if (amount > 5f) firstHits++;
        };
        trap.Tick(0.099f);
        State(trap, FlamethrowerTrap.AttackState.Delay, "before 0.1 seconds");
        Near(first.CurrentHealth, 100f, "no damage during preheat");
        Check(first.GetComponent<FireStatusEffect>() == null, "no ignition during preheat");
        trap.Tick(0.001f);
        State(trap, FlamethrowerTrap.AttackState.Spraying, "exact preheat endpoint");
        Near(first.CurrentHealth, 80f, "opening boundary hits first target immediately");
        Near(second.CurrentHealth, 80f, "opening boundary hits all targets");
        Check(firstHits == 1, "one direct hit at spray start");
        FireStatusEffect burn = first.GetComponent<FireStatusEffect>();
        Check(burn != null && burn.IsBurning, "direct hit attaches an enemy-owned burn");
        Near(burn.RemainingSeconds, 1f, "one second ignition");
        Near(burn.DamagePerSecond, 5f, "five damage per second ignition");
        trap.Tick(0.5f);
        Near(first.CurrentHealth, 80f, "continued scan cannot repeat direct damage");
        Near(late.CurrentHealth, 100f, "outside target is untouched");
        late.transform.position = Vector3.forward * 7f;
        trap.Tick(0.25f);
        Near(late.CurrentHealth, 80f, "late arrival joins the ongoing burst");
        Check(late.GetComponent<FireStatusEffect>().IsBurning, "late arrival is ignited");
        first.transform.position = Vector3.back;
        burn.Tick(0.4f);
        Near(first.CurrentHealth, 78f, "burn continues outside volume");
        trap.Tick(0.25f);
        first.transform.position = Vector3.forward * 3f;
        trap.Tick(0.25f);
        Near(first.CurrentHealth, 78f, "leaving and re-entering cannot repeat direct hit");
        burn.Tick(5f);
        Near(first.CurrentHealth, 75f, "long burn frame is capped to five total damage");
        Check(!burn.IsBurning, "burn expires independently");
        trap.Tick(0.749f);
        State(trap, FlamethrowerTrap.AttackState.Spraying, "still spraying at 1.999 seconds");
        trap.Tick(0.001f);
        State(trap, FlamethrowerTrap.AttackState.Cooldown, "exact two-second spray endpoint");
        Check(firstHits == 1, "one direct hit for the whole burst");
        EnemyHealth cooling = Enemy("Arrival during cooldown", Vector3.forward * 2f);
        FireStatusEffect secondBurn = second.GetComponent<FireStatusEffect>();
        secondBurn.Tick(0.5f);
        Near(second.CurrentHealth, 77.5f, "enemy burn continues while trap cools");
        trap.Tick(1.899f);
        State(trap, FlamethrowerTrap.AttackState.Cooldown, "before cooldown endpoint");
        Near(cooling.CurrentHealth, 100f, "cooldown arrival receives no direct hit");
        Check(cooling.GetComponent<FireStatusEffect>() == null, "cooldown does not ignite targets");
        trap.Tick(0.001f);
        State(trap, FlamethrowerTrap.AttackState.Ready, "exact 1.9-second cooldown endpoint");
        trap.Tick(0.1f);
        State(trap, FlamethrowerTrap.AttackState.Spraying, "next burst after a new preheat");
        Near(first.CurrentHealth, 55f, "next burst resets target deduplication");
        Near(cooling.CurrentHealth, 80f, "cooldown arrival participates in next burst");
        Check(firstHits == 2, "two bursts produce exactly two direct hits");
    }

    private static void ValidateCommittedDelay()
    {
        FlamethrowerTrap trap = Trap();
        EnemyHealth trigger = Enemy("Transient trigger", Vector3.forward);
        trap.Tick(0.05f);
        trigger.transform.position = Vector3.back;
        trap.Tick(0.05f);
        State(trap, FlamethrowerTrap.AttackState.Spraying, "losing trigger does not cancel delay");
        Near(trigger.CurrentHealth, 100f, "departed trigger is not hit");
        EnemyHealth late = Enemy("Target after trigger departed", Vector3.forward * 4f);
        trap.Tick(0.5f);
        Near(late.CurrentHealth, 80f, "new target can join a committed initially empty spray");
        late.transform.position = Vector3.back;
        trap.Tick(1.5f);
        State(trap, FlamethrowerTrap.AttackState.Cooldown, "empty spray keeps the full duration");
        late.GetComponent<FireStatusEffect>().Tick(2f);
        Near(late.CurrentHealth, 75f, "departed late target continues burning in cooldown");
        trap.Tick(1.9f);
        State(trap, FlamethrowerTrap.AttackState.Ready, "committed cycle completes without targets");
        trap.Tick(20f);
        State(trap, FlamethrowerTrap.AttackState.Ready, "no new trigger outside attack volume");
    }

    private static void ValidateLongFrames()
    {
        FlamethrowerTrap trap = Trap();
        EnemyHealth enemy = Enemy("Long frame target", Vector3.forward);
        trap.Tick(2.5f);
        State(trap, FlamethrowerTrap.AttackState.Cooldown, "single frame crosses delay and entire spray");
        Near(enemy.CurrentHealth, 80f, "crossed spray window still hits once");
        trap.Tick(1.5f);
        State(trap, FlamethrowerTrap.AttackState.Ready, "cooldown preserves carried frame time");
        trap.Tick(0.1f);
        Near(enemy.CurrentHealth, 60f, "next exact opening boundary after long frame");
        Cleanup();
        trap = Trap();
        enemy = Enemy("Multiple cycle target", Vector3.forward);
        trap.Tick(8.1f);
        State(trap, FlamethrowerTrap.AttackState.Spraying, "single frame reaches third burst");
        Near(enemy.CurrentHealth, 40f, "multiple crossed cycles each apply one direct hit");
        Check(enemy.GetComponents<FireStatusEffect>().Length == 1, "multiple cycles refresh one burn component");
        Near(enemy.GetComponent<FireStatusEffect>().RemainingSeconds, 1f, "last ignition refreshes duration");
    }

    private static void ValidateFramePartitions()
    {
        float[] frameLengths = { 0.01f, 0.1f, 0.5f, 4f };
        foreach (float frameLength in frameLengths)
        {
            FlamethrowerTrap trap = Trap();
            EnemyHealth enemy = Enemy("Partition target", Vector3.forward);
            int frameCount = Mathf.RoundToInt(4f / frameLength);
            for (int i = 0; i < frameCount; i++) trap.Tick(frameLength);
            State(trap, FlamethrowerTrap.AttackState.Ready, $"four seconds split into {frameCount} frames");
            Near(enemy.CurrentHealth, 80f, "frame partitions preserve one hit per cycle");
            trap.Tick(0.1f);
            Near(enemy.CurrentHealth, 60f, "partitioned cycle preserves next opening boundary");
            Cleanup();
        }
    }

    private static void ValidateLifecycle()
    {
        FlamethrowerTrap trap = Trap();
        EnemyHealth enemy = Enemy("Lifecycle target", Vector3.forward);
        trap.Tick(0f);
        trap.Tick(-1f);
        trap.Tick(float.NaN);
        trap.Tick(float.PositiveInfinity);
        State(trap, FlamethrowerTrap.AttackState.Ready, "invalid deltas do not trigger or hang");
        trap.Tick(0.06f);
        trap.enabled = false;
        trap.gameObject.SendMessage("OnDisable", SendMessageOptions.DontRequireReceiver);
        trap.Tick(5f);
        Near(enemy.CurrentHealth, 100f, "disabled trap cannot damage");
        trap.enabled = true;
        trap.gameObject.SendMessage("OnEnable", SendMessageOptions.DontRequireReceiver);
        trap.Tick(0.05f);
        State(trap, FlamethrowerTrap.AttackState.Delay, "reenabling clears previous preheat time");
        Near(enemy.CurrentHealth, 100f, "fresh preheat is required after reenabling");
        trap.Tick(0.05f);
        Near(enemy.CurrentHealth, 80f, "fresh preheat ends at 0.1 seconds");
        trap.enabled = false;
        trap.gameObject.SendMessage("OnDisable", SendMessageOptions.DontRequireReceiver);
        enemy.GetComponent<FireStatusEffect>().Tick(2f);
        Near(enemy.CurrentHealth, 75f, "trap disable does not stop enemy-owned burning");
        trap.enabled = true;
        trap.gameObject.SendMessage("OnEnable", SendMessageOptions.DontRequireReceiver);
        trap.Tick(0.1f);
        Near(enemy.CurrentHealth, 55f, "reenabling clears previous burst hit records");
        var settings = new SerializedObject(trap);
        settings.FindProperty("directDamage").floatValue = 7f;
        settings.ApplyModifiedPropertiesWithoutUndo();
        trap.Tick(3.9f);
        trap.Tick(0.1f);
        Near(enemy.CurrentHealth, 48f, "direct damage remains configurable");
    }

    private static FlamethrowerTrap Trap()
    {
        GameObject root = NewObject("Step 6 trap");
        FlamethrowerTrap trap = root.AddComponent<FlamethrowerTrap>();
        // Edit mode does not invoke ordinary MonoBehaviour Awake automatically.
        if (!Application.isPlaying) root.SendMessage("Awake", SendMessageOptions.DontRequireReceiver);
        return trap;
    }

    private static EnemyHealth Enemy(string name, Vector3 position)
    {
        GameObject root = NewObject(name);
        root.transform.position = position;
        EnemyHealth enemy = root.AddComponent<EnemyHealth>();
        enemy.ResetHealth();
        return enemy;
    }

    private static GameObject NewObject(string name)
    {
        var root = new GameObject(name);
        owned.Add(root);
        return root;
    }

    private static void Cleanup()
    {
        for (int i = owned.Count - 1; i >= 0; i--)
            if (owned[i] != null) Object.DestroyImmediate(owned[i]);
        owned.Clear();
    }

    private static void State(FlamethrowerTrap trap, FlamethrowerTrap.AttackState expected, string message)
    {
        Check(trap.State == expected, $"{message}: expected {expected}, got {trap.State}");
    }

    private static void Near(float actual, float expected, string message)
    {
        Check(Mathf.Abs(actual - expected) < 0.0001f, $"{message}: expected {expected}, got {actual}");
    }

    private static void Check(bool passed, string message)
    {
        if (!passed) throw new InvalidOperationException("Flamethrower step 6: " + message);
        checks++;
    }
}
