#if UNITY_INCLUDE_TESTS
using NUnit.Framework;
using UnityEngine;

public sealed class TrapDamageClassificationTests
{
    [Test]
    public void DamageCategoriesKeepLegacyValuesAndExposeSpecificTypes()
    {
        Assert.That((int)TrapDamageType.Physical, Is.EqualTo(0));
        Assert.That((int)TrapDamageType.Fire, Is.EqualTo(1));
        Assert.That((int)TrapDamageType.Explosive, Is.EqualTo(2));
        Assert.That((int)TrapDamageType.Energy, Is.EqualTo(3));
        Assert.That(TrapDamageType.Bullet, Is.Not.EqualTo(TrapDamageType.Melee));
        Assert.That(TrapDamageType.Bullet, Is.Not.EqualTo(TrapDamageType.Physical));
        Assert.That(TrapDamageType.Melee, Is.Not.EqualTo(TrapDamageType.Physical));
    }

    [TestCase(TrapDamageType.Bullet)]
    [TestCase(TrapDamageType.Melee)]
    [TestCase(TrapDamageType.Explosive)]
    [TestCase(TrapDamageType.Fire)]
    public void TypedTrapDamagePublishesCategoryWithoutChangingDamage(TrapDamageType expectedType)
    {
        GameObject enemy = new GameObject("Trap damage classification test enemy");
        try
        {
            EnemyHealth health = enemy.AddComponent<EnemyHealth>();
            TrapDamageType observedType = TrapDamageType.Physical;
            float observedAmount = 0f;
            health.TrapDamaged += (_, amount, type) =>
            {
                observedAmount = amount;
                observedType = type;
            };

            health.TakeDamage(12f, expectedType);

            Assert.That(health.CurrentHealth, Is.EqualTo(health.MaxHealth - 12f));
            Assert.That(observedAmount, Is.EqualTo(12f));
            Assert.That(observedType, Is.EqualTo(expectedType));
        }
        finally
        {
            Object.DestroyImmediate(enemy);
        }
    }

    [Test]
    public void PlayerDamageOverloadStillAppliesDamageWithoutTrapEvent()
    {
        GameObject enemy = new GameObject("Player damage compatibility test enemy");
        try
        {
            EnemyHealth health = enemy.AddComponent<EnemyHealth>();
            bool trapEventRaised = false;
            health.TrapDamaged += (_, _, _) => trapEventRaised = true;

            health.TakeDamage(9f);

            Assert.That(health.CurrentHealth, Is.EqualTo(health.MaxHealth - 9f));
            Assert.That(trapEventRaised, Is.False);
        }
        finally
        {
            Object.DestroyImmediate(enemy);
        }
    }
}
#endif
