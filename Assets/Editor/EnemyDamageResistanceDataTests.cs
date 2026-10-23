#if UNITY_INCLUDE_TESTS
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Data-contract checks for step 1 of docs/ENEMY_DAMAGE_RESISTANCE_PROPOSAL.md:
/// the authoritative DamageType enum, the fixed legacy trap mapping and per-instance resistance configuration.
/// These tests must not require any change to the damage pipeline.
/// </summary>
public sealed class EnemyDamageResistanceDataTests
{
    private static readonly string[] ResistanceFieldNames =
    {
        "fireResistancePercent",
        "electricResistancePercent",
        "frostResistancePercent",
        "lightResistancePercent",
        "rangedPhysicalResistancePercent",
        "meleePhysicalResistancePercent",
        "explosiveResistancePercent"
    };

    private static GameObject CreateEnemy(out EnemyHealth health)
    {
        GameObject enemy = new GameObject("Damage resistance test enemy");
        health = enemy.AddComponent<EnemyHealth>();
        return enemy;
    }

    [Test]
    public void DamageTypeDefinesSevenDistinctCategoriesWithStableValues()
    {
        Assert.That(System.Enum.GetValues(typeof(DamageType)).Length, Is.EqualTo(7));
        Assert.That(DamageTypeCatalog.Count, Is.EqualTo(7));
        Assert.That(DamageTypeCatalog.All.Length, Is.EqualTo(DamageTypeCatalog.Count));
        CollectionAssert.AllItemsAreUnique(DamageTypeCatalog.All);

        Assert.That((int)DamageType.Fire, Is.EqualTo(0));
        Assert.That((int)DamageType.Electric, Is.EqualTo(1));
        Assert.That((int)DamageType.Frost, Is.EqualTo(2));
        Assert.That((int)DamageType.Light, Is.EqualTo(3));
        Assert.That((int)DamageType.RangedPhysical, Is.EqualTo(4));
        Assert.That((int)DamageType.MeleePhysical, Is.EqualTo(5));
        Assert.That((int)DamageType.Explosive, Is.EqualTo(6));

        foreach (DamageType type in DamageTypeCatalog.All)
        {
            Assert.That(DamageTypeCatalog.IsDefined(type), Is.True, type.ToString());
        }

        Assert.That(DamageTypeCatalog.IsDefined((DamageType)99), Is.False);
    }

    [Test]
    public void LegacyTrapDamageTypeValuesRemainUnchanged()
    {
        Assert.That((int)TrapDamageType.Physical, Is.EqualTo(0));
        Assert.That((int)TrapDamageType.Fire, Is.EqualTo(1));
        Assert.That((int)TrapDamageType.Explosive, Is.EqualTo(2));
        Assert.That((int)TrapDamageType.Energy, Is.EqualTo(3));
        Assert.That((int)TrapDamageType.Bullet, Is.EqualTo(4));
        Assert.That((int)TrapDamageType.Melee, Is.EqualTo(5));
    }

    [TestCase(TrapDamageType.Fire, DamageType.Fire)]
    [TestCase(TrapDamageType.Explosive, DamageType.Explosive)]
    [TestCase(TrapDamageType.Energy, DamageType.Electric)]
    [TestCase(TrapDamageType.Bullet, DamageType.RangedPhysical)]
    [TestCase(TrapDamageType.Melee, DamageType.MeleePhysical)]
    public void LegacyTrapCategoryMapsToFixedDamageType(TrapDamageType legacy, DamageType expected)
    {
        Assert.That(legacy.TryGetDamageType(out DamageType mapped), Is.True);
        Assert.That(mapped, Is.EqualTo(expected));
    }

    [Test]
    public void LegacyPhysicalAndUnknownValuesStayUncategorized()
    {
        Assert.That(TrapDamageType.Physical.TryGetDamageType(out DamageType physical), Is.False);
        Assert.That(physical, Is.EqualTo(default(DamageType)));
        Assert.That(((TrapDamageType)99).TryGetDamageType(out _), Is.False);
    }

    [Test]
    public void EnemyHealthDefaultsEveryResistanceToZero()
    {
        GameObject enemy = null;
        try
        {
            enemy = CreateEnemy(out EnemyHealth health);

            foreach (DamageType type in DamageTypeCatalog.All)
            {
                Assert.That(health.GetResistancePercent(type), Is.EqualTo(0f), type.ToString());
                Assert.That(health.GetDamageMultiplier(type), Is.EqualTo(1f), type.ToString());
            }

            Assert.That(health.GetResistancePercent((DamageType)99), Is.EqualTo(0f));
            Assert.That(health.GetDamageMultiplier((DamageType)99), Is.EqualTo(1f));
        }
        finally
        {
            if (enemy != null)
            {
                Object.DestroyImmediate(enemy);
            }
        }
    }

    [Test]
    public void ResistanceConfigurationIsSerializedPerCategory()
    {
        GameObject enemy = null;
        try
        {
            enemy = CreateEnemy(out EnemyHealth health);
            SerializedObject serialized = new SerializedObject(health);

            foreach (string fieldName in ResistanceFieldNames)
            {
                SerializedProperty property = serialized.FindProperty(fieldName);
                Assert.That(property, Is.Not.Null, fieldName);
                Assert.That(property.propertyType, Is.EqualTo(SerializedPropertyType.Float), fieldName);
            }

            Assert.That(ResistanceFieldNames.Length, Is.EqualTo(DamageTypeCatalog.Count));
        }
        finally
        {
            if (enemy != null)
            {
                Object.DestroyImmediate(enemy);
            }
        }
    }

