using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Authoring helper for the dual missile launcher trap.
///
/// The imported FBX is visual-only, so this builds the rotatable rig
/// (Yaw Pivot -&gt; Pitch Pivot -&gt; launcher meshes) once, hangs the authored
/// launch plumes off the pitch pivot, saves it as a prefab and points the trap
/// definition assets at it. Run it from
/// Tools/塔防/生成导弹发射器预制体 or in batch mode via
/// MissileLauncherTurretAuthoring.CreateLauncherPrefab.
/// </summary>
public static class MissileLauncherTurretAuthoring
{
    public const string LauncherModelPath = "Assets/Models/Trap_Base_Disc_Dual_Launcher_Loaded.fbx";
    public const string LauncherPrefabPath = "Assets/Prefabs/DualMissileLauncher.prefab";
    /// <summary>Twin launch plume authored in Blender (desktop 陷阱素材文件夹\DualMissileExhaust).</summary>
    public const string ExhaustModelPath = "Assets/Models/DualMissileExhaust/DualMissileExhaust.fbx";
    /// <summary>URP flame materials generated here; the FBX only ships built-in Standard ones.</summary>
    public const string ExhaustMaterialFolder = "Assets/Models/DualMissileExhaust/Materials";

    private const float LauncherFootprintMeters = 2f;
    /// <summary>Uniform scale applied to the authored plume inside the launcher prefab.</summary>
    private const float PlumeScale = 0.85f;
    /// <summary>How far behind the missile tail the nozzle mouth sits, in pitch pivot local units.</summary>
    private const float PlumeTailGap = 0.12f;
    private const string PlumeRootName = "Exhaust Plume";
    private static readonly string[] RailSuffixes = { "_01", "_02" };
    private static readonly string[] Barrels = { "Missile_01", "Missile_02" };

    /// <summary>
    /// Flame layers, matched against the plume mesh names. Colours mirror the Blender
    /// source (create_dual_missile_exhaust.py) so the Unity look matches the preview
    /// render; alphas are additive strengths because the layers nest.
    /// </summary>
    private static readonly ExhaustLayer[] ExhaustLayers =
    {
        new ExhaustLayer("Exhaust_Tip_Dim", "HotGasCore", new Color(0.35f, 0.05f, 0.01f, 1f)),
        new ExhaustLayer("Exhaust_Outer_Orange", "OuterFlame", new Color(0.90f, 0.10f, 0.01f, 1f)),
        new ExhaustLayer("Exhaust_Mid_Gold", "MidFlame", new Color(1.00f, 0.36f, 0.03f, 1f)),
        new ExhaustLayer("Exhaust_Inner_WhiteHot", "InnerFlame", new Color(1.00f, 0.76f, 0.34f, 1f))
    };

    private readonly struct ExhaustLayer
    {
        public readonly string MaterialName;
        public readonly string RendererToken;
        public readonly Color Color;

        public ExhaustLayer(string materialName, string rendererToken, Color color)
        {
            MaterialName = materialName;
            RendererToken = rendererToken;
            Color = color;
        }
    }

    private static readonly string[] TrapDefinitionPaths =
    {
        "Assets/Resources/DualMissileLauncherLoaded.asset",
        "Assets/TrapDefinitions/DualMissileLauncherLoaded.asset"
    };

