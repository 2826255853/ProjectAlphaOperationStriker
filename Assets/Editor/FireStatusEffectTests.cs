#if UNITY_INCLUDE_TESTS
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

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
        // Ordinary MonoBehaviour.Awake is not dispatched by EditMode creation, so CurrentHealth would
        // stay 0 and FireStatusEffect.Ignite would early-out on IsDead. Full health matches runtime.
        health.ResetHealth();
    }

    [TearDown]
    public void TearDown()
    {
        LogAssert.ignoreFailingMessages = false;
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
        burn = enemyObject.GetComponent<FireStatusEffect>();
        Assert.That(burn, Is.Not.Null);
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
        // The kill path calls Destroy, which EditMode cannot perform and the engine reports as an error.
        LogAssert.ignoreFailingMessages = true;
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

        // MonoBehaviours without ExecuteAlways receive no lifecycle messages in EditMode, and routing
        // OnDisable through SendMessage trips Unity's native ShouldRunBehaviour assert. Invoke the
        // private handler directly so the disable path stays covered outside play mode.
        System.Reflection.MethodInfo onDisable = typeof(FireStatusEffect)
            .GetMethod("OnDisable", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.That(onDisable, Is.Not.Null);
        onDisable.Invoke(burn, null);

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
