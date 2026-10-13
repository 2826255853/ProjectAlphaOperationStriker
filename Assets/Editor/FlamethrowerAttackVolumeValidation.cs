using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Step 5 checks against real Unity objects, without optional NUnit assemblies.</summary>
public static class FlamethrowerAttackVolumeValidation
{
    private static readonly List<GameObject> owned = new List<GameObject>();
    private static int checks;

    public static void Run()
    {
        if (!Application.isBatchMode || Application.isPlaying)
            throw new InvalidOperationException("Run in a batch editor outside Play mode.");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        checks = 0;
        try
        {
            ValidateBounds();
            Cleanup();
            ValidateQueries();
            Cleanup();
            ValidateExistingDamagePath();
            Debug.Log($"FLAMETHROWER_STEP5_PASS: {checks} assertions");
        }
        finally { Cleanup(); }
    }

    private static void ValidateBounds()
    {
        FlamethrowerTrap trap = NewObject("Attack volume validation").AddComponent<FlamethrowerTrap>();
        EnemyHealth enemy = Enemy("Boundary probe", Vector3.zero);
        Check(trap.AttackVolumeSize == new Vector3(2f, 2f, 8f), "default volume is 2x2x8 metres");
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FlamethrowerTrapAuthoring.PrefabPath);
        Check(prefab != null && prefab.GetComponent<FlamethrowerTrap>().AttackVolumeSize == new Vector3(2f, 2f, 8f),
            "game prefab volume is 2x2x8 metres");
        var inside = new List<Vector3>
        {
            Vector3.zero, new Vector3(0f, 0f, 8f), new Vector3(-1f, 0f, 2f),
            new Vector3(1f, 0f, 2f), new Vector3(0f, -1f, 2f), new Vector3(0f, 1f, 2f),
            new Vector3(0.999f, -0.999f, 7.999f), new Vector3(0f, 0f, 0.001f)
        };
        foreach (float x in new[] { -1f, 1f })
        foreach (float y in new[] { -1f, 1f })
        foreach (float z in new[] { 0f, 8f }) inside.Add(new Vector3(x, y, z));
        var outside = new[]
        {
            new Vector3(0f, 0f, -0.002f), new Vector3(0f, 0f, 8.002f),
            new Vector3(-1.002f, 0f, 2f), new Vector3(1.002f, 0f, 2f),
            new Vector3(0f, -1.002f, 2f), new Vector3(0f, 1.002f, 2f),
            new Vector3(0f, 0f, -2f), new Vector3(2f, 0f, 2f), new Vector3(0f, 3f, 2f)
        };
        foreach (Quaternion rotation in new[]
        {
            Quaternion.identity, Quaternion.Euler(0f, 90f, 0f), Quaternion.Euler(0f, 180f, 0f),
            Quaternion.Euler(0f, 270f, 0f), Quaternion.Euler(0f, 37f, 0f), Quaternion.Euler(15f, 37f, 10f)
        })
        {
            trap.transform.SetPositionAndRotation(new Vector3(12f, 3f, -7f), rotation);
            foreach (Vector3 point in inside) Probe(trap, enemy, point, true);
            foreach (Vector3 point in outside) Probe(trap, enemy, point, false);
        }
        GameObject parent = NewObject("Scaled trap parent");
        parent.transform.SetPositionAndRotation(new Vector3(-4f, 2f, 8f), Quaternion.Euler(0f, 23f, 0f));
        parent.transform.localScale = new Vector3(2f, 3f, 0.5f);
        trap.transform.SetParent(parent.transform, true);
        trap.transform.localScale = new Vector3(0.5f, 2f, 3f);
        foreach (Vector3 point in inside) Probe(trap, enemy, point, true);
        foreach (Vector3 point in outside) Probe(trap, enemy, point, false);
        Check(Mathf.Abs(trap.AttackVolumeMatrix.MultiplyVector(Vector3.forward * 8f).magnitude - 8f) < 0.00001f,
            "debug matrix and query keep eight-metre length under nonuniform parent/model scale");
        // Serialized tuning remains the source of truth for both query and Gizmo.
        var settings = new SerializedObject(trap);
        settings.FindProperty("range").floatValue = 3f;
        settings.FindProperty("width").floatValue = 1f;
        settings.FindProperty("height").floatValue = 1.5f;
        settings.ApplyModifiedPropertiesWithoutUndo();
        Check(trap.AttackVolumeSize == new Vector3(1f, 1.5f, 3f), "configured dimensions are exposed to debug drawing");
        Probe(trap, enemy, new Vector3(0.499f, 0.749f, 2.999f), true);
        Probe(trap, enemy, new Vector3(0.502f, 0f, 2f), false);
        Probe(trap, enemy, new Vector3(0f, 0.752f, 2f), false);
        Probe(trap, enemy, new Vector3(0f, 0f, 3.002f), false);
    }

    private static void ValidateQueries()
    {
        FlamethrowerTrap trap = NewObject("Query validation").AddComponent<FlamethrowerTrap>();
        EnemyHealth bare = Enemy("No collider or identity", new Vector3(0f, 0f, 1f));
        EnemyHealth hitboxes = Enemy("Many child colliders", new Vector3(0.5f, 0f, 2f));
        hitboxes.gameObject.AddComponent<EnemyInstance>();
        for (int i = 0; i < 3; i++)
        {
            GameObject child = NewObject("Hitbox " + i);
            child.transform.SetParent(hitboxes.transform, false);
            child.AddComponent<BoxCollider>();
        }
        GameObject body = NewObject("Identity body");
        body.transform.position = new Vector3(-0.5f, 0f, 3f);
        body.AddComponent<EnemyInstance>();
        EnemyHealth childHealth = Enemy("Offset child health", new Vector3(30f, 30f, -30f));
        childHealth.transform.SetParent(body.transform, true);
        EnemyHealth behind = Enemy("Behind", new Vector3(0f, 0f, -1f));
        EnemyHealth disabled = Enemy("Disabled health", new Vector3(0f, 0f, 1f));
        disabled.enabled = false;
        EnemyHealth inactive = Enemy("Inactive enemy", new Vector3(0f, 0f, 1f));
        inactive.gameObject.SetActive(false);
        EnemyHealth dead = Enemy("Dead enemy", new Vector3(0f, 0f, 1f));
        // Keep a dead object present to test filtering without edit-mode destruction.
        typeof(EnemyHealth).GetProperty(nameof(EnemyHealth.CurrentHealth)).SetValue(dead, 0f);
        Check(!trap.IsInAttackVolume(null), "null health is rejected");
        Check(!trap.IsInAttackVolume(disabled), "disabled health is rejected");
        Check(!trap.IsInAttackVolume(inactive), "inactive object is rejected");
        Check(!trap.IsInAttackVolume(dead), "dead object is rejected before destruction");
        var results = new List<EnemyHealth> { behind, behind };
        Check(trap.CollectAttackTargets(results) == 3, "all three valid bodies found together");
        Check(new HashSet<EnemyHealth>(results).Count == results.Count, "each health appears once despite three child colliders");
        Check(results.Contains(bare) && results.Contains(hitboxes) && results.Contains(childHealth), "bare/root/child health resolve correctly");
        Check(!results.Contains(behind) && !results.Contains(dead) && !results.Contains(inactive) && !results.Contains(disabled), "invalid and out-of-range entries are absent");
        Check(bare.GetComponentsInChildren<Collider>().Length == 0, "query does not require target colliders");
        Check(trap.GetComponentsInChildren<Collider>().Length == 0, "query does not require trap colliders");
        Check(trap.CollectAttackTargets(results) == 3, "repeated queries replace rather than accumulate results");
        body.transform.position = new Vector3(0f, 0f, -1f);
        childHealth.transform.position = new Vector3(0f, 0f, 2f);
        Check(!trap.IsInAttackVolume(childHealth), "outside body cannot be admitted by an inside child position");
        Check(trap.CollectAttackTargets(results) == 2 && !results.Contains(childHealth), "query follows moved body");
        bare.gameObject.SetActive(false);
        hitboxes.enabled = false;
        Check(trap.CollectAttackTargets(results) == 0 && results.Count == 0, "empty query clears stale targets");
    }

    private static void ValidateExistingDamagePath()
    {
        GameObject root = NewObject("Existing spray path validation");
        FlamethrowerTrap trap = root.AddComponent<FlamethrowerTrap>();
        root.SendMessage("Awake", SendMessageOptions.DontRequireReceiver);
        EnemyHealth first = Enemy("Collider-free damage target", new Vector3(0f, 0f, 6f));
        EnemyHealth second = Enemy("Multi-hitbox damage target", new Vector3(0.5f, 0f, 8f));
        for (int i = 0; i < 3; i++)
        {
            GameObject child = NewObject("Damage hitbox " + i);
            child.transform.SetParent(second.transform, false);
            child.AddComponent<BoxCollider>();
        }
        EnemyHealth outside = Enemy("Outside damage target", new Vector3(0f, 0f, 8.002f));
        int hits = 0;
        first.TrapDamaged += (_, amount, type) => { Check(type == TrapDamageType.Fire && amount == 20f, "first direct hit remains fire/20 damage"); hits++; };
        second.TrapDamaged += (_, amount, type) => { Check(type == TrapDamageType.Fire && amount == 20f, "second direct hit remains fire/20 damage"); hits++; };
        trap.Tick(0.11f);
        Check(first.CurrentHealth == 80f && second.CurrentHealth == 80f && hits == 2, "existing spray damages both unique targets once");
        trap.Tick(0.01f);
        Check(first.CurrentHealth == 80f && second.CurrentHealth == 80f && hits == 2, "repeated scans in the same spray do not repeat direct damage");
        Check(outside.CurrentHealth == 100f, "existing damage path rejects target beyond eight metres");
        Check(first.GetComponent<FireStatusEffect>() != null && second.GetComponent<FireStatusEffect>() != null, "existing ignition hookup is retained");
    }

    private static void Probe(FlamethrowerTrap trap, EnemyHealth enemy, Vector3 local, bool expected)
    {
        enemy.transform.position = trap.AttackVolumeMatrix.MultiplyPoint3x4(local);
        Check(trap.IsInAttackVolume(enemy) == expected,
            $"local {local:F4} expected {expected}, rotation {trap.transform.eulerAngles:F2}");
    }

    private static EnemyHealth Enemy(string name, Vector3 position)
    {
        GameObject root = NewObject(name);
        root.transform.position = position;
        EnemyHealth health = root.AddComponent<EnemyHealth>();
        health.ResetHealth();
        return health;
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

    private static void Check(bool passed, string message)
    {
        if (!passed) throw new InvalidOperationException("Flamethrower step 5: " + message);
        checks++;
    }
}
