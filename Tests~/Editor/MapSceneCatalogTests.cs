#if UNITY_INCLUDE_TESTS
using NUnit.Framework;
using UnityEngine.SceneManagement;

public sealed class MapSceneCatalogTests
{
    [Test]
    public void BuildSettingsScenesAreReturnedWithFileNameDisplayNames()
    {
        var maps = MapSceneCatalog.GetMaps();
        Assert.That(maps, Is.Not.Null);
        Assert.That(maps.Count, Is.EqualTo(SceneManager.sceneCountInBuildSettings));
        Assert.That(maps, Is.Not.Empty);
        for (int i = 0; i < maps.Count; i++)
        {
            Assert.That(maps[i].BuildIndex, Is.EqualTo(i));
            Assert.That(maps[i].Path, Does.EndWith(".unity"));
            Assert.That(maps[i].DisplayName, Is.Not.Empty);
        }
    }

    [Test]
    public void CurrentBuildIndexCanBeResolved()
    {
        int current = MapSceneCatalog.GetCurrentBuildIndex();
        Assert.That(current, Is.EqualTo(SceneManager.GetActiveScene().buildIndex));
        Assert.That(MapSceneCatalog.TryGet(current, out MapSceneCatalog.Entry entry), Is.True);
        Assert.That(entry.BuildIndex, Is.EqualTo(current));
    }
}
#endif
