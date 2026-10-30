using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Step 10 compatibility checks; staged into Assets/Editor only by the test runner.</summary>
public sealed class TrapUpgradeStep10Tests
{
    private readonly List<Object> objects = new List<Object>();

    [TearDown]
    public void TearDown()
    {
        for (int i = objects.Count - 1; i >= 0; --i)
            if (objects[i] != null) Object.DestroyImmediate(objects[i]);
        objects.Clear();
    }

    [Test]
    public void MissingCatalogKeepsDefaultNormalGrowthAndHasNoBranches()
    {
        TrapDefinition definition = NewDefinition("step10-defaults", 40);
        Guid runId = Guid.NewGuid();
        using (var service = new TrapUpgradeService(() => runId))
        {
            TrapUpgradeQuote quote = service.GetQuote(definition);
            Assert.That(quote.CanUpgrade, Is.True);
            Assert.That(quote.TargetMultipliers.Damage, Is.EqualTo(1.25f));
            Assert.That(quote.Cost, Is.EqualTo(20));
            Assert.That(service.GetBranches(definition.TrapId), Is.Empty);
            Assert.That(service.TryUpgrade(quote, out TrapUpgradeFailure failure), Is.True, failure.ToString());
            Assert.That(service.TryUpgrade(service.GetQuote(definition), out failure), Is.True, failure.ToString());
            TrapUpgradeQuote maxNormal = service.GetQuote(definition, TrapUpgradeDirection.Normal);
            Assert.That(maxNormal.CanUpgrade, Is.True);
            Assert.That(maxNormal.TargetMultipliers.Damage, Is.EqualTo(2f));
            Assert.That(service.TryUpgrade(maxNormal, out failure), Is.True, failure.ToString());
            Assert.That(service.GetState(definition.TrapId), Is.EqualTo(
                new TrapUpgradeState(4, TrapUpgradeDirection.Normal)));
        }
    }

    [Test]
    public void Betty7InitialPriceIsAppliedOnlyForANewDefinition()
    {
        TrapDefinition definition = NewDefinition("betty7", TrapDefinition.DefaultCost);
        var settings = new SerializedObject(definition);
        Method("SetInitialDefinitionCost", typeof(SerializedObject), typeof(bool), typeof(int))
            .Invoke(null, new object[] { settings, false, 60 });
        settings.ApplyModifiedPropertiesWithoutUndo();
        Assert.That(definition.Cost, Is.EqualTo(TrapDefinition.DefaultCost));

        Method("SetInitialDefinitionCost", typeof(SerializedObject), typeof(bool), typeof(int))
            .Invoke(null, new object[] { settings, true, 60 });
        settings.ApplyModifiedPropertiesWithoutUndo();
        Assert.That(definition.Cost, Is.EqualTo(60));
    }

    [Test]
    public void RepairStationRegenerationCopiesBaseHealthAndManualRepairTuning()
    {
        var source = new GameObject("repair tuning source");
        var destination = new GameObject("repair tuning destination");
        objects.Add(source);
        objects.Add(destination);
        TrapInstance previousInstance = source.AddComponent<TrapInstance>();
        TrapInstance currentInstance = destination.AddComponent<TrapInstance>();
        SetFloat(previousInstance, "maxHealth", 187f);
        Method(typeof(RepairStationAuthoring), "CopyManualFloat", typeof(Object), typeof(Object), typeof(string))
            .Invoke(null, new object[] { previousInstance, currentInstance, "maxHealth" });

        RepairStationTrap previousRepair = source.AddComponent<RepairStationTrap>();
        RepairStationTrap currentRepair = destination.AddComponent<RepairStationTrap>();
        SetFloat(previousRepair, "repairAmount", 31.25f);
        SetFloat(previousRepair, "cooldownSeconds", 6.5f);
        SetFloat(previousRepair, "deploySeconds", 0.6f);
        SetFloat(previousRepair, "holdSeconds", 0.8f);
        SetFloat(previousRepair, "retractSeconds", 0.7f);
        Method(typeof(RepairStationAuthoring), "CopyManualRepairTuning", typeof(RepairStationTrap), typeof(RepairStationTrap))
            .Invoke(null, new object[] { previousRepair, currentRepair });

        Assert.That(GetFloat(currentInstance, "maxHealth"), Is.EqualTo(187f));
        Assert.That(GetFloat(currentRepair, "repairAmount"), Is.EqualTo(31.25f));
        Assert.That(GetFloat(currentRepair, "cooldownSeconds"), Is.EqualTo(6.5f));
        Assert.That(GetFloat(currentRepair, "deploySeconds"), Is.EqualTo(0.6f));
        Assert.That(GetFloat(currentRepair, "holdSeconds"), Is.EqualTo(0.8f));
        Assert.That(GetFloat(currentRepair, "retractSeconds"), Is.EqualTo(0.7f));
    }

    [Test]
    public void ThroatLampRegenerationCopiesManualBaseHealth()
    {
        var source = new GameObject("lamp tuning source");
        var destination = new GameObject("lamp tuning destination");
        objects.Add(source);
        objects.Add(destination);
        TrapInstance previous = source.AddComponent<TrapInstance>();
        TrapInstance current = destination.AddComponent<TrapInstance>();
        SetFloat(previous, "maxHealth", 215f);

        Method(typeof(ThroatLampAuthoring), "CopyManualFloat", typeof(Object), typeof(Object), typeof(string))
            .Invoke(null, new object[] { previous, current, "maxHealth" });

        Assert.That(GetFloat(current, "maxHealth"), Is.EqualTo(215f));
    }

    [Test]
    public void UpgradeRuntimeRegistersAutomaticSceneInitialization()
    {
        MethodInfo initializer = typeof(TrapUpgradeRuntime).GetMethod("InitializeAfterSceneLoad",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.That(initializer, Is.Not.Null);
        var attribute = (RuntimeInitializeOnLoadMethodAttribute)Attribute.GetCustomAttribute(
            initializer, typeof(RuntimeInitializeOnLoadMethodAttribute));
        Assert.That(attribute, Is.Not.Null);
        Assert.That(attribute.loadType, Is.EqualTo(RuntimeInitializeLoadType.AfterSceneLoad));
    }

    private TrapDefinition NewDefinition(string id, int cost)
    {
        TrapDefinition definition = ScriptableObject.CreateInstance<TrapDefinition>();
        objects.Add(definition);
        var settings = new SerializedObject(definition);
        settings.FindProperty("trapId").stringValue = id;
        settings.FindProperty("cost").intValue = cost;
        settings.ApplyModifiedPropertiesWithoutUndo();
        return definition;
    }

    private static void SetFloat(Object target, string property, float value)
    {
        var settings = new SerializedObject(target);
        settings.FindProperty(property).floatValue = value;
        settings.ApplyModifiedPropertiesWithoutUndo();
    }

    private static float GetFloat(Object target, string property) =>
        new SerializedObject(target).FindProperty(property).floatValue;

    private static MethodInfo Method(string name, params Type[] arguments) =>
        Method(typeof(Betty7TrapAuthoring), name, arguments);

    private static MethodInfo Method(Type owner, string name, params Type[] arguments)
    {
        MethodInfo method = owner.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic, null, arguments, null);
        Assert.That(method, Is.Not.Null, owner.Name + "." + name);
        return method;
    }
}