    [MenuItem("Tools/塔防/生成导弹发射器预制体")]
    public static void CreateLauncherPrefab()
    {
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(LauncherModelPath);
        if (model == null)
        {
            Debug.LogError($"找不到导弹发射器模型：{LauncherModelPath}");
            return;
        }

        // A plain copy is used on purpose: reparenting meshes inside a nested
        // prefab instance is not persisted by SaveAsPrefabAsset, while a copy
        // produces a clean, baked Yaw Pivot / Pitch Pivot hierarchy.
        GameObject root = (GameObject)Object.Instantiate(model);
        root.name = "DualMissileLauncher";
        root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

        MissileLauncherTurret turret = root.AddComponent<MissileLauncherTurret>();
        bool built = turret.BuildRig();
        if (!built)
            Debug.LogWarning($"未能从模型自动识别机座，已保存静态预制体，运行时会在 {nameof(MissileLauncherTurret)} 里重试。");

        FitLauncherToFootprint(root, LauncherFootprintMeters);

        // The FBX carries no usable material, so the body would render colourless in URP.
        // Painted before the plumes are mounted so the flame layers are left alone.
        ApplyLauncherMaterials(root);

        // The firing half lives next to the mount: it owns the lead solution, the
        // salvo cadence and the in-flight bookkeeping (see the air-attack plan).
        MissileLauncherWeapon weapon = root.AddComponent<MissileLauncherWeapon>();
        WireWeapon(weapon, root, turret);

        // Plumes are mounted after the footprint fit so they never feed back into
        // the launcher's own scale calculation.
        MountExhaustPlumes(root, turret, weapon);

        EditorUtility.SetDirty(turret);
        EditorUtility.SetDirty(weapon);
        Directory.CreateDirectory(Path.GetDirectoryName(LauncherPrefabPath) ?? "Assets/Prefabs");
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, LauncherPrefabPath);
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();

