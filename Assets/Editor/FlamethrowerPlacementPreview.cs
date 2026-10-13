using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Disposable installation preview; never saves or edits a gameplay scene.</summary>
public static class FlamethrowerPlacementPreview
{
    public static void Render()
    {
        Render(false);
    }

    public static void RenderGraybox()
    {
        Render(true);
    }

    public static void RenderAttackVolume()
    {
        Render(true, true);
    }

    private static void Render(bool graybox, bool attackVolume = false)
    {
        if (!Application.isBatchMode || Application.isPlaying)
            throw new InvalidOperationException("Run in a batch editor with graphics enabled.");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        TrapDefinition definition = AssetDatabase.LoadAssetAtPath<TrapDefinition>(FlamethrowerTrapAuthoring.DefinitionPath);
        if (definition == null || definition.Prefab == null)
            throw new InvalidOperationException("Generate the flamethrower definition first.");
        string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            "陷阱素材文件夹", "Flamethrower");
        Directory.CreateDirectory(directory);
        var root = new GameObject("Disposable placement preview");
        Material wallMaterial = Unlit(new Color(0.12f, 0.17f, 0.23f));
        Material gridMaterial = Unlit(new Color(0.28f, 0.4f, 0.5f));
        Material footprintMaterial = Unlit(new Color(0.1f, 0.95f, 0.75f));
        Material directionMaterial = Unlit(new Color(1f, 0.8f, 0.12f));
        Material rangeMaterial = Unlit(new Color(0.2f, 0.6f, 1f));
        Material acceptedMaterial = attackVolume ? Unlit(new Color(0.2f, 1f, 0.25f)) : null;
        Material rejectedMaterial = attackVolume ? Unlit(new Color(1f, 0.15f, 0.12f)) : null;
        var target = new RenderTexture(1200, 900, 24);
        var pixels = new Texture2D(1200, 900, TextureFormat.RGB24, false);
        RenderTexture previous = RenderTexture.active;
        bool previousAsyncCompilation = ShaderUtil.allowAsyncCompilation;
        try
        {
            // A first batch-render frame can otherwise capture temporary gray
            // shader placeholders instead of the nozzle and arrow materials.
            ShaderUtil.allowAsyncCompilation = false;
            Transform wall = new GameObject("Rotatable wall").transform;
            wall.SetParent(root.transform, false);
            Transform slab = Box(wall, "Wall", new Vector3(2f, 2f, -0.08f), new Vector3(4f, 4f, 0.16f), wallMaterial);
            BoxCollider support = slab.gameObject.AddComponent<BoxCollider>();
            var open = new bool[16];
            for (int i = 0; i < open.Length; i++) open[i] = true;
            TrapPlacementGrid grid = wall.gameObject.AddComponent<TrapPlacementGrid>();
            grid.ConfigureLayout(4, 4, 1f, 0f, open, TrapPlacementGrid.SurfaceKind.Ground, TrapMountType.Wall, support, 0.01f);
            if (!grid.TryPlaceTrap(new Vector2Int(1, 1), definition, out TrapInstance placed, out string failure))
                throw new InvalidOperationException(failure);
            for (int i = 0; i <= 4; i++)
            {
                Box(wall, "1m grid X", new Vector3(i, 2f, 0.016f), new Vector3(0.012f, 4f, 0.012f), gridMaterial);
                Box(wall, "1m grid Y", new Vector3(2f, i, 0.016f), new Vector3(4f, 0.012f, 0.012f), gridMaterial);
            }
            foreach (float edge in new[] { 1f, 3f })
            {
                Box(wall, "2x2 reserved X", new Vector3(edge, 2f, 0.025f), new Vector3(0.035f, 2f, 0.02f), footprintMaterial);
                Box(wall, "2x2 reserved Y", new Vector3(2f, edge, 0.025f), new Vector3(2f, 0.035f, 0.02f), footprintMaterial);
            }
            if (graybox)
            {
                // Disposable wire frame only: the prefab contains no spray geometry.
                var runtime = placed.GetComponent<FlamethrowerTrap>();
                Vector3 size = runtime.AttackVolumeSize;
                float halfWidth = size.x * 0.5f;
                float halfHeight = size.y * 0.5f;
                float range = size.z;
                Vector3 origin = wall.InverseTransformPoint(runtime.AttackVolumeMatrix.MultiplyPoint3x4(Vector3.zero));
                foreach (float x in new[] { -halfWidth, halfWidth })
                foreach (float y in new[] { -halfHeight, halfHeight })
                    Box(wall, "Attack +Z edge", origin + new Vector3(x, y, range * 0.5f),
                        new Vector3(0.012f, 0.012f, range), rangeMaterial);
                foreach (float z in new[] { 0f, range })
                {
                    foreach (float x in new[] { -halfWidth, halfWidth })
                        Box(wall, "Attack vertical edge", origin + new Vector3(x, 0f, z),
                            new Vector3(0.012f, halfHeight * 2f, 0.012f), rangeMaterial);
                    foreach (float y in new[] { -halfHeight, halfHeight })
                        Box(wall, "Attack horizontal edge", origin + new Vector3(0f, y, z),
                            new Vector3(halfWidth * 2f, 0.012f, 0.012f), rangeMaterial);
                }
                if (attackVolume)
                {
                    foreach (Vector3 point in new[]
                    {
                        new Vector3(0f, 0f, 1f), new Vector3(0.6f, 0.4f, 3.2f),
                        new Vector3(1.35f, 0f, 2.1f), new Vector3(0f, 1.35f, 2.5f),
                        new Vector3(0f, 0f, 4.35f), new Vector3(0f, 0f, -0.35f)
                    })
                    {
                        Transform probe = Box(wall, "Target probe " + point, origin + point, Vector3.one * 0.18f, acceptedMaterial);
                        EnemyHealth health = probe.gameObject.AddComponent<EnemyHealth>();
                        health.ResetHealth();
                        bool accepted = runtime.IsInAttackVolume(health);
                        probe.GetComponent<Renderer>().sharedMaterial = accepted ? acceptedMaterial : rejectedMaterial;
                        Debug.Log($"FLAMETHROWER_STEP5_PREVIEW_TARGET: local {point}, accepted {accepted}");
                    }
                }
            }
            else
            {
                Box(wall, "Outward +Z probe", new Vector3(2f, 2f, 0.95f), new Vector3(0.04f, 0.04f, 1f), directionMaterial);
                Box(wall, "Outward tip", new Vector3(2f, 2f, 1.45f), Vector3.one * 0.12f, directionMaterial);
            }
            Camera camera = new GameObject("Preview camera").AddComponent<Camera>();
            camera.transform.SetParent(root.transform, false);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.035f, 0.045f, 0.065f);
            camera.orthographic = true;
            camera.orthographicSize = 2.85f;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 100f;
            camera.targetTexture = target;
            Light light = new GameObject("Preview light").AddComponent<Light>();
            light.transform.SetParent(root.transform, false);
            light.type = LightType.Directional;
            light.intensity = 2f;
            light.shadows = LightShadows.None;
            for (int i = 0; i < (graybox && !attackVolume ? 3 : 2); i++)
            {
                int yaw = i == 1 ? 90 : 0;
                wall.rotation = Quaternion.Euler(0f, yaw, 0f);
                Vector3 viewOffset = i == 0 ? new Vector3(3.4f, 1.8f, 7f) : new Vector3(-4.5f, 2.2f, 7f);
                Vector3 focus = new Vector3(2f, 2f, 0.2f);
                if (graybox)
                {
                    bool closeup = i == 2;
                    viewOffset = closeup ? new Vector3(2f, 1f, 4f) : new Vector3(i == 0 ? 6f : -6f, 2.8f, 8f);
                    camera.orthographicSize = closeup ? 1.35f : (attackVolume ? 3.6f : 3.3f);
                    focus.z = closeup ? 0.2f : 1.5f;
                }
                camera.transform.position = wall.TransformPoint(new Vector3(2f, 2f, 0f) + viewOffset);
                camera.transform.LookAt(wall.TransformPoint(focus), wall.up);
                light.transform.rotation = camera.transform.rotation;
                // Prime SRP material buffers before capturing the first batch frame.
                camera.Render();
                camera.Render();
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0f, 0f, target.width, target.height), 0, 0);
                pixels.Apply();
                string filename = attackVolume ? "step5-volume-yaw" + yaw + ".png"
                    : graybox ? (i == 2 ? "step4-graybox-closeup.png" : "step4-wall-yaw" + yaw + ".png")
                    : "step3-wall-yaw" + yaw + ".png";
                string path = Path.Combine(directory, filename);
                File.WriteAllBytes(path, pixels.EncodeToPNG());
                Debug.Log((attackVolume ? "FLAMETHROWER_STEP5_PREVIEW: "
                    : graybox ? "FLAMETHROWER_STEP4_PREVIEW: " : "FLAMETHROWER_STEP3_PREVIEW: ") + path);
            }
        }
        finally
        {
            ShaderUtil.allowAsyncCompilation = previousAsyncCompilation;
            RenderTexture.active = previous;
            Object.DestroyImmediate(root);
            target.Release();
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(pixels);
            Object.DestroyImmediate(wallMaterial);
            Object.DestroyImmediate(gridMaterial);
            Object.DestroyImmediate(footprintMaterial);
            Object.DestroyImmediate(directionMaterial);
            Object.DestroyImmediate(rangeMaterial);
            if (acceptedMaterial != null) Object.DestroyImmediate(acceptedMaterial);
            if (rejectedMaterial != null) Object.DestroyImmediate(rejectedMaterial);
        }
    }

    private static Material Unlit(Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
        return new Material(shader) { color = color };
    }

    private static Transform Box(Transform parent, string name, Vector3 position, Vector3 size, Material material)
    {
        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = name;
        box.transform.SetParent(parent, false);
        box.transform.localPosition = position;
        box.transform.localScale = size;
        box.GetComponent<Renderer>().sharedMaterial = material;
        Object.DestroyImmediate(box.GetComponent<Collider>());
        return box.transform;
    }
}
