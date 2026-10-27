#if UNITY_INCLUDE_TESTS
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 快速小怪（新怪物种类）的编辑期检查：数值表、预制体接线、程序化步态骨骼、
/// 右手警棍网格与“比普通小怪更小”的体型约束。
/// 运行方式：-runTests -testPlatform EditMode -testFilter FastRunnerTests
/// </summary>
public sealed class FastRunnerTests
{
    private const string PrefabPath = "Assets/Prefabs/FastRunner.prefab";

    private static GameObject LoadPrefab()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        Assert.That(prefab, Is.Not.Null, "缺少快速小怪预制体，请先执行 Tools/塔防/快速小怪/生成材质与预制体");
        return prefab;
    }

    [Test]
    public void SpeciesCatalogMatchesDesignDocument()
    {
        var species = MonsterSpeciesCatalog.FastRunner;
        Assert.That(species.Id, Is.EqualTo(MonsterSpeciesCatalog.FastRunnerId));
        Assert.That(species.DisplayName, Is.EqualTo("快速小怪"));
        Assert.That(species.MaxHealth, Is.EqualTo(40f), "设计文档：血量 40");
        Assert.That(species.AttackDamage, Is.EqualTo(8f), "设计文档：攻击 8");
        Assert.That(species.MoveSpeed, Is.GreaterThan(3f), "设计文档：移速快");
        Assert.That(species.GetResistancePercent(DamageType.Light), Is.LessThan(0f), "设计文档：光属性易伤");
        Assert.That(species.ModelScale, Is.LessThan(1f), "快速小怪体型小于普通小怪");
    }

    [Test]
    public void CatalogFindReturnsKnownSpeciesOnly()
    {
        Assert.That(MonsterSpeciesCatalog.Find(MonsterSpeciesCatalog.FastRunnerId), Is.SameAs(MonsterSpeciesCatalog.FastRunner));
        Assert.That(MonsterSpeciesCatalog.Find("not_a_species"), Is.Null);
        Assert.That(MonsterSpeciesCatalog.Find(null), Is.Null);
    }

    [Test]
    public void PrefabCarriesSpeciesStats()
    {
        var prefab = LoadPrefab();
        var species = MonsterSpeciesCatalog.FastRunner;

        var health = prefab.GetComponent<EnemyHealth>();
        Assert.That(health, Is.Not.Null, "预制体缺少 EnemyHealth");
        Assert.That(health.MaxHealth, Is.EqualTo(species.MaxHealth).Within(0.01f));
        Assert.That(health.GetResistancePercent(DamageType.Light),
            Is.EqualTo(species.GetResistancePercent(DamageType.Light)).Within(0.01f));

        var combat = prefab.GetComponent<GroundEnemyCombat>();
        Assert.That(combat, Is.Not.Null, "预制体缺少 GroundEnemyCombat");
        var serialized = new SerializedObject(combat);
        Assert.That(serialized.FindProperty("attackDamage").floatValue, Is.EqualTo(species.AttackDamage).Within(0.01f));
        Assert.That(serialized.FindProperty("attackInterval").floatValue, Is.EqualTo(species.AttackInterval).Within(0.01f));
    }

    [Test]
    public void PrefabUsesProceduralGaitWithEveryRequiredBone()
    {
        var prefab = LoadPrefab();
        var gait = prefab.GetComponent<MonsterWalkAnimator>();
        Assert.That(gait, Is.Not.Null, "预制体缺少 MonsterWalkAnimator");
        Assert.That(prefab.GetComponentInChildren<Animator>(true), Is.Null, "快速小怪不应依赖 Animator 资源");

        gait.EnsureInitialized();
        Assert.That(gait.MissingBones, Is.Empty, "缺少骨骼: " + string.Join(",", gait.MissingBones));
    }

    [Test]
    public void BatonIsSeparateMeshWeightedToRightHand()
    {
        var prefab = LoadPrefab();
        var baton = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true)
            .FirstOrDefault(r => r.name.IndexOf("Baton", System.StringComparison.OrdinalIgnoreCase) >= 0);
        Assert.That(baton, Is.Not.Null, "警棍必须是独立的蒙皮网格");
        Assert.That(baton.bones.Any(b => b != null && b.name == "Baton"), Is.True, "警棍网格缺少 Baton 骨骼");
        Assert.That(baton.bones.Any(b => b != null && b.name == "Hand.R"), Is.True, "Baton 骨骼应挂在右手下");
    }

    [Test]
    public void FastRunnerIsSmallerThanTheRegularGrunt()
    {
        var prefab = LoadPrefab();
        var capsule = prefab.GetComponent<CapsuleCollider>();
        Assert.That(capsule, Is.Not.Null, "预制体缺少碰撞体");
        Assert.That(prefab.transform.localScale.x, Is.LessThan(1f), "预制体缩放应小于 1");
        Assert.That(capsule.height, Is.LessThan(1.8f), "碰撞体应低于普通小怪（1.8 m）");
        Assert.That(capsule.radius, Is.LessThanOrEqualTo(0.32f));
    }

    // ---- 接入刷怪：50% 普通 + 50% 快速 ----

    [Test]
    public void AlternateShareSplitsEveryWaveInHalf()
    {
        foreach (var count in new[] { 1, 2, 9, 10, 15 })
        {
            var alternates = Enumerable.Range(1, count)
                .Count(index => EnemySpawnPoint.UseAlternatePrefab(index, 0.5f));
            Assert.That(alternates, Is.EqualTo(EnemySpawnPoint.GetAlternateCount(count, 0.5f)),
                "第 " + count + " 只的波次：交替数量应与占比一致");
            Assert.That(alternates, Is.EqualTo(Mathf.FloorToInt(count * 0.5f)),
                "第 " + count + " 只的波次：一半是快速小怪");
        }
    }

    [Test]
    public void AlternateShareInterleavesInsteadOfClumping()
    {
        // 0.5 时应该是 普通/快速/普通/快速……，不允许连续两只快速。
        var sequence = Enumerable.Range(1, 10)
            .Select(index => EnemySpawnPoint.UseAlternatePrefab(index, 0.5f))
            .ToArray();
        CollectionAssert.AreEqual(
            new[] { false, true, false, true, false, true, false, true, false, true },
            sequence);
    }

    [Test]
    public void AlternateShareHonoursEdgesAndClamps()
    {
        Assert.That(Enumerable.Range(1, 10).Any(i => EnemySpawnPoint.UseAlternatePrefab(i, 0f)), Is.False);
        Assert.That(Enumerable.Range(1, 10).All(i => EnemySpawnPoint.UseAlternatePrefab(i, 1f)), Is.True);
        Assert.That(EnemySpawnPoint.UseAlternatePrefab(0, 0.5f), Is.False, "序号从 1 开始");
        Assert.That(EnemySpawnPoint.GetAlternateCount(10, -1f), Is.EqualTo(0));
        Assert.That(EnemySpawnPoint.GetAlternateCount(10, 2f), Is.EqualTo(10));
    }

    [Test]
    public void WaveMixingSpawnsHalfFastRunnersAtSpeciesSpeed()
    {
        var fastPrefab = LoadPrefab();
        var normalPrefab = new GameObject("NormalGruntStub");
        var spawnGo = new GameObject("MixSpawnStub");
        var spawned = new System.Collections.Generic.List<GameObject>();
        try
        {
            var spawn = spawnGo.AddComponent<EnemySpawnPoint>();
            var so = new SerializedObject(spawn);
            var waves = so.FindProperty("waveSpawnSettings");
            Assert.That(waves, Is.Not.Null);
            waves.arraySize = 1;
            var wave = waves.GetArrayElementAtIndex(0);
            wave.FindPropertyRelative("enabled").boolValue = true;
            wave.FindPropertyRelative("enemyCount").intValue = 10;
            wave.FindPropertyRelative("enemyPrefab").objectReferenceValue = normalPrefab;
            wave.FindPropertyRelative("alternateEnemyPrefab").objectReferenceValue = fastPrefab;
            wave.FindPropertyRelative("alternateShare").floatValue = 0.5f;
            wave.FindPropertyRelative("alternateMoveSpeed").floatValue = MonsterSpeciesCatalog.FastRunner.MoveSpeed;
            so.ApplyModifiedPropertiesWithoutUndo();

            var create = typeof(EnemySpawnPoint).GetMethod("CreateEnemy",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.That(create, Is.Not.Null, "找不到 EnemySpawnPoint.CreateEnemy");

            var fastCount = 0;
            for (var index = 1; index <= 10; index++)
            {
                var instance = (EnemyInstance)create.Invoke(spawn, new object[] { 1, index });
                Assert.That(instance, Is.Not.Null);
                spawned.Add(instance.gameObject);

                var isFast = instance.gameObject.name.StartsWith("FastRunner");
                if (!isFast) continue;
                fastCount++;
                Assert.That(instance.PathFollower, Is.Not.Null);
                Assert.That(instance.PathFollower.MoveSpeed,
                    Is.EqualTo(MonsterSpeciesCatalog.FastRunner.MoveSpeed).Within(0.01f),
                    "快速小怪应使用数值表里的移速");
            }

            Assert.That(fastCount, Is.EqualTo(5), "10 只里应正好 5 只快速小怪");
        }
        finally
        {
            foreach (var go in spawned)
            {
                if (go != null) Object.DestroyImmediate(go);
            }

            Object.DestroyImmediate(normalPrefab);
            Object.DestroyImmediate(spawnGo);
        }
    }

    [Test]
    public void ApplyMixedWavesRestoresPrimaryPrefabOnFastOnlyWaves()
    {
        var fastPrefab = LoadPrefab();
        var normalPrefab = new GameObject("NormalGruntStub");
        var spawnGo = new GameObject("MixAuthoringStub");
        try
        {
            var spawn = spawnGo.AddComponent<EnemySpawnPoint>();
            var so = new SerializedObject(spawn);
            so.FindProperty("enemyPrefab").objectReferenceValue = normalPrefab;
            // 波次列表会被 OnValidate 补齐到总波数，所以先固定成 2 波再建条目。
            so.FindProperty("totalWaves").intValue = 2;
            var waves = so.FindProperty("waveSpawnSettings");
            waves.arraySize = 2;
            for (var i = 0; i < waves.arraySize; i++)
            {
                var element = waves.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("enabled").boolValue = true;
                element.FindPropertyRelative("enemyCount").intValue = 10;
                // 第 1 波：旧配置整波快速小怪；第 2 波：普通小怪。
                element.FindPropertyRelative("enemyPrefab").objectReferenceValue = i == 0 ? fastPrefab : normalPrefab;
            }

            so.ApplyModifiedPropertiesWithoutUndo();

            var expectedWaves = new SerializedObject(spawn).FindProperty("waveSpawnSettings").arraySize;
            Assert.That(expectedWaves, Is.EqualTo(2), "测试夹具应正好 2 波");
            var changed = FastRunnerAuthoring.ApplyMixedWaves(spawn, fastPrefab, FastRunnerAuthoring.DefaultFastShare);
            Assert.That(changed, Is.EqualTo(expectedWaves));

            var check = new SerializedObject(spawn);
            var checkedWaves = check.FindProperty("waveSpawnSettings");
            for (var i = 0; i < 2; i++)
            {
                var element = checkedWaves.GetArrayElementAtIndex(i);
                Assert.That(element.FindPropertyRelative("enemyPrefab").objectReferenceValue, Is.SameAs(normalPrefab),
                    "第 " + (i + 1) + " 波的主预制体应是普通小怪");
                Assert.That(element.FindPropertyRelative("alternateEnemyPrefab").objectReferenceValue, Is.SameAs(fastPrefab));
                Assert.That(element.FindPropertyRelative("alternateShare").floatValue, Is.EqualTo(0.5f).Within(0.001f));
                Assert.That(element.FindPropertyRelative("alternateMoveSpeed").floatValue,
                    Is.EqualTo(MonsterSpeciesCatalog.FastRunner.MoveSpeed).Within(0.01f));
                Assert.That(element.FindPropertyRelative("enemyCount").intValue, Is.EqualTo(10), "不应改动波次数量");
            }
        }
        finally
        {
            Object.DestroyImmediate(normalPrefab);
            Object.DestroyImmediate(spawnGo);
        }
    }
}
#endif
