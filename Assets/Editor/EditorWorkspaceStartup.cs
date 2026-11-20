using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
internal static class EditorWorkspaceStartup
{
    internal const string WorkspaceScenePath = "Assets/Scenes/EditorWorkspace.unity";
    private const string FacilityScenePath = "Assets/Scenes/DOUBLE_LAYER_FACILITY.unity";

    static EditorWorkspaceStartup()
    {
        if (!Application.isBatchMode)
            EditorApplication.wantsToQuit += CloseFacilityBeforeSceneRestoreIsSaved;
    }

    private static bool CloseFacilityBeforeSceneRestoreIsSaved()
    {
        var facility = SceneManager.GetSceneByPath(FacilityScenePath);
        if (!facility.IsValid() || !facility.isLoaded)
            return true;

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return false;

        // Unity restores the scene setup on the next launch, before editor scripts can run.
        if (SceneManager.loadedSceneCount == 1)
        {
            EditorSceneManager.OpenScene(WorkspaceScenePath, OpenSceneMode.Single);
        }
        else
        {
            EditorSceneManager.CloseScene(facility, removeScene: true);
        }

        return true;
    }
}