    [Test]
    public void EnemyHealthInstancesKeepIndependentResistances()
    {
        GameObject firstObject = null;
        GameObject secondObject = null;
        try
        {
            firstObject = CreateEnemy(out EnemyHealth first);
            secondObject = CreateEnemy(out EnemyHealth second);

            first.SetResistancePercent(DamageType.Fire, 40f);
            first.SetResistancePercent(DamageType.Explosive, -25f);

            Assert.That(first.GetResistancePercent(DamageType.Fire), Is.EqualTo(40f));
            Assert.That(first.GetResistancePercent(DamageType.Explosive), Is.EqualTo(-25f));

            foreach (DamageType type in DamageTypeCatalog.All)
            {
                Assert.That(second.GetResistancePercent(type), Is.EqualTo(0f), type.ToString());
            }

            Assert.That(first.GetResistancePercent(DamageType.Electric), Is.EqualTo(0f));
        }
        finally
        {
            if (firstObject != null)
            {
                Object.DestroyImmediate(firstObject);
            }

            if (secondObject != null)
            {
                Object.DestroyImmediate(secondObject);
            }
        }
    }

    [Test]
    public void RuntimeCloneInheritsSourceResistanceConfiguration()
    {
        GameObject source = null;
        GameObject clone = null;
        try
        {
            source = CreateEnemy(out EnemyHealth original);
            original.SetResistancePercent(DamageType.Electric, 25f);
            original.SetResistancePercent(DamageType.RangedPhysical, -10f);

            clone = Object.Instantiate(source);
            EnemyHealth copy = clone.GetComponent<EnemyHealth>();

            Assert.That(copy, Is.Not.Null);
            Assert.That(copy.GetResistancePercent(DamageType.Electric), Is.EqualTo(25f));
            Assert.That(copy.GetResistancePercent(DamageType.RangedPhysical), Is.EqualTo(-10f));
            Assert.That(copy.GetResistancePercent(DamageType.Fire), Is.EqualTo(0f));

            copy.SetResistancePercent(DamageType.Electric, 70f);
            Assert.That(original.GetResistancePercent(DamageType.Electric), Is.EqualTo(25f));
        }
        finally
        {
            if (clone != null)
            {
                Object.DestroyImmediate(clone);
            }

            if (source != null)
            {
                Object.DestroyImmediate(source);
            }
        }
    }

    [Test]
    public void ResetHealthKeepsResistanceConfiguration()
    {
        GameObject enemy = null;
        try
        {
            enemy = CreateEnemy(out EnemyHealth health);
            health.ResetHealth();
            health.SetResistancePercent(DamageType.Fire, 35f);
            health.SetResistancePercent(DamageType.Frost, -20f);
            health.SetResistancePercent(DamageType.Light, 100f);

            health.ResetHealth();

            Assert.That(health.GetResistancePercent(DamageType.Fire), Is.EqualTo(35f));
            Assert.That(health.GetResistancePercent(DamageType.Frost), Is.EqualTo(-20f));
            Assert.That(health.GetResistancePercent(DamageType.Light), Is.EqualTo(100f));
            Assert.That(health.GetResistancePercent(DamageType.Electric), Is.EqualTo(0f));
        }
        finally
        {
            if (enemy != null)
            {
                Object.DestroyImmediate(enemy);
            }
        }
    }

    [Test]
    public void SetResistancePercentIgnoresUndefinedCategoriesAndNonFiniteValues()
    {
        GameObject enemy = null;
        try
        {
            enemy = CreateEnemy(out EnemyHealth health);

            health.SetResistancePercent((DamageType)99, 50f);
            health.SetResistancePercent(DamageType.Fire, float.NaN);
            health.SetResistancePercent(DamageType.Fire, float.PositiveInfinity);

            foreach (DamageType type in DamageTypeCatalog.All)
            {
                Assert.That(health.GetResistancePercent(type), Is.EqualTo(0f), type.ToString());
            }
        }
        finally
        {
            if (enemy != null)
            {
                Object.DestroyImmediate(enemy);
            }
        }
    }

    [TestCase(0f, 1f)]
    [TestCase(30f, 0.7f)]
    [TestCase(-30f, 1.3f)]
    [TestCase(100f, 0f)]
    [TestCase(150f, 0f)]
    [TestCase(-100f, 2f)]
    public void ResistanceMultiplierFollowsPercentFormula(float resistancePercent, float expectedMultiplier)
    {
        GameObject enemy = null;
        try
        {
            enemy = CreateEnemy(out EnemyHealth health);
            health.SetResistancePercent(DamageType.Fire, resistancePercent);

            Assert.That(health.GetDamageMultiplier(DamageType.Fire), Is.EqualTo(expectedMultiplier).Within(0.0001f));
        }
        finally
        {
            if (enemy != null)
            {
                Object.DestroyImmediate(enemy);
            }
        }
    }
}
#endif
