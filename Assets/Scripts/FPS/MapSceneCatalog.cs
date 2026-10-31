using System.Collections.Generic;
using UnityEngine.SceneManagement;

/// <summary>Discovers playable maps from the enabled Unity Build Settings scenes.</summary>
public static class MapSceneCatalog
{
    public readonly struct Entry
    {
        public Entry(int buildIndex, string path)
        {
            BuildIndex = buildIndex;
            Path = path;
            DisplayName = System.IO.Path.GetFileNameWithoutExtension(path ?? string.Empty);
        }

        public int BuildIndex { get; }
        public string Path { get; }
        public string DisplayName { get; }
    }

    public static List<Entry> GetMaps()
    {
        var maps = new List<Entry>();
        for (int buildIndex = 0; buildIndex < SceneManager.sceneCountInBuildSettings; buildIndex++)
        {
            string path = SceneUtility.GetScenePathByBuildIndex(buildIndex);
            if (!string.IsNullOrEmpty(path)) maps.Add(new Entry(buildIndex, path));
        }

        return maps;
    }

    public static int GetCurrentBuildIndex() => SceneManager.GetActiveScene().buildIndex;

    public static bool TryGet(int buildIndex, out Entry entry)
    {
        if (buildIndex >= 0 && buildIndex < SceneManager.sceneCountInBuildSettings)
        {
            string path = SceneUtility.GetScenePathByBuildIndex(buildIndex);
            if (!string.IsNullOrEmpty(path))
            {
                entry = new Entry(buildIndex, path);
                return true;
            }
        }

        entry = default;
        return false;
    }
}
