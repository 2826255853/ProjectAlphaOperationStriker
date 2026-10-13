#if UNITY_INCLUDE_TESTS
using NUnit.Framework;
using UnityEngine;

public sealed class FireStatusEffectTests
{
    private GameObject enemyObject;
    private EnemyHealth health;
    private FireStatusEffect burn;

    [SetUp]
    public void SetUp()
    {
        enemyObject = new GameObject("Fire status test enemy");
        health = enemyObject.AddComponent<EnemyHealth>();
    }

    [TearDown]
    public void TearDown()
    {
        if (enemyObject != null) Object.DestroyImmediate(enemyObject);
    }

    [Test]
    public void DefaultIgnitionDealsFiveDamageOverOneSecond()
    {
        IgniteDefault();

        burn.Tick(0.4f);
        Assert.That(health.CurrentHealth, Is.EqualTo(98f).Within(0.001f));
        Assert.That(burn.IsBurning, Is.True);

        burn.Tick(0.6f);
        Assert.That(health.CurrentHealth, Is.EqualTo(95f).Within(0.001f));
        Assert.That(burn.IsBurning, Is.False);
        Assert.That(burn.RemainingSeconds, Is.Zero);
    }

    [Test]
    public void LongFrameNeverDealsDamagePastBurnExpiry()
    {
        IgniteDefault();

        burn.Tick(10f);

        Assert.That(health.CurrentHealth, Is.EqualTo(95f).Within(0.001f));
        Assert.That(burn.IsBurning, Is.False);
    }

    [Test]
    public void ReignitionRefreshesDurationWithoutStackingAndKeepsHigherRate()
    {
        FireStatusEffect.Ignite(health, 0.5f, 2f);
        burn.Tick(0.25f);
        FireStatusEffect.Ignite(health, 0.75f, 3f);

        Assert.That(burn.RemainingSeconds, Is.EqualTo(0.75f).Within(0.001f));
        Assert.That(burn.DamagePerSecond, Is.EqualTo(3f));
        burn.Tick(0.75f);
        Assert.That(health.CurrentHealth, Is.EqualTo(97.25f).Within(0.001f));
    }

    [Test]
    public void ResetHealthClearsBurnForPooledEnemyReuse()
    {
        IgniteDefault();

        health.ResetHealth();

        Assert.That(burn.IsBurning, Is.False);
        Assert.That(burn.DamagePerSecond, Is.Zero);
        Assert.That(health.CurrentHealth, Is.EqualTo(health.MaxHealth));
    }

    [Test]
    public void DeathClearsBurnBeforeDeathCallbacksRun()
    {
        IgniteDefault();
        bool burnWasClearAtDeath = false;
        health.Died += _ => burnWasClearAtDeath = !burn.IsBurning;

        health.TakeDamage(health.CurrentHealth);

        Assert.That(burnWasClearAtDeath, Is.True);
    }

    [Test]
    public void DisablingEnemyClearsBurn()
    {
        IgniteDefault();

        enemyObject.SetActive(false);

        Assert.That(burn.IsBurning, Is.False);
        Assert.That(burn.DamagePerSecond, Is.Zero);
    }

    private void IgniteDefault()
    {
        FireStatusEffect.Ignite(health);
        burn = enemyObject.GetComponent<FireStatusEffect>();
        Assert.That(burn, Is.Not.Null);
        Assert.That(burn.RemainingSeconds, Is.EqualTo(1f));
        Assert.That(burn.DamagePerSecond, Is.EqualTo(5f));
    }
}
#endif
