#if UNITY_INCLUDE_TESTS
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// Step 2 checks for docs/ENEMY_DAMAGE_RESISTANCE_PROPOSAL.md:
/// the single categorized settlement entry on <see cref="EnemyHealth"/>.
/// Covers zero/positive/negative resistance for all seven categories, zero final damage,
/// the actual mitigated values reported by events, post-death damage and single death notification,
/// plus the legacy <see cref="TrapDamageType"/> compatibility mapping.
/// </summary>
public sealed class EnemyDamageSettlementTests
{
    private const float Tolerance = 0.0001f;

    private GameObject enemyObject;
    private EnemyHealth health;

    [SetUp]
    public void SetUp()
    {
        enemyObject = new GameObject("Enemy damage settlement test enemy");
        health = enemyObject.AddComponent<EnemyHealth>();
        // Ordinary MonoBehaviour.Awake is not dispatched by EditMode creation.
        health.ResetHealth();
    }

    [TearDown]
    public void TearDown()
    {
        LogAssert.ignoreFailingMessages = false;
        if (enemyObject != null)
        {
            Object.DestroyImmediate(enemyObject);
        }

        enemyObject = null;
        health = null;
    }

    [TestCase(DamageType.Fire)]
    [TestCase(DamageType.Electric)]
    [TestCase(DamageType.Frost)]
    [TestCase(DamageType.Light)]
    [TestCase(DamageType.RangedPhysical)]
    [TestCase(DamageType.MeleePhysical)]
    [TestCase(DamageType.Explosive)]
    public void ZeroResistanceAppliesRequestedDamageForEveryCategory(DamageType type)
    {
        float observed = -1f;
        DamageType observedType = default;
        int typedEvents = 0;
        health.TypedDamaged += (_, amount, category) =>
        {
            observed = amount;
            observedType = category;
            typedEvents++;
        };

        health.TakeDamage(25f, type);

        Assert.That(health.CurrentHealth, Is.EqualTo(75f).Within(Tolerance), type.ToString());
        Assert.That(typedEvents, Is.EqualTo(1), type.ToString());
        Assert.That(observed, Is.EqualTo(25f).Within(Tolerance), type.ToString());
        Assert.That(observedType, Is.EqualTo(type));
    }

    [TestCase(DamageType.Fire, 30f, 70f)]
    [TestCase(DamageType.Electric, 25f, 75f)]
    [TestCase(DamageType.Frost, 60f, 40f)]
    [TestCase(DamageType.Light, 10f, 90f)]
    [TestCase(DamageType.RangedPhysical, 45f, 55f)]
    [TestCase(DamageType.MeleePhysical, 80f, 20f)]
    [TestCase(DamageType.Explosive, 15f, 85f)]
    public void PositiveResistanceReducesDamageExactlyOnce(DamageType type, float resistancePercent, float expectedDamage)
    {
        health.SetResistancePercent(type, resistancePercent);
        float observed = -1f;
        health.TypedDamaged += (_, amount, _) => observed = amount;

        health.TakeDamage(100f, type);

        Assert.That(health.CurrentHealth, Is.EqualTo(100f - expectedDamage).Within(Tolerance), type.ToString());
        Assert.That(observed, Is.EqualTo(expectedDamage).Within(Tolerance), type.ToString());
    }

    [TestCase(DamageType.Fire, -30f, 130f)]
    [TestCase(DamageType.Electric, -100f, 200f)]
    [TestCase(DamageType.Frost, -50f, 150f)]
    [TestCase(DamageType.Light, -10f, 110f)]
    [TestCase(DamageType.RangedPhysical, -20f, 120f)]
    [TestCase(DamageType.MeleePhysical, -100f, 200f)]
    [TestCase(DamageType.Explosive, -25f, 125f)]
    public void NegativeResistanceAmplifiesDamage(DamageType type, float resistancePercent, float expectedDamage)
    {
        health.SetResistancePercent(type, resistancePercent);
        float observed = -1f;
        health.TypedDamaged += (_, amount, _) => observed = amount;

        // maxHealth is 100; use a small request so amplification cannot be clamped by the health pool.
        health.TakeDamage(10f, type);

        float expectedApplied = expectedDamage / 10f;
        Assert.That(health.CurrentHealth, Is.EqualTo(100f - expectedApplied).Within(Tolerance), type.ToString());
        Assert.That(observed, Is.EqualTo(expectedApplied).Within(Tolerance), type.ToString());
    }

