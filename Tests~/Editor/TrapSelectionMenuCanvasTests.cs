using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Structural regression checks for the editable trap-selection Canvas assets.</summary>
public sealed class TrapSelectionMenuCanvasTests
{
    private const string MenuPath = "Assets/Resources/UI/TrapSelectionMenu.prefab";
    private static readonly string[] OfficialIds =
    {
        "auto_sentry_turret", "ground-spike", "dual_missile_launcher_loaded", "flamethrower",
        "betty7", "throat-lamp", "repair-station"
    };

    [Test]
    public void CanvasPrefabHasEditableGridSlotsFiltersAndCardPrefab()
    {
        GameObject menu = AssetDatabase.LoadAssetAtPath<GameObject>(MenuPath);
        GameObject card = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/UI/TrapSelectionMenuCard.prefab");
        Assert.That(menu, Is.Not.Null);
        Assert.That(menu.GetComponent<Canvas>(), Is.Not.Null);
        Assert.That(menu.GetComponent<CanvasScaler>(), Is.Not.Null);
        Assert.That(menu.GetComponent<TrapSelectionMenuView>(), Is.Not.Null);
        Assert.That(menu.GetComponentsInChildren<TrapSelectionMenuCardView>(true), Has.Length.EqualTo(54));
        Assert.That(menu.GetComponentsInChildren<GridLayoutGroup>(true).Single().constraintCount, Is.EqualTo(9));
        Assert.That(card, Is.Not.Null);
        Assert.That(card.GetComponent<TrapSelectionMenuCardView>(), Is.Not.Null);
    }

    [Test]
    public void OfficialDefinitionsHaveSharedIconsAndAllMountFilters()
    {
        TrapDefinition[] definitions = Resources.LoadAll<TrapDefinition>("")
            .Where(d => OfficialIds.Contains(d.TrapId)).ToArray();
        Assert.That(definitions.Select(d => d.TrapId).Distinct().Count(), Is.EqualTo(7));
        foreach (string id in OfficialIds)
        {
            TrapDefinition[] matches = definitions.Where(d => d.TrapId == id).ToArray();
            Assert.That(matches, Is.Not.Empty, id);
            Assert.That(matches[0].MenuIcon, Is.Not.Null, id);
            Assert.That(matches.All(d => d.MenuIcon == matches[0].MenuIcon), Is.True, id);
        }
        Assert.That(definitions.Count(d => TrapSelectionMenu.MatchesMountFilter(d, 1)), Is.GreaterThan(0));
        Assert.That(definitions.Count(d => TrapSelectionMenu.MatchesMountFilter(d, 2)), Is.GreaterThan(0));
        Assert.That(definitions.Count(d => TrapSelectionMenu.MatchesMountFilter(d, 3)), Is.GreaterThan(0));
        Assert.That(definitions.All(d => TrapSelectionMenu.MatchesMountFilter(d, 0)), Is.True);
    }
}
