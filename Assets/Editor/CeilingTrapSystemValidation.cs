using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class CeilingTrapSystemValidation
{
    public static string OutputDirectory => Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.DesktopDirectory),
        "陷阱素材文件夹", "CeilingTrapSystem");

    [MenuItem("Tools/塔防/生成并预览天花板陷阱系统")]
    public static void GenerateAndPreview()
    {
        CeilingTrapTestPieceAuthoring.CreateAssets();
        Scene scene = EditorSceneManager.NewPreviewScene();
        try
        {
            var support = GameObject.CreatePrimitive(PrimitiveType.Cube); SceneManager.MoveGameObjectToScene(support, scene);
            support.isStatic = true; support.transform.position = new Vector3(0, 4.1f, 0); support.transform.localScale = new Vector3(4, .2f, 4);
            var gridHost = new GameObject("Ceiling grid preview"); SceneManager.MoveGameObjectToScene(gridHost, scene);
            gridHost.transform.SetParent(support.transform, false);
            var grid = gridHost.AddComponent<TrapPlacementGrid>();
            CeilingTrapGridAuthoring.Configure(grid, support.GetComponent<BoxCollider>(), new MapForgeSceneOrganization.CeilingTrapGridMap { id = "preview" });
            var definition = AssetDatabase.LoadAssetAtPath<TrapDefinition>(CeilingTrapTestPieceAuthoring.DefinitionPath);
            foreach (var cell in new[] { Vector2Int.zero, new Vector2Int(2, 2) })
            {
                grid.GetPlacementPose(cell, definition, out var p, out var q);
                var trap = Object.Instantiate(definition.Prefab, p, q); SceneManager.MoveGameObjectToScene(trap, scene);
            }
            var material = new Material(Shader.Find("Universal Render Pipeline/Unlit")); material.color = new Color(.2f, 1f, .6f);
            for (int i = 0; i <= 4; i++)
            {
                AddLine(scene, material, new Vector3(-2 + i, 3.98f, -2), new Vector3(-2 + i, 3.98f, 2));
                AddLine(scene, material, new Vector3(-2, 3.98f, -2 + i), new Vector3(2, 3.98f, -2 + i));
            }
            var lightHost = new GameObject("Preview light"); SceneManager.MoveGameObjectToScene(lightHost, scene);
            var light = lightHost.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 2f;
            lightHost.transform.rotation = Quaternion.Euler(-65, 30, 0);
            var cameraHost = new GameObject("Preview camera"); SceneManager.MoveGameObjectToScene(cameraHost, scene);
            var camera = cameraHost.AddComponent<Camera>(); camera.backgroundColor = new Color(.06f, .08f, .12f); camera.clearFlags = CameraClearFlags.SolidColor;
            camera.scene = scene;
            camera.transform.position = new Vector3(5, .8f, -6); camera.transform.LookAt(new Vector3(0, 3.9f, 0));
            Capture(camera, "ceiling-editor-preview.png");
            Object.DestroyImmediate(material);
            Debug.Log("CEILING_SYSTEM_PREVIEW_PASS: " + OutputDirectory);
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

    private static void AddLine(Scene scene, Material material, Vector3 start, Vector3 end)
    {
        var host = new GameObject("Grid line"); SceneManager.MoveGameObjectToScene(host, scene);
        var line = host.AddComponent<LineRenderer>(); line.sharedMaterial = material; line.startWidth = line.endWidth = .018f;
        line.positionCount = 2; line.SetPosition(0, start); line.SetPosition(1, end);
    }

    public static void Capture(Camera camera, string filename)
    {
        System.IO.Directory.CreateDirectory(OutputDirectory);
        var previous = RenderTexture.active; var oldTarget = camera.targetTexture;
        var target = new RenderTexture(1280, 900, 24); var pixels = new Texture2D(1280, 900, TextureFormat.RGB24, false);
        try
        {
            camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0, 0, 1280, 900), 0, 0); pixels.Apply();
            Color32 background = pixels.GetPixel(0, 0);
            int foregroundPixels = 0;
            foreach (Color32 pixel in pixels.GetPixels32())
                if (Mathf.Abs(pixel.r - background.r) + Mathf.Abs(pixel.g - background.g)
                    + Mathf.Abs(pixel.b - background.b) > 24) foregroundPixels++;
            if (foregroundPixels < 100)
                throw new System.InvalidOperationException("天花板预览画面为空，未检测到场景几何。");
            File.WriteAllBytes(Path.Combine(OutputDirectory, filename), pixels.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = oldTarget; RenderTexture.active = previous;
            Object.DestroyImmediate(pixels); Object.DestroyImmediate(target);
        }
    }
}