    [TestCase(100f)]
    [TestCase(150f)]
    public void FullResistanceDealsNoDamageAndRaisesNoEvents(float resistancePercent)
    {
        health.SetResistancePercent(DamageType.Fire, resistancePercent);
        int damagedEvents = 0;
        int typedEvents = 0;
        int diedEvents = 0;
        health.Damaged += _ => damagedEvents++;
        health.TypedDamaged += (_, _, _) => typedEvents++;
        health.Died += _ => diedEvents++;

        health.TakeDamage(50f, DamageType.Fire);

        Assert.That(health.CurrentHealth, Is.EqualTo(100f).Within(Tolerance));
        Assert.That(health.IsDead, Is.False);
        Assert.That(damagedEvents, Is.Zero);
        Assert.That(typedEvents, Is.Zero);
        Assert.That(diedEvents, Is.Zero);
    }

    [Test]
    public void TypedEventReportsActuallyDeductedHealthNotRequestedAmount()
    {
        // The final hit kills the enemy; EditMode cannot really destroy objects and logs an engine error.
        LogAssert.ignoreFailingMessages = true;
        health.SetResistancePercent(DamageType.Fire, 30f);
        health.TakeDamage(95f, DamageType.Fire); // 95 * 0.7 = 66.5, remains 33.5

        float lastTypedAmount = -1f;
        float lastLegacyAmount = -1f;
        health.TypedDamaged += (_, amount, _) => lastTypedAmount = amount;
        health.TrapDamaged += (_, amount, _) => lastLegacyAmount = amount;

        health.TakeDamage(100f, TrapDamageType.Fire); // 70 requested after mitigation, clamped to remaining 33.5

        Assert.That(health.CurrentHealth, Is.EqualTo(0f).Within(Tolerance));
        Assert.That(lastTypedAmount, Is.EqualTo(33.5f).Within(Tolerance));
        Assert.That(lastLegacyAmount, Is.EqualTo(33.5f).Within(Tolerance));
    }

    [Test]
    public void UncategorizedDamageIgnoresEveryResistanceAndRaisesNoTypedEvent()
    {
        foreach (DamageType type in DamageTypeCatalog.All)
        {
            health.SetResistancePercent(type, 90f);
        }

        bool damagedRaised = false;
        bool trapRaised = false;
        bool typedRaised = false;
        health.Damaged += _ => damagedRaised = true;
        health.TrapDamaged += (_, _, _) => trapRaised = true;
        health.TypedDamaged += (_, _, _) => typedRaised = true;

        health.TakeDamage(20f);

        Assert.That(health.CurrentHealth, Is.EqualTo(80f).Within(Tolerance));
        Assert.That(damagedRaised, Is.True);
        Assert.That(trapRaised, Is.False);
        Assert.That(typedRaised, Is.False);
    }

    [TestCase(TrapDamageType.Bullet, DamageType.RangedPhysical)]
    [TestCase(TrapDamageType.Melee, DamageType.MeleePhysical)]
    [TestCase(TrapDamageType.Explosive, DamageType.Explosive)]
    [TestCase(TrapDamageType.Fire, DamageType.Fire)]
    [TestCase(TrapDamageType.Energy, DamageType.Electric)]
    public void LegacyTrapCategorySettlesMappedResistanceOnceAndKeepsLegacyEvent(TrapDamageType legacy, DamageType expected)
    {
        health.SetResistancePercent(expected, 50f);
        float trapAmount = -1f;
        TrapDamageType trapType = TrapDamageType.Physical;
        float typedAmount = -1f;
        DamageType typedType = default;
        health.TrapDamaged += (_, amount, type) =>
        {
            trapAmount = amount;
            trapType = type;
        };
        health.TypedDamaged += (_, amount, type) =>
        {
            typedAmount = amount;
            typedType = type;
        };

        health.TakeDamage(20f, legacy);

        Assert.That(health.CurrentHealth, Is.EqualTo(90f).Within(Tolerance), "resistance must be applied exactly once");
        Assert.That(trapAmount, Is.EqualTo(10f).Within(Tolerance));
        Assert.That(trapType, Is.EqualTo(legacy));
        Assert.That(typedAmount, Is.EqualTo(10f).Within(Tolerance));
        Assert.That(typedType, Is.EqualTo(expected));
    }