        AssignPrefabToTrapDefinitions(prefab);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"已生成导弹发射器预制体：{LauncherPrefabPath}" +
            $"（旋转机座：{(built ? "已生成" : "运行时会重建")}）");
    }

    /// <summary>
    /// Hangs one authored plume stack off the pitch pivot per rail, so the exhaust
    /// yaws and pitches with the tubes. Everything is measured from the meshes:
    /// the nozzle sits just behind each missile's tail (barrel forward is the
    /// missile nose direction) and the plume axis is read back from the model, so
    /// a Blender axis change does not need this file to be edited.
    /// </summary>
    private static void MountExhaustPlumes(GameObject root, MissileLauncherTurret turret, MissileLauncherWeapon weapon)
    {
        GameObject exhaustModel = AssetDatabase.LoadAssetAtPath<GameObject>(ExhaustModelPath);
        if (exhaustModel == null)
        {
            Debug.LogWarning($"找不到尾焰模型：{ExhaustModelPath}，预制体将不带喷射效果。");
            return;
        }
        if (turret == null || !turret.HasRig || turret.PitchPivot == null)
        {
            Debug.LogWarning("机座未就绪，跳过尾焰挂载。");
            return;
        }

        Dictionary<string, Material> materials = EnsureExhaustMaterials();

        Transform pitchPivot = turret.PitchPivot;
        Vector3 barrelForward = turret.RestBarrelDirectionWorld;
        barrelForward = pitchPivot.InverseTransformDirection(barrelForward).normalized;
        if (barrelForward.sqrMagnitude < 0.5f) barrelForward = Vector3.right;
        // The barrel points at the nose, so the tail - and the plume - sit opposite.
        Vector3 tailDirection = -barrelForward;

        var plumes = new List<Transform>();
        for (int i = 0; i < Barrels.Length && i < RailSuffixes.Length; i++)
        {
            Transform barrel = FindDeepChild(root.transform, Barrels[i]);
            if (barrel == null)
            {
                Debug.LogWarning($"找不到 {Barrels[i]}，第 {i + 1} 根管子的尾焰未挂载。");
                continue;
            }

            Bounds barrelBounds;
            if (!TryGetBoundsInSpace(pitchPivot, barrel, out barrelBounds))
            {
                Debug.LogWarning($"{Barrels[i]} 没有可用的 Renderer，尾焰未挂载。");
                continue;
            }

            var plumeRoot = new GameObject($"{PlumeRootName} {i + 1:00}");
            plumeRoot.transform.SetParent(pitchPivot, false);

            GameObject instance = (GameObject)Object.Instantiate(exhaustModel, plumeRoot.transform, false);
            instance.name = $"{PlumeRootName} Meshes {i + 1:00}";
            StripOtherRail(instance, RailSuffixes[i]);
            ApplyExhaustMaterials(plumeRoot.transform, materials);

            float nozzleOffset;
            Quaternion orientation;
            if (!AlignPlumeToAxis(plumeRoot.transform, tailDirection, out nozzleOffset, out orientation))
            {
                Debug.LogWarning($"尾焰模型里找不到第 {i + 1} 根管子的火焰网格，跳过。");
                Object.DestroyImmediate(plumeRoot);
                continue;
            }

            plumeRoot.transform.localRotation = orientation;
            plumeRoot.transform.localScale = Vector3.one * PlumeScale;

            // Rear face of the rocket plus a small stand-off so the collar is not
            // buried inside the tube mesh.
            Vector3 rear = barrelBounds.center + tailDirection * ProjectedHalfLength(barrelBounds.extents, tailDirection);
            Vector3 nozzleLocal = orientation * (tailDirection * nozzleOffset);
            plumeRoot.transform.localPosition = rear + tailDirection * PlumeTailGap - nozzleLocal * PlumeScale;

            DisablePlumeShadows(plumeRoot);
            plumes.Add(plumeRoot.transform);
        }

        if (plumes.Count == 0)
        {
            Debug.LogWarning("尾焰一根都没挂上，预制体保持无喷射效果。");
            return;
        }

        MissileExhaustFx fx = root.GetComponent<MissileExhaustFx>();
        if (fx == null) fx = root.AddComponent<MissileExhaustFx>();
        fx.SetWeapon(weapon);
        fx.SetPlumes(plumes.ToArray());
        EditorUtility.SetDirty(fx);
        Debug.Log($"已挂载尾焰 {plumes.Count} 组：{string.Join(", ", plumes.ConvertAll(p => p.name).ToArray())}");
    }

    /// <summary>
    /// Creates the URP flame materials once and returns them keyed by FBX material
    /// name. The Blender export only carries built-in Standard materials, which URP
    /// renders magenta, so the plumes get additive unlit materials instead.
    /// </summary>
    public static Dictionary<string, Material> EnsureExhaustMaterials()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Unlit/Color");
        if (shader == null)
        {
            Debug.LogError("找不到 URP Unlit 着色器，尾焰会沿用 FBX 自带材质（URP 下会变成品红）。");
            return new Dictionary<string, Material>();
        }

        Directory.CreateDirectory(ExhaustMaterialFolder);
        var materials = new Dictionary<string, Material>();
        for (int i = 0; i < ExhaustLayers.Length; i++)
        {
            ExhaustLayer layer = ExhaustLayers[i];
            string path = $"{ExhaustMaterialFolder}/{layer.MaterialName}.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader) { name = layer.MaterialName };
                AssetDatabase.CreateAsset(material, path);
            }

            ConfigureFlameMaterial(material, shader, layer.Color);
            EditorUtility.SetDirty(material);
            materials[layer.MaterialName] = material;
        }
        AssetDatabase.SaveAssets();
        return materials;
    }

    /// <summary>Turns a URP Unlit material into an additive, depth-write-off flame layer.</summary>
    private static void ConfigureFlameMaterial(Material material, Shader shader, Color color)
    {
        if (material.shader != shader) material.shader = shader;
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color")) material.SetColor("_Color", color);
        if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f); // transparent
        if (material.HasProperty("_Blend")) material.SetFloat("_Blend", 2f);     // additive
        if (material.HasProperty("_SrcBlend")) material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        if (material.HasProperty("_DstBlend")) material.SetFloat("_DstBlend", (float)BlendMode.One);
        if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);
        if (material.HasProperty("_Cull")) material.SetFloat("_Cull", (float)CullMode.Off);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.DisableKeyword("_ALPHATEST_ON");
        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        material.DisableKeyword("_EMISSION");
        material.renderQueue = (int)RenderQueue.Transparent;
    }

    /// <summary>
    /// URP body materials generated for the launcher. The imported FBX ships built-in
    /// Standard materials (embedded sub-assets with no colour information), so URP drew
    /// the whole turret flat and colourless; these assets are what the prefab references.
    /// </summary>
    public const string LauncherMaterialFolder = "Assets/Models/DualMissileLauncher/Materials";

    /// <summary>
    /// Colour scheme for the launcher body. The FBX material names already describe the
    /// parts ("Trap Base Dark Metal", "Launcher Body Metal", "Launcher Rail",
    /// "Missile Body", "Missile Nose", "Missile Details", "Missile Fins", "Warning Edge"),
    /// but the embedded materials ship without any colour, which is why the turret used
    /// to render flat and colourless. Order matters: earlier styles win, so
    /// "Trap Base Dark Metal" resolves through "dark", "Missile_Nose" through "nose" and
    /// "Missile_Rail_1" through "rail" before the generic missile token.
    /// </summary>
    private static readonly PartStyle[] LauncherPartStyles =
    {
        new PartStyle("M_DML_WarningEdge", new[] { "warning", "hazard", "radial_lock" },
            new Color(0.92f, 0.48f, 0.07f), 0.15f, 0.45f),
        new PartStyle("M_DML_InnerGroove", new[] { "groove", "inner" },
            new Color(0.30f, 0.31f, 0.32f), 0.20f, 0.30f),
        new PartStyle("M_DML_DarkMetal", new[] { "dark", "base" },
            new Color(0.42f, 0.43f, 0.44f), 0.25f, 0.40f),
        new PartStyle("M_DML_PylonMetal", new[] { "pylon" },
            new Color(0.62f, 0.64f, 0.65f), 0.40f, 0.50f),
        new PartStyle("M_DML_Rail", new[] { "rail", "stop", "clamp", "bolt" },
            new Color(0.74f, 0.76f, 0.78f), 0.50f, 0.55f),
        new PartStyle("M_DML_LauncherBody", new[] { "launcher", "housing" },
            new Color(0.40f, 0.45f, 0.41f), 0.25f, 0.45f),
        new PartStyle("M_DML_MissileNose", new[] { "nose", "warhead", "cone", "tip" },
            new Color(0.74f, 0.22f, 0.12f), 0.10f, 0.38f),
        new PartStyle("M_DML_MissileDetails", new[] { "detail", "band", "stripe" },
            new Color(0.90f, 0.46f, 0.09f), 0.15f, 0.40f),
        new PartStyle("M_DML_MissileFins", new[] { "fin" },
            new Color(0.44f, 0.45f, 0.46f), 0.25f, 0.40f),
        new PartStyle("M_DML_MissileBody", new[] { "missile", "rocket" },
            new Color(0.93f, 0.92f, 0.88f), 0.05f, 0.40f),
        new PartStyle("M_DML_ArmorLight", new[] { "armor", "shell", "plate", "mount", "cover", "body" },
            new Color(0.78f, 0.78f, 0.74f), 0.20f, 0.45f)
    };

    private readonly struct PartStyle
    {
        public readonly string MaterialName;
        public readonly string[] Tokens;
        public readonly Color BaseColor;
        public readonly float Metallic;
        public readonly float Smoothness;
        public readonly Color Emission;
        public readonly float EmissionStrength;

        public PartStyle(string materialName, string[] tokens, Color baseColor, float metallic, float smoothness,
            Color emission = default, float emissionStrength = 0f)
        {
            MaterialName = materialName;
            Tokens = tokens;
            BaseColor = baseColor;
            Metallic = metallic;
            Smoothness = smoothness;
            Emission = emission;
            EmissionStrength = emissionStrength;
        }
    }

    /// <summary>Creates (or refreshes) one URP/Lit material per part style and returns them by name.</summary>
    public static Dictionary<string, Material> EnsureLauncherMaterials()
    {
        Shader lit = Shader.Find("Universal Render Pipeline/Lit");
        if (lit == null) lit = Shader.Find("Standard");
        if (lit == null)
        {
            Debug.LogError("找不到 URP/Lit 着色器，导弹发射架会继续沿用 FBX 自带材质（URP 下没有颜色）。");
            return new Dictionary<string, Material>();
        }

        Directory.CreateDirectory(LauncherMaterialFolder);
        var materials = new Dictionary<string, Material>();
        for (int i = 0; i < LauncherPartStyles.Length; i++)
        {
            PartStyle style = LauncherPartStyles[i];
            string path = $"{LauncherMaterialFolder}/{style.MaterialName}.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(lit) { name = style.MaterialName };
                AssetDatabase.CreateAsset(material, path);
            }

            ConfigurePartMaterial(material, lit, style);
            EditorUtility.SetDirty(material);
            materials[style.MaterialName] = material;
        }
        AssetDatabase.SaveAssets();
        return materials;
    }

    private static void ConfigurePartMaterial(Material material, Shader shader, PartStyle style)
    {
        if (material.shader != shader) material.shader = shader;
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", style.BaseColor);
        if (material.HasProperty("_Color")) material.SetColor("_Color", style.BaseColor);
        if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", style.Metallic);
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", style.Smoothness);
        if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", style.Smoothness);
        if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 0f); // opaque

        bool emissive = style.EmissionStrength > 0f;
        if (material.HasProperty("_EmissionColor"))
            material.SetColor("_EmissionColor", emissive ? style.Emission * style.EmissionStrength : Color.black);
        if (emissive) material.EnableKeyword("_EMISSION");
        else material.DisableKeyword("_EMISSION");
        material.renderQueue = -1;
    }

    /// <summary>
    /// Replaces every body renderer's FBX material with a generated URP style. Runs before
    /// the plumes are mounted so it never touches the flame layers.
    /// </summary>
    public static void ApplyLauncherMaterials(GameObject root)
    {
        Dictionary<string, Material> materials = EnsureLauncherMaterials();
        if (materials.Count == 0) return;

        var counts = new Dictionary<string, int>();
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        int touched = 0;
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null) continue;

            Material[] current = renderer.sharedMaterials;
            int slots = Mathf.Max(1, current.Length);
            var updated = new Material[slots];
            for (int s = 0; s < slots; s++)
            {
                Material source = s < current.Length ? current[s] : null;
                string style = ResolveLauncherStyle(renderer, source);
                updated[s] = materials[style];
                counts.TryGetValue(style, out int seen);
                counts[style] = seen + 1;
            }
            renderer.sharedMaterials = updated;
            touched++;
        }

        var summary = new System.Text.StringBuilder();
        foreach (KeyValuePair<string, int> pair in counts)
        {
            if (summary.Length > 0) summary.Append(", ");
            summary.Append(pair.Key).Append('x').Append(pair.Value);
        }
        Debug.Log($"导弹发射架材质已指派：{touched} 个渲染器（{summary}）。");
    }

    /// <summary>
    /// FBX material name first (the export carries readable part materials), then the
    /// renderer name, then the neutral armour style.
    /// </summary>
    private static string ResolveLauncherStyle(Renderer renderer, Material source)
    {
        string sourceName = source != null ? source.name : null;
        if (!IsGenericMaterialName(sourceName))
        {
            string fromMaterial = MatchStyleToken(sourceName);
            if (fromMaterial != null) return fromMaterial;
        }
        string fromRenderer = MatchStyleToken(renderer != null ? renderer.name : null);
        if (fromRenderer != null) return fromRenderer;
        return LauncherPartStyles[LauncherPartStyles.Length - 1].MaterialName;
    }

    private static string MatchStyleToken(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        string lower = name.ToLowerInvariant();
        for (int i = 0; i < LauncherPartStyles.Length; i++)
        {
            string[] tokens = LauncherPartStyles[i].Tokens;
            for (int t = 0; t < tokens.Length; t++)
                if (lower.Contains(tokens[t])) return LauncherPartStyles[i].MaterialName;
        }
        return null;
    }

    /// <summary>"Material", "Material.001" and the model name carry no colour information.</summary>
    private static bool IsGenericMaterialName(string name)
    {
        if (string.IsNullOrEmpty(name)) return true;
        if (name.StartsWith("Material", System.StringComparison.OrdinalIgnoreCase)) return true;
        return name.StartsWith("Trap_Base", System.StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Read-only probe: prints what each body renderer would be painted with.</summary>
    [MenuItem("Tools/塔防/探针：导弹发射架材质")]
    public static void ProbeLauncherMaterials()
    {
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(LauncherModelPath);
        if (model == null)
        {
            Debug.LogError($"找不到导弹发射器模型：{LauncherModelPath}");
            return;
        }

        GameObject instance = (GameObject)Object.Instantiate(model);
        Dictionary<string, Material> materials = EnsureLauncherMaterials();
        Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
        Debug.Log($"导弹发射架材质探针：{renderers.Length} 个渲染器，{materials.Count} 个 URP 材质");
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null) continue;
            Material[] current = renderer.sharedMaterials;
            var names = new List<string>();
            for (int s = 0; s < current.Length; s++) names.Add(current[s] != null ? current[s].name : "无");
            Debug.Log($"  {renderer.name} | {current.Length} 槽 [{string.Join(", ", names)}]" +
                $" -> {ResolveLauncherStyle(renderer, current.Length > 0 ? current[0] : null)}");
        }
        Object.DestroyImmediate(instance);
        Debug.Log("MISSILE_LAUNCHER_MATERIAL_PROBE_DONE");
    }

    /// <summary>Replaces each plume renderer's FBX material with the matching URP flame layer.</summary>
    private static void ApplyExhaustMaterials(Transform plumeRoot, Dictionary<string, Material> materials)
    {
        Renderer[] renderers = plumeRoot.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null) continue;

            Material replacement = MatchExhaustMaterial(renderer.name, materials);
            if (replacement == null) continue;
            renderer.sharedMaterial = replacement;
        }
    }

    /// <summary>
    /// Matches a plume mesh (<c>OuterFlame_01</c> ...) or its imported material name
    /// (<c>Exhaust_Outer_Orange</c>) onto a generated flame layer.
    /// </summary>
    private static Material MatchExhaustMaterial(string name, Dictionary<string, Material> materials)
    {
        if (string.IsNullOrEmpty(name) || materials == null) return null;

        Material direct;
        if (materials.TryGetValue(name, out direct)) return direct;

        for (int i = 0; i < ExhaustLayers.Length; i++)
        {
            ExhaustLayer layer = ExhaustLayers[i];
            if (name.Contains(layer.RendererToken)) return materials[layer.MaterialName];
        }
        return null;
    }

    /// <summary>Drops every flame mesh that belongs to the other rail (the FBX ships both).</summary>
    private static void StripOtherRail(GameObject instance, string keepSuffix)
    {
        var doomed = new List<GameObject>();
        Transform[] all = instance.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] == instance.transform) continue;
            if (HasRailSuffix(all[i].name, keepSuffix)) continue;
            if (all[i].GetComponent<Renderer>() == null) continue;
            doomed.Add(all[i].gameObject);
        }
        for (int i = 0; i < doomed.Count; i++) Object.DestroyImmediate(doomed[i]);
    }

    private static bool HasRailSuffix(string name, string suffix)
    {
        if (name.EndsWith(suffix, System.StringComparison.Ordinal)) return true;
        // Unity appends a duplicate index when the FBX holds repeated names.
        if (name.Contains(suffix + " ")) return true;
        return false;
    }

    /// <summary>
    /// Reads the plume axis out of the model (hot gas collar -&gt; outer flame) and
    /// returns the rotation that points it along <paramref name="axis"/> in the
    /// parent space, plus the nozzle distance from the plume root origin.
    /// </summary>
    private static bool AlignPlumeToAxis(Transform plumeRoot, Vector3 axis, out float nozzleOffset, out Quaternion orientation)
    {
        nozzleOffset = 0f;
        orientation = Quaternion.identity;

        Renderer collar = FindRendererByName(plumeRoot, "HotGasCore");
        Renderer flame = FindRendererByName(plumeRoot, "OuterFlame");
        if (collar == null || flame == null) return false;

        Vector3 collarCenter = plumeRoot.InverseTransformPoint(collar.bounds.center);
        Vector3 flameCenter = plumeRoot.InverseTransformPoint(flame.bounds.center);
        Vector3 plumeAxis = flameCenter - collarCenter;
        if (plumeAxis.sqrMagnitude < 0.000001f) return false;

        orientation = Quaternion.FromToRotation(plumeAxis.normalized, axis.normalized);
        nozzleOffset = Vector3.Dot(collarCenter, plumeAxis.normalized);
        return true;
    }

    private static Renderer FindRendererByName(Transform root, string token)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        var alive = new List<Renderer>();
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] == null) continue;
            if (renderers[i].name.StartsWith(token, System.StringComparison.Ordinal)) return renderers[i];
            alive.Add(renderers[i]);
        }
        // Fall back to anything containing the token, mirroring the fuzzy matching
        // used elsewhere in the launcher code.
        for (int i = 0; i < alive.Count; i++)
            if (alive[i].name.Contains(token)) return alive[i];
        return null;
    }

    private static void DisablePlumeShadows(GameObject root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] == null) continue;
            renderers[i].shadowCastingMode = ShadowCastingMode.Off;
            renderers[i].receiveShadows = false;
            renderers[i].lightProbeUsage = LightProbeUsage.Off;
        }
    }

    private static float ProjectedHalfLength(Vector3 extents, Vector3 direction)
    {
        return Mathf.Abs(extents.x * direction.x) + Mathf.Abs(extents.y * direction.y) + Mathf.Abs(extents.z * direction.z);
    }

    private static bool TryGetBoundsInSpace(Transform space, Transform part, out Bounds bounds)
    {
        bounds = new Bounds(Vector3.zero, Vector3.zero);
        Renderer[] renderers = part.GetComponentsInChildren<Renderer>(true);
        bool any = false;
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] == null) continue;
            Vector3 center = space.InverseTransformPoint(renderers[i].bounds.center);
            Vector3 extents = space.InverseTransformVector(renderers[i].bounds.extents);
            extents = new Vector3(Mathf.Abs(extents.x), Mathf.Abs(extents.y), Mathf.Abs(extents.z));
            var local = new Bounds(center, extents * 2f);
            if (!any) { bounds = local; any = true; }
            else bounds.Encapsulate(local);
        }
        return any;
    }

    /// <summary>
    /// Points the weapon at its mount and its two tubes. The imported model names
    /// its missiles Missile_01 / Missile_02, so the launcher can fire one round per
    /// rail without any hand wiring; both fields stay editable when a model changes.
    /// </summary>
    private static void WireWeapon(MissileLauncherWeapon weapon, GameObject root, MissileLauncherTurret turret)
    {
        if (weapon == null) return;

        var serialized = new SerializedObject(weapon);
        SetObjectReference(serialized, "turret", turret);

        Transform muzzleA = FindDeepChild(root.transform, "Missile_01");
        Transform muzzleB = FindDeepChild(root.transform, "Missile_02");
        if (muzzleA == null) muzzleA = FindDeepChild(root.transform, "Missile_01_Body");
        if (muzzleB == null) muzzleB = FindDeepChild(root.transform, "Missile_02_Body");
        SetObjectReference(serialized, "muzzleA", muzzleA);
        SetObjectReference(serialized, "muzzleB", muzzleB);

        // Reload art: each rail shows its own round, which disappears when that
        // rail fires and comes back when the reload finishes. Wired here so the
        // prefab keeps the component (the weapon also adds it at runtime when an
        // older prefab / scene instance has none).
        MissileLauncherReloadVisual reloadVisual = weapon.GetComponent<MissileLauncherReloadVisual>();
        if (reloadVisual == null) reloadVisual = weapon.gameObject.AddComponent<MissileLauncherReloadVisual>();
        SetObjectReference(serialized, "reloadVisual", reloadVisual);
        serialized.ApplyModifiedPropertiesWithoutUndo();

        reloadVisual.SetTubes(muzzleA, muzzleB);
        EditorUtility.SetDirty(reloadVisual);

        if (muzzleA == null || muzzleB == null)
            Debug.LogWarning("未能从模型识别出两个导弹发射口（Missile_01 / Missile_02），" +
                "运行时会在 MissileLauncherWeapon 里重试，届时可能两根管子共用一个发射点。");
    }

    private static void SetObjectReference(SerializedObject serialized, string propertyName, Object value)
    {
        SerializedProperty property = serialized.FindProperty(propertyName);
        if (property != null) property.objectReferenceValue = value;
    }

    private static Transform FindDeepChild(Transform root, string name)
    {
        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            if (child.name == name) return child;
            Transform nested = FindDeepChild(child, name);
            if (nested != null) return nested;
        }
        return null;
    }

    private static void FitLauncherToFootprint(GameObject root, float targetMeters)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return;

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

        float currentFootprint = Mathf.Max(bounds.size.x, bounds.size.z);
        if (currentFootprint > 0.0001f)
            root.transform.localScale = Vector3.one * (targetMeters / currentFootprint);
    }

    /// <summary>Points every launcher trap definition at the generated prefab.</summary>
    public static void AssignPrefabToTrapDefinitions(GameObject prefab)
    {
        if (prefab == null) prefab = AssetDatabase.LoadAssetAtPath<GameObject>(LauncherPrefabPath);
        if (prefab == null) return;

        for (int i = 0; i < TrapDefinitionPaths.Length; i++)
        {
            TrapDefinition definition = AssetDatabase.LoadAssetAtPath<TrapDefinition>(TrapDefinitionPaths[i]);
            if (definition == null) continue;
            // Rebuilding visuals must retain the existing Cost (including explicit 0).
            // Prices live in the definitions, not in this prefab generation path.
            var serialized = new SerializedObject(definition);
            SerializedProperty prefabProperty = serialized.FindProperty("prefab");
            if (prefabProperty == null) continue;
            prefabProperty.objectReferenceValue = prefab;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(definition);
        }
    }

    /// <summary>Batch-mode entry point: regenerates the prefab and relinks the trap definitions.</summary>
    public static void CreateLauncherPrefabBatch()
    {
        CreateLauncherPrefab();
        EditorSceneManager.SaveOpenScenes();
    }

    /// <summary>Regenerates just the URP flame materials (menu entry for quick iteration).</summary>
    [MenuItem("Tools/塔防/生成尾焰材质")]
    public static void CreateExhaustMaterials()
    {
        Dictionary<string, Material> materials = EnsureExhaustMaterials();
        Debug.Log($"尾焰材质已就绪（{materials.Count} 个）：{ExhaustMaterialFolder}");
    }

    /// <summary>
    /// Diagnostic: prints the measured plume geometry (axis, offsets, sizes) so the
    /// mount can be checked in batch mode without opening the editor.
    /// </summary>
    [MenuItem("Tools/塔防/诊断尾焰挂载")]
    public static void ProbeExhaustMount()
    {
        GameObject plumes = AssetDatabase.LoadAssetAtPath<GameObject>(ExhaustModelPath);
        if (plumes == null) { Debug.LogError($"找不到尾焰模型：{ExhaustModelPath}"); return; }
        Debug.Log($"尾焰模型根对象：{plumes.name}");
        Transform[] all = plumes.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < all.Length; i++)
        {
            var renderer = all[i].GetComponent<Renderer>();
            if (renderer == null) continue;
            Vector3 center = plumes.transform.InverseTransformPoint(renderer.bounds.center);
            Debug.Log($"  {all[i].name}: center={center} size={renderer.bounds.size} mat={(renderer.sharedMaterial != null ? renderer.sharedMaterial.name : "无")}");
        }

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(LauncherPrefabPath);
        if (prefab == null) { Debug.Log("预制体还没生成。"); return; }
        GameObject instance = (GameObject)Object.Instantiate(prefab);
        MissileLauncherTurret turret = instance.GetComponent<MissileLauncherTurret>();
        if (turret != null && turret.HasRig)
        {
            Transform pitch = turret.PitchPivot;
            Debug.Log($"Pitch Pivot 世界缩放={pitch.lossyScale}，barrel=({turret.RestBarrelDirectionWorld})");
            for (int i = 0; i < Barrels.Length; i++)
            {
                Transform barrel = FindDeepChild(instance.transform, Barrels[i]);
                Bounds bounds;
                if (barrel == null || !TryGetBoundsInSpace(pitch, barrel, out bounds)) continue;
                Debug.Log($"  {Barrels[i]} 在 Pitch Pivot 空间 bounds center={bounds.center} size={bounds.size}");
            }
            Renderer[] plumeRenderers = instance.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < plumeRenderers.Length; i++)
            {
                if (!plumeRenderers[i].transform.IsChildOf(pitch)) continue;
                Material material = plumeRenderers[i].sharedMaterial;
                Debug.Log($"  渲染器 {plumeRenderers[i].name}: 材质={(material != null ? material.name : "无")}" +
                    $" 着色器={(material != null && material.shader != null ? material.shader.name : "无")}");
            }
            Transform[] found = instance.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < found.Length; i++)
                if (found[i].name.StartsWith(PlumeRootName, System.StringComparison.Ordinal))
                    Debug.Log($"  {found[i].name}: localPos={found[i].localPosition} localRot={found[i].localEulerAngles} scale={found[i].localScale}");
        }
        Object.DestroyImmediate(instance);
    }
}

