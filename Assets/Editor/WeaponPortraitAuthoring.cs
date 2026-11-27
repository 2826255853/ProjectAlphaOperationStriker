using System;
using System.IO;
using System.Linq;
using KINEMATION.FPSAnimationPack.Scripts.Weapon;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class WeaponPortraitAuthoring
{
    private const string Folder = "Assets/Resources/" + GameStartMenuView.WeaponPortraitFolder;

    [MenuItem("Tools/行动打击/生成武器侧视图")]
    public static void Generate()
    {
        Directory.CreateDirectory(Folder);
        int count = 0;
        foreach (string path in AssetDatabase.GetDependencies("Assets/Resources/FPSPlayer.prefab"))
        {
            if (!path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)) continue;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var weapon = prefab.GetComponent<FPSWeapon>();
            if (weapon == null || PlayerWeaponCategories.Resolve(weapon) == PlayerWeaponCategory.Launcher) continue;
            string output = Folder + weapon.name + ".png";
            Render(prefab, output);
            AssetDatabase.ImportAsset(output);
            var importer = (TextureImporter)AssetImporter.GetAtPath(output);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.maxTextureSize = 512;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            count++;
        }
        if (count == 0) throw new InvalidOperationException("No selectable weapons found on the player prefab.");
        Debug.Log($"Rendered {count} selectable weapon portraits.");
    }

    private static void Render(GameObject prefab, string output)
    {
        var scene = EditorSceneManager.NewPreviewScene();
        var previous = RenderTexture.active;
        var target = RenderTexture.GetTemporary(1024, 512, 24, RenderTextureFormat.ARGB32);
        var pixels = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
        try
        {
            var model = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            model.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var meshes = model.GetComponentsInChildren<Renderer>().Where(r => r.enabled &&
                (r is MeshRenderer || r is SkinnedMeshRenderer)).ToArray();
            if (meshes.Length == 0) throw new InvalidOperationException("Empty weapon model: " + prefab.name);
            Bounds bounds = meshes[0].bounds;
            foreach (var mesh in meshes) bounds.Encapsulate(mesh.bounds);
            var cameraObject = new GameObject("Side View", typeof(Camera));
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            var camera = cameraObject.GetComponent<Camera>();
            camera.scene = scene;
            camera.targetTexture = target;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.green;
            camera.orthographic = true;
            camera.aspect = (float)target.width / target.height;
            camera.orthographicSize = Mathf.Max(bounds.extents.y, bounds.extents.z / camera.aspect) * 1.1f;
            camera.transform.position = bounds.center + Vector3.right * (bounds.extents.x + 2);
            camera.transform.rotation = Quaternion.LookRotation(Vector3.left, Vector3.up);
            AddLight(scene, new Vector3(-1, -.65f, .4f), 1.6f);
            AddLight(scene, new Vector3(-1, .4f, -.6f), .75f);
            camera.Render();
            RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            var colors = pixels.GetPixels32();
            int visible = 0;
            for (int i = 0; i < colors.Length; i++)
            {
                Color32 c = colors[i];
                if (c.g > c.r + 36 && c.g > c.b + 36) colors[i] = default;
                else { colors[i].a = 255; visible++; }
            }
            if (visible < 100 || visible == colors.Length)
                throw new InvalidOperationException("Invalid portrait coverage: " + prefab.name);
            pixels.SetPixels32(colors);
            pixels.Apply();
            var frame = FindWeaponFrame(colors, target.width, target.height);
            var cropped = new Texture2D(frame.width, frame.height, TextureFormat.RGBA32, false);
            try
            {
                cropped.SetPixels(pixels.GetPixels(frame.x, frame.y, frame.width, frame.height));
                cropped.Apply();
                File.WriteAllBytes(output, cropped.EncodeToPNG());
            }
            finally { Object.DestroyImmediate(cropped); }
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
            Object.DestroyImmediate(pixels);
        }
    }

    // Reload props can sit above the weapon in the prefab's rest pose. Frame the
    // largest continuous band of occupied rows so a separate cartridge stays out.
    private static RectInt FindWeaponFrame(Color32[] colors, int width, int height)
    {
        RectInt frame = default;
        int bestArea = 0, area = 0, startY = 0, minX = width, maxX = -1;
        for (int y = 0; y <= height; y++)
        {
            int rowArea = 0;
            for (int x = 0; y < height && x < width; x++)
            {
                if (colors[y * width + x].a == 0) continue;
                rowArea++;
                minX = Math.Min(minX, x);
                maxX = Math.Max(maxX, x);
            }
            if (rowArea > 0)
            {
                if (area == 0) startY = y;
                area += rowArea;
                continue;
            }
            if (area > bestArea)
            {
                frame = new RectInt(minX, startY, maxX - minX + 1, y - startY);
                bestArea = area;
            }
            area = 0;
            minX = width;
            maxX = -1;
        }
        return frame;
    }

    private static void AddLight(Scene scene, Vector3 direction, float intensity)
    {
        var lightObject = new GameObject("Studio Light", typeof(Light));
        SceneManager.MoveGameObjectToScene(lightObject, scene);
        var light = lightObject.GetComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = intensity;
        light.transform.rotation = Quaternion.LookRotation(direction);
    }
}