    [Test]
    public void LegacyPhysicalStaysUncategorizedRawDamage()
    {
        foreach (DamageType type in DamageTypeCatalog.All)
        {
            health.SetResistancePercent(type, 80f);
        }

        float trapAmount = -1f;
        TrapDamageType trapType = TrapDamageType.Fire;
        bool typedRaised = false;
        health.TrapDamaged += (_, amount, type) =>
        {
            trapAmount = amount;
            trapType = type;
        };
        health.TypedDamaged += (_, _, _) => typedRaised = true;

        health.TakeDamage(20f, TrapDamageType.Physical);

        Assert.That(health.CurrentHealth, Is.EqualTo(80f).Within(Tolerance));
        Assert.That(trapAmount, Is.EqualTo(20f).Within(Tolerance));
        Assert.That(trapType, Is.EqualTo(TrapDamageType.Physical));
        Assert.That(typedRaised, Is.False);
    }

    [Test]
    public void DamageAfterDeathIsIgnoredAndDeathIsRaisedOnce()
    {
        // EditMode cannot really destroy objects; suppress the engine's destroy-in-edit-mode error.
        LogAssert.ignoreFailingMessages = true;
        health.SetResistancePercent(DamageType.Fire, 30f);
        int diedEvents = 0;
        int typedEvents = 0;
        health.Died += _ => diedEvents++;
        health.TypedDamaged += (_, _, _) => typedEvents++;

        health.TakeDamage(100f, DamageType.Fire); // 70 applied, 30 remains
        Assert.That(health.CurrentHealth, Is.EqualTo(30f).Within(Tolerance));
        Assert.That(typedEvents, Is.EqualTo(1));

        health.TakeDamage(100f, DamageType.Fire); // 70 requested, clamped to 30
        Assert.That(health.CurrentHealth, Is.EqualTo(0f).Within(Tolerance));
        Assert.That(health.IsDead, Is.True);
        Assert.That(diedEvents, Is.EqualTo(1));
        Assert.That(typedEvents, Is.EqualTo(2));

        health.TakeDamage(100f, DamageType.Fire);
        health.TakeDamage(100f, TrapDamageType.Fire);
        health.TakeDamage(100f);

        Assert.That(health.CurrentHealth, Is.EqualTo(0f).Within(Tolerance));
        Assert.That(diedEvents, Is.EqualTo(1), "death must be raised exactly once");
        Assert.That(typedEvents, Is.EqualTo(2), "no settlement after death");
    }

    [Test]
    public void ZeroAndNegativeRequestsAreIgnored()
    {
        bool damagedRaised = false;
        bool typedRaised = false;
        health.Damaged += _ => damagedRaised = true;
        health.TypedDamaged += (_, _, _) => typedRaised = true;

        health.TakeDamage(0f, DamageType.Fire);
        health.TakeDamage(-25f, DamageType.Fire);
        health.TakeDamage(0f, TrapDamageType.Fire);
        health.TakeDamage(-25f, TrapDamageType.Fire);
        health.TakeDamage(0f);

        Assert.That(health.CurrentHealth, Is.EqualTo(100f).Within(Tolerance));
        Assert.That(damagedRaised, Is.False);
        Assert.That(typedRaised, Is.False);
    }

    [Test]
    public void CalculateDamageIsPureAndMatchesSettlement()
    {
        health.SetResistancePercent(DamageType.Explosive, 40f);
        bool anyEvent = false;
        health.Damaged += _ => anyEvent = true;
        health.TypedDamaged += (_, _, _) => anyEvent = true;

        Assert.That(health.CalculateDamage(50f, DamageType.Explosive), Is.EqualTo(30f).Within(Tolerance));
        Assert.That(health.CalculateDamage(0f, DamageType.Explosive), Is.Zero);
        Assert.That(health.CalculateDamage(50f, DamageType.Frost), Is.EqualTo(50f).Within(Tolerance));
        Assert.That(health.CurrentHealth, Is.EqualTo(100f).Within(Tolerance));
        Assert.That(anyEvent, Is.False);
    }

    [Test]
    public void ResetHealthKeepsResistancesWhileSettlementStillAppliesThem()
    {
        LogAssert.ignoreFailingMessages = true;
        health.SetResistancePercent(DamageType.MeleePhysical, 20f);
        health.TakeDamage(100f, DamageType.MeleePhysical);
        Assert.That(health.CurrentHealth, Is.EqualTo(20f).Within(Tolerance));

        health.ResetHealth();

        Assert.That(health.CurrentHealth, Is.EqualTo(100f).Within(Tolerance));
        Assert.That(health.IsDead, Is.False);
        Assert.That(health.GetResistancePercent(DamageType.MeleePhysical), Is.EqualTo(20f));

        health.TakeDamage(100f, DamageType.MeleePhysical);
        Assert.That(health.CurrentHealth, Is.EqualTo(20f).Within(Tolerance));
    }
}
#endif
