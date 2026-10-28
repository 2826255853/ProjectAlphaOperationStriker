using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Creates, validates and previews the editable trap-menu Canvas assets.</summary>
public static class TrapSelectionMenuAuthoring
{
    private const string RootPath = "Assets/Resources/UI";
    private const string MenuPath = RootPath + "/TrapSelectionMenu.prefab";
    private const string CardPath = RootPath + "/TrapSelectionMenuCard.prefab";
    private const string IconFolder = "Assets/Models/TrapMenu/Icons";
    private const string OutputDirectory = "C:/Users/ASUS/Desktop/陷阱素材文件夹/TrapSelectionMenu";
    private static readonly Color Panel = new Color(.018f, .055f, .082f, .97f);
    private static readonly Color ButtonColor = new Color(.035f, .13f, .18f, .98f);
    private static readonly Color Cyan = new Color(.12f, .82f, .94f, 1f);
    private static readonly Color Amber = new Color(1f, .59f, .16f, 1f);
    private static readonly Color Text = new Color(.83f, .91f, .95f, 1f);
    private static readonly HashSet<string> OfficialTrapIds = new HashSet<string>(StringComparer.Ordinal)
    {
        "auto_sentry_turret", "ground-spike", "dual_missile_launcher_loaded", "flamethrower",
        "betty7", "throat-lamp", "repair-station"
    };

    [MenuItem("Tools/行动打击/陷阱菜单/创建 Canvas 预制体（缺失时）")]
    public static void CreatePrefabIfMissing()
    {
        EnsureFolder(RootPath);
        if (File.Exists(MenuPath)) throw new InvalidOperationException("Trap menu prefab already exists. Use the explicit rebuild command to replace it.");
        SaveCardPrefab(false);
        SaveMenuPrefab();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    [MenuItem("Tools/行动打击/陷阱菜单/显式重建 Canvas 预制体")]
    public static void RebuildPrefab()
    {
        EnsureFolder(RootPath);
        SaveCardPrefab(true);
        SaveMenuPrefab();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    [MenuItem("Tools/行动打击/陷阱菜单/生成缺失模型缩略图")]
    public static void GenerateMissingIcons() => GenerateIcons(false);

    [MenuItem("Tools/行动打击/陷阱菜单/重新生成全部模型缩略图")]
    public static void RegenerateAllIcons() => GenerateIcons(true);

    [MenuItem("Tools/行动打击/陷阱菜单/验证 Canvas 与图标")]
    public static void Validate()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MenuPath);
        GameObject cardPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CardPath);
        if (prefab == null || prefab.GetComponent<Canvas>() == null || prefab.GetComponent<TrapSelectionMenuView>() == null)
            throw new InvalidOperationException("Trap menu Canvas prefab is missing or incomplete: " + MenuPath);
        if (cardPrefab == null || cardPrefab.GetComponent<TrapSelectionMenuCardView>() == null)
            throw new InvalidOperationException("Trap menu card prefab is missing or incomplete: " + CardPath);
        TrapSelectionMenuView view = prefab.GetComponent<TrapSelectionMenuView>();
        SerializedObject serialized = new SerializedObject(view);
        SerializedProperty cards = serialized.FindProperty("cards");
        SerializedProperty slots = serialized.FindProperty("slotButtons");
        SerializedProperty filters = serialized.FindProperty("filterButtons");
        if (cards == null || cards.arraySize != 54 || slots == null || slots.arraySize != 5 || filters == null || filters.arraySize != 4)
            throw new InvalidOperationException("Expected 54 card references, five slot buttons and four filter buttons.");
        for (int i = 0; i < cards.arraySize; i++)
            if (cards.GetArrayElementAtIndex(i).objectReferenceValue == null)
                throw new InvalidOperationException("Trap menu card reference missing at index " + i);
        var definitions = LoadDefinitions().Where(d => OfficialTrapIds.Contains(d.TrapId)).GroupBy(d => d.TrapId).ToArray();
        var report = new List<string> { "TRAP_MENU_CANVAS_VALIDATION_PASS", "Cards=54; slots=5; filters=4" };
        foreach (var group in definitions)
        {
            Sprite icon = group.First().MenuIcon;
            if (icon == null) throw new InvalidOperationException("Missing menu icon for TrapId " + group.Key);
            if (group.Any(d => d.MenuIcon != icon)) throw new InvalidOperationException("Definitions sharing TrapId have different icons: " + group.Key);
            report.Add(group.Key + " = " + AssetDatabase.GetAssetPath(icon));
        }
        Directory.CreateDirectory(OutputDirectory);
        File.WriteAllLines(Path.Combine(OutputDirectory, "validation.txt"), report);
        Debug.Log("Trap selection Canvas validation passed. " + Path.Combine(OutputDirectory, "validation.txt"));
    }

    public static void ValidateAndPreviewBatch()
    {
        Validate();
        RenderPreviewBatch();
    }

    public static void RebuildMenuAndPreviewBatch()
    {
        GenerateIcons(false);
        SaveCardPrefab(true);
        SaveMenuPrefab();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Validate();
        RenderPreviewBatch();
    }

    [MenuItem("Tools/行动打击/陷阱菜单/生成 Canvas 预览")]
    public static void RenderPreviewBatch()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MenuPath);
        if (prefab == null) throw new InvalidOperationException("Generate the trap-menu prefab first.");
        Directory.CreateDirectory(OutputDirectory);
        Scene scene = EditorSceneManager.NewPreviewScene();
        RenderTexture previous = RenderTexture.active;
        RenderTexture target = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
        Texture2D pixels = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
        try
        {
            GameObject root = Object.Instantiate(prefab);
            SceneManager.MoveGameObjectToScene(root, scene);
            var cameraObject = new GameObject("Trap Menu Preview Camera", typeof(Camera));
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            Camera camera = cameraObject.GetComponent<Camera>();
            camera.scene = scene;
            camera.targetTexture = target;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.035f, .055f, .072f);
            camera.orthographic = true;
            camera.orthographicSize = 540f;
            camera.transform.position = new Vector3(0, 0, -10);
            camera.transform.rotation = Quaternion.identity;
            Canvas canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 1f;
            TrapDefinition[] definitions = LoadDefinitions().Where(d => OfficialTrapIds.Contains(d.TrapId) && d.MenuIcon != null)
                .GroupBy(d => d.TrapId).Select(g => g.First()).OrderBy(d => d.DisplayName, StringComparer.Ordinal).ToArray();
            TrapSelectionMenuView view = root.GetComponent<TrapSelectionMenuView>();
            view.SetVisible(true);
            CaptureSample(view, camera, target, pixels, definitions, 0, definitions.FirstOrDefault(), "金币：100", null, "menu-default.png");
            CaptureSample(view, camera, target, pixels, definitions, Mathf.Min(1, definitions.Length - 1), definitions.Skip(1).FirstOrDefault(), "金币：100", null, "menu-hover-details.png");
            TrapDefinition unaffordable = definitions.FirstOrDefault();
            CaptureSample(view, camera, target, pixels, definitions, 0, unaffordable, "金币：0", unaffordable != null ? $"金币不足，还差 {unaffordable.Cost}" : "金币不足", "menu-insufficient.png");
            File.Copy(Path.Combine(OutputDirectory, "menu-default.png"), Path.Combine(OutputDirectory, "canvas-layout.png"), true);
            CaptureResolution(view, camera, 1366, 768, "resolution-1366x768.png");
            CaptureResolution(view, camera, 1280, 720, "resolution-1280x720.png");
            CaptureResolution(view, camera, 2560, 1080, "resolution-ultrawide-2560x1080.png");
        }
        finally
        {
            RenderTexture.active = previous;
            EditorSceneManager.ClosePreviewScene(scene);
            Object.DestroyImmediate(pixels);
            target.Release();
            Object.DestroyImmediate(target);
        }
    }

    private static void CaptureSample(TrapSelectionMenuView view, Camera camera, RenderTexture target, Texture2D pixels,
        TrapDefinition[] definitions, int selected, TrapDefinition detail, string balance, string hint, string filename)
    {
        view.SetPreview(definitions, selected, detail, balance, hint);
        Canvas.ForceUpdateCanvases();
        foreach (TextMeshProUGUI label in view.GetComponentsInChildren<TextMeshProUGUI>(true)) label.ForceMeshUpdate();
        camera.Render();
        camera.Render();
        RenderTexture.active = target;
        pixels.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
        pixels.Apply();
        File.WriteAllBytes(Path.Combine(OutputDirectory, filename), pixels.EncodeToPNG());
        Debug.Log("Trap menu Canvas preview written: " + Path.Combine(OutputDirectory, filename));
    }

    private static void CaptureResolution(TrapSelectionMenuView view, Camera camera, int width, int height, string filename)
    {
        var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
        var pixels = new Texture2D(width, height, TextureFormat.RGB24, false);
        RenderTexture previous = RenderTexture.active;
        RenderTexture oldTarget = camera.targetTexture;
        float oldSize = camera.orthographicSize;
        try
        {
            camera.targetTexture = target;
            camera.orthographicSize = height * .5f;
            Canvas.ForceUpdateCanvases();
            foreach (TextMeshProUGUI label in view.GetComponentsInChildren<TextMeshProUGUI>(true)) label.ForceMeshUpdate();
            camera.Render();
            camera.Render();
            RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            pixels.Apply();
            File.WriteAllBytes(Path.Combine(OutputDirectory, filename), pixels.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previous;
            camera.targetTexture = oldTarget;
            camera.orthographicSize = oldSize;
            Object.DestroyImmediate(pixels);
            target.Release();
            Object.DestroyImmediate(target);
        }
    }

    private static void SaveMenuPrefab()
    {
        var root = new GameObject("Trap Selection Menu Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(CanvasGroup), typeof(TrapSelectionMenuView));
        try
        {
            Canvas canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 300;
            CanvasScaler scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;

            Image dim = Image(root.transform, "Backdrop", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Color(.005f, .016f, .027f, .72f));
            dim.raycastTarget = true;
            GameObject panel = Obj(root.transform, "Menu Panel");
            RectTransform pr = panel.GetComponent<RectTransform>();
            pr.anchorMin = pr.anchorMax = new Vector2(.5f, .5f);
            pr.pivot = new Vector2(.5f, .5f);
            pr.sizeDelta = new Vector2(1800, 990);
            Image panelImage = panel.AddComponent<Image>();
            panelImage.color = Panel;

            AddText(panel.transform, "陷阱选择  /  防御配置", 32, -20, 700, 44, 36, Color.white, TextAlignmentOptions.Left);
            AddText(panel.transform, "TACTICAL DEFENSE SYSTEM", 32, -68, 1000, 26, 17, Cyan, TextAlignmentOptions.Left);
            TextMeshProUGUI balance = AddText(panel.transform, "金币：--", 1390, -36, 375, 42, 25, Amber, TextAlignmentOptions.Right);
            TextMeshProUGUI instruction = AddText(panel.transform, "", 32, -98, 1550, 28, 14, Text, TextAlignmentOptions.Left);

            var slotButtons = new List<Object>();
            var slotLabels = new List<Object>();
            var slotHighlights = new List<Object>();
            for (int i = 0; i < 5; i++)
            {
                Button button = MakeButton(panel.transform, $"Slot {i + 4}", 32 + i * 344, -132, 334, 66, ButtonColor, out TextMeshProUGUI label);
                label.text = $"{i + 4}  ·  空槽";
                label.fontSize = 18;
                label.alignment = TextAlignmentOptions.Center;
                Image highlight = Image(button.transform, "Selected Accent", new Vector2(0, 0), new Vector2(0, 1), Vector2.zero, new Vector2(5, 0), Cyan);
                highlight.raycastTarget = false;
                highlight.enabled = false;
                slotButtons.Add(button);
                slotLabels.Add(label);
                slotHighlights.Add(highlight);
            }

            string[] filterNames = { "全部", "地面", "墙面", "天花板" };
            var filterButtons = new List<Object>();
            var filterLabels = new List<Object>();
            for (int i = 0; i < filterNames.Length; i++)
            {
                Button button = MakeButton(panel.transform, "Filter " + filterNames[i], 32 + i * 101, -202, 92, 42, ButtonColor, out TextMeshProUGUI label);
                label.text = filterNames[i];
                label.fontSize = 16;
                label.alignment = TextAlignmentOptions.Center;
                filterButtons.Add(button);
                filterLabels.Add(label);
            }

            TextMeshProUGUI pageLabel = AddText(panel.transform, "1 / 1", 865, -204, 180, 38, 15, Text, TextAlignmentOptions.Center);
            Button previous = MakeButton(panel.transform, "Previous Page", 745, -202, 52, 40, ButtonColor, out TextMeshProUGUI previousText);
            previousText.text = "‹";
            previousText.fontSize = 28;
            previousText.alignment = TextAlignmentOptions.Center;
            Button next = MakeButton(panel.transform, "Next Page", 1008, -202, 52, 40, ButtonColor, out TextMeshProUGUI nextText);
            nextText.text = "›";
            nextText.fontSize = 28;
            nextText.alignment = TextAlignmentOptions.Center;

            GameObject grid = Obj(panel.transform, "Trap Card Grid 6 x 9");
            RectTransform gridRect = grid.GetComponent<RectTransform>();
            gridRect.anchorMin = gridRect.anchorMax = new Vector2(0, 1);
            gridRect.pivot = new Vector2(0, 1);
            gridRect.anchoredPosition = new Vector2(32, -254);
            gridRect.sizeDelta = new Vector2(1036, 688);
            GridLayoutGroup layout = grid.AddComponent<GridLayoutGroup>();
            layout.cellSize = new Vector2(108, 108);
            layout.spacing = new Vector2(8, 8);
            layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            layout.constraintCount = 9;
            layout.startCorner = GridLayoutGroup.Corner.UpperLeft;
            layout.startAxis = GridLayoutGroup.Axis.Horizontal;
            layout.childAlignment = TextAnchor.UpperLeft;
            GameObject cardAsset = AssetDatabase.LoadAssetAtPath<GameObject>(CardPath);
            if (cardAsset == null) throw new InvalidOperationException("Trap card prefab was not generated.");
            var cards = new List<Object>();
            for (int i = 0; i < 54; i++)
            {
                GameObject card = PrefabUtility.InstantiatePrefab(cardAsset, grid.transform) as GameObject;
                card.name = $"Trap Card {i + 1:00}";
                cards.Add(card.GetComponent<TrapSelectionMenuCardView>());
            }

            GameObject detail = Obj(panel.transform, "Trap Details");
            RectTransform dr = detail.GetComponent<RectTransform>();
            dr.anchorMin = dr.anchorMax = new Vector2(0, 1);
            dr.pivot = new Vector2(0, 1);
            dr.anchoredPosition = new Vector2(1080, -254);
            dr.sizeDelta = new Vector2(688, 688);
            Image detailBackground = detail.AddComponent<Image>();
            detailBackground.color = ButtonColor;
            AddText(detail.transform, "SELECTED DEFENSE", 24, -18, 640, 32, 15, Amber, TextAlignmentOptions.Left);
            Image detailIcon = Image(detail.transform, "Large Icon", new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(-130, -300), new Vector2(130, -32), new Color(1, 1, 1, .9f));
            detailIcon.preserveAspect = true;
            TextMeshProUGUI fallback = AddText(detail.transform, "GROUND", 120, -92, 448, 190, 26, Cyan, TextAlignmentOptions.Center);
            fallback.alignment = TextAlignmentOptions.Center;
            TextMeshProUGUI detailName = AddText(detail.transform, "选择一个防御设施", 24, -320, 640, 58, 26, Color.white, TextAlignmentOptions.Left);
            TextMeshProUGUI detailMount = AddText(detail.transform, "将鼠标移到卡片上查看详情", 24, -376, 640, 34, 18, Cyan, TextAlignmentOptions.Left);
            TextMeshProUGUI footprint = AddText(detail.transform, "", 24, -423, 640, 34, 17, Text, TextAlignmentOptions.Left);
            TextMeshProUGUI detailCost = AddText(detail.transform, "", 24, -470, 640, 36, 20, Amber, TextAlignmentOptions.Left);
            Image divider = Image(detail.transform, "Details Divider", new Vector2(0, 1), new Vector2(1, 1), new Vector2(24, -520), new Vector2(-24, -517), Cyan);
            divider.raycastTarget = false;
            TextMeshProUGUI hint = AddText(detail.transform, "", 24, -534, 640, 76, 17, Text, TextAlignmentOptions.Left);
            hint.textWrappingMode = TextWrappingModes.Normal;

            Button clear = MakeButton(panel.transform, "Clear Selected Slot", 32, -946, 220, 38, ButtonColor, out TextMeshProUGUI clearText);
            clearText.text = "清空当前槽位";
            clearText.fontSize = 16;
            clearText.alignment = TextAlignmentOptions.Center;
            Button close = MakeButton(panel.transform, "Close Menu", 1434, -946, 334, 38, new Color(.22f, .12f, .08f, .98f), out TextMeshProUGUI closeText);
            closeText.text = "关闭菜单   ·   N / Esc";
            closeText.fontSize = 16;
            closeText.alignment = TextAlignmentOptions.Center;
            AddText(panel.transform, "选择不扣款  /  确认放置时支付", 270, -946, 450, 38, 14, new Color(.62f, .74f, .79f), TextAlignmentOptions.Left);

            TrapSelectionMenuView view = root.GetComponent<TrapSelectionMenuView>();
            SerializedObject serialized = new SerializedObject(view);
            SetArray(serialized, "cards", cards);
            SetArray(serialized, "slotButtons", slotButtons);
            SetArray(serialized, "slotLabels", slotLabels);
            SetArray(serialized, "slotHighlights", slotHighlights);
            SetArray(serialized, "filterButtons", filterButtons);
            SetArray(serialized, "filterLabels", filterLabels);
            SetObject(serialized, "balanceLabel", balance);
            SetObject(serialized, "instructionLabel", instruction);
            SetObject(serialized, "detailIcon", detailIcon);
            SetObject(serialized, "detailFallbackIcon", fallback);
            SetObject(serialized, "detailName", detailName);
            SetObject(serialized, "detailMount", detailMount);
            SetObject(serialized, "detailFootprint", footprint);
            SetObject(serialized, "detailCost", detailCost);
            SetObject(serialized, "purchaseHint", hint);
            SetObject(serialized, "clearButton", clear);
            SetObject(serialized, "closeButton", close);
            SetObject(serialized, "previousPageButton", previous);
            SetObject(serialized, "nextPageButton", next);
            SetObject(serialized, "pageLabel", pageLabel);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, MenuPath);
        }
        finally { Object.DestroyImmediate(root); }
    }

    private static void SaveCardPrefab(bool overwrite)
    {
        if (!overwrite && AssetDatabase.LoadAssetAtPath<GameObject>(CardPath) != null) return;
        var root = new GameObject("Trap Selection Card", typeof(RectTransform), typeof(Image), typeof(Button), typeof(TrapSelectionMenuCardView));
        try
        {
            Image background = root.GetComponent<Image>();
            background.color = new Color(.035f, .13f, .18f, .98f);
            Button button = root.GetComponent<Button>();
            button.targetGraphic = background;
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(.45f, .91f, .98f);
            colors.pressedColor = new Color(.19f, .59f, .68f);
            colors.selectedColor = colors.highlightedColor;
            colors.disabledColor = Color.white;
            colors.fadeDuration = .08f;
            button.colors = colors;
            Image icon = Image(root.transform, "Icon", new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(-22, -48), new Vector2(22, -6), Color.white);
            icon.preserveAspect = true;
            TextMeshProUGUI fallback = AddText(root.transform, "G", 6, -8, 96, 38, 23, Cyan, TextAlignmentOptions.Center);
            fallback.alignment = TextAlignmentOptions.Center;
            TextMeshProUGUI name = AddText(root.transform, "", 5, -51, 98, 24, 14, Color.white, TextAlignmentOptions.Center);
            name.enableAutoSizing = true;
            name.fontSizeMin = 9;
            name.fontSizeMax = 14;
            name.textWrappingMode = TextWrappingModes.NoWrap;
            name.overflowMode = TextOverflowModes.Ellipsis;
            TextMeshProUGUI mount = AddText(root.transform, "", 4, -75, 100, 14, 10, new Color(.65f, .84f, .90f), TextAlignmentOptions.Center);
            TextMeshProUGUI cost = AddText(root.transform, "", 4, -89, 100, 15, 11, Amber, TextAlignmentOptions.Center);
            TextMeshProUGUI status = AddText(root.transform, "", 3, -103, 102, 13, 9, new Color(1f, .48f, .40f), TextAlignmentOptions.Center);
            TrapSelectionMenuCardView card = root.GetComponent<TrapSelectionMenuCardView>();
            SerializedObject serialized = new SerializedObject(card);
            SetObject(serialized, "button", button);
            SetObject(serialized, "cardBackground", background);
            SetObject(serialized, "icon", icon);
            SetObject(serialized, "fallbackIcon", fallback);
            SetObject(serialized, "nameLabel", name);
            SetObject(serialized, "mountLabel", mount);
            SetObject(serialized, "costLabel", cost);
            SetObject(serialized, "statusLabel", status);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, CardPath);
        }
        finally { Object.DestroyImmediate(root); }
    }

    private static void GenerateIcons(bool overwrite)
    {
        EnsureFolder("Assets/Models");
        EnsureFolder("Assets/Models/TrapMenu");
        EnsureFolder(IconFolder);
        Directory.CreateDirectory(Path.Combine(OutputDirectory, "icon-previews"));
        var groups = LoadDefinitions().Where(d => OfficialTrapIds.Contains(d.TrapId)).GroupBy(d => d.TrapId).ToList();
        Scene scene = EditorSceneManager.NewPreviewScene();
        Camera camera = null;
        RenderTexture previous = RenderTexture.active;
        var target = new RenderTexture(512, 512, 24, RenderTextureFormat.ARGB32);
        var pixels = new Texture2D(512, 512, TextureFormat.RGBA32, false, false);
        try
        {
            GameObject cameraObject = new GameObject("Trap Menu Icon Camera", typeof(Camera));
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            camera = cameraObject.GetComponent<Camera>();
            camera.scene = scene;
            camera.targetTexture = target;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0, 0, 0, 0);
            camera.orthographic = true;
            camera.nearClipPlane = .01f;
            camera.farClipPlane = 1000f;
            GameObject keyLight = new GameObject("Icon Key Light", typeof(Light));
            SceneManager.MoveGameObjectToScene(keyLight, scene);
            keyLight.transform.rotation = Quaternion.Euler(42, -35, 0);
            keyLight.GetComponent<Light>().type = LightType.Directional;
            keyLight.GetComponent<Light>().intensity = 1.3f;
            GameObject fillLight = new GameObject("Icon Fill Light", typeof(Light));
            SceneManager.MoveGameObjectToScene(fillLight, scene);
            fillLight.transform.rotation = Quaternion.Euler(25, 145, 0);
            fillLight.GetComponent<Light>().type = LightType.Directional;
            fillLight.GetComponent<Light>().intensity = .55f;

            foreach (var group in groups)
            {
                string path = IconFolder + "/" + SafeName(group.Key) + ".png";
                Sprite existing = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (!overwrite && existing != null)
                {
                    AssignIcon(group, existing);
                    string previewPath = Path.Combine(OutputDirectory, "icon-previews", SafeName(group.Key) + ".png");
                    if (File.Exists(path) && !File.Exists(previewPath)) File.Copy(path, previewPath, true);
                    continue;
                }
                TrapDefinition definition = group.FirstOrDefault(d => d.Prefab != null);
                GameObject sourceModel = ResolveIconModel(group.Key, definition);
                if (sourceModel == null) throw new InvalidOperationException("No model asset or prefab for trap icon: " + group.Key);
                var visualRoot = new GameObject("Trap Icon " + group.Key);
                SceneManager.MoveGameObjectToScene(visualRoot, scene);
                var renderers = new List<Renderer>();
                CopyMeshesOnly(sourceModel.transform, visualRoot.transform, renderers);
                if (renderers.Count == 0) throw new InvalidOperationException("No renderable model meshes found at " + AssetDatabase.GetAssetPath(sourceModel) + " for " + group.Key);
                visualRoot.transform.rotation = Quaternion.Euler(0, 32, 0);
                Bounds bounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Count; i++) bounds.Encapsulate(renderers[i].bounds);
                Vector3 center = bounds.center;
                float radius = Mathf.Max(bounds.extents.magnitude, .25f);
                camera.transform.position = center + new Vector3(radius * 1.25f, radius * .92f, -radius * 1.65f);
                camera.transform.LookAt(center);
                camera.orthographicSize = bounds.extents.magnitude * .92f;
                camera.farClipPlane = radius * 12f + 20f;
                camera.Render();
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, 512, 512), 0, 0);
                pixels.Apply();
                File.WriteAllBytes(path, pixels.EncodeToPNG());
                Object.DestroyImmediate(visualRoot);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer != null)
                {
                    importer.textureType = TextureImporterType.Sprite;
                    importer.spriteImportMode = SpriteImportMode.Single;
                    importer.alphaIsTransparency = true;
                    importer.mipmapEnabled = false;
                    importer.wrapMode = TextureWrapMode.Clamp;
                    importer.filterMode = FilterMode.Bilinear;
                    importer.SaveAndReimport();
                }
                Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (sprite == null) throw new InvalidOperationException("Rendered icon could not be imported: " + path);
                AssignIcon(group, sprite);
                File.Copy(path, Path.Combine(OutputDirectory, "icon-previews", SafeName(group.Key) + ".png"), true);
                Debug.Log("Generated trap menu icon: " + path);
            }
        }
        finally
        {
            RenderTexture.active = previous;
            EditorSceneManager.ClosePreviewScene(scene);
            Object.DestroyImmediate(pixels);
            target.Release();
            Object.DestroyImmediate(target);
        }
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    private static void AssignIcon(IEnumerable<TrapDefinition> definitions, Sprite sprite)
    {
        foreach (TrapDefinition definition in definitions)
        {
            string sourcePath = AssetDatabase.GetAssetPath(definition);
            string backupFolder = Path.Combine(OutputDirectory, "backups", "TrapDefinitions");
            Directory.CreateDirectory(backupFolder);
            string backupPath = Path.Combine(backupFolder, Path.GetFileName(sourcePath) + ".before-menu-icons");
            if (!File.Exists(backupPath)) File.Copy(sourcePath, backupPath);
            string metaPath = sourcePath + ".meta";
            if (File.Exists(metaPath) && !File.Exists(backupPath + ".meta")) File.Copy(metaPath, backupPath + ".meta");
            SerializedObject serialized = new SerializedObject(definition);
            serialized.FindProperty("menuIcon").objectReferenceValue = sprite;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(definition);
        }
    }

    private static IEnumerable<TrapDefinition> LoadDefinitions()
    {
        foreach (string guid in AssetDatabase.FindAssets("t:TrapDefinition", new[] { "Assets/Resources", "Assets/TrapDefinitions" }))
        {
            TrapDefinition definition = AssetDatabase.LoadAssetAtPath<TrapDefinition>(AssetDatabase.GUIDToAssetPath(guid));
            if (definition != null) yield return definition;
        }
    }

    private static GameObject ResolveIconModel(string trapId, TrapDefinition definition)
    {
        string path = trapId switch
        {
            "auto_sentry_turret" => "Assets/Models/SentryMachineGun/SentryMachineGun.fbx",
            "ground-spike" => "Assets/Models/GroundSpike/GroundSpike.fbx",
            "dual_missile_launcher_loaded" => "Assets/Prefabs/DualMissileLauncher.prefab",
            "flamethrower" => "Assets/Models/Flamethrower/Flamethrower.fbx",
            "betty7" => "Assets/Prefabs/Betty7.prefab",
            "throat-lamp" => "Assets/Models/ThroatLamp/ThroatLamp.fbx",
            "repair-station" => "Assets/Models/RepairStation/RepairStation.fbx",
            _ => null
        };
        GameObject model = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(path);
        return model != null ? model : definition != null ? definition.Prefab : null;
    }

    private static void CopyMeshesOnly(Transform source, Transform parent, List<Renderer> renderers)
    {
        var copy = new GameObject(source.name);
        copy.transform.SetParent(parent, false);
        copy.transform.localPosition = source.localPosition;
        copy.transform.localRotation = source.localRotation;
        copy.transform.localScale = source.localScale;
        MeshFilter filter = source.GetComponent<MeshFilter>();
        MeshRenderer meshRenderer = source.GetComponent<MeshRenderer>();
        if (filter != null && filter.sharedMesh != null && meshRenderer != null && !IsNonVisual(source.name))
        {
            MeshFilter cloneFilter = copy.AddComponent<MeshFilter>();
            cloneFilter.sharedMesh = filter.sharedMesh;
            MeshRenderer cloneRenderer = copy.AddComponent<MeshRenderer>();
            cloneRenderer.sharedMaterials = meshRenderer.sharedMaterials;
            renderers.Add(cloneRenderer);
        }
        for (int i = 0; i < source.childCount; i++) CopyMeshesOnly(source.GetChild(i), copy.transform, renderers);
    }

    private static bool IsNonVisual(string name)
    {
        string n = name.ToLowerInvariant();
        return n.Contains("range") || n.Contains("radius") || n.Contains("trigger") || n.Contains("projectile")
            || n.Contains("attack") || n.Contains("preview") || n.Contains("debug") || n.Contains("effect")
            || n.Contains("exhaust") || n.Contains("gizmo") || n.Contains("volume");
    }

    private static Button MakeButton(Transform parent, string name, float x, float top, float width, float height, Color color, out TextMeshProUGUI label)
    {
        GameObject go = TopObject(parent, name, x, top, width, height);
        Image image = go.AddComponent<Image>();
        image.color = color;
        Button button = go.AddComponent<Button>();
        button.targetGraphic = image;
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(.55f, .92f, 1f);
        colors.pressedColor = new Color(.24f, .64f, .74f);
        colors.selectedColor = colors.highlightedColor;
        colors.fadeDuration = .08f;
        button.colors = colors;
        label = AddText(go.transform, "", 8, -2, width - 16, height - 4, 18, Text, TextAlignmentOptions.Left);
        label.raycastTarget = false;
        return button;
    }

    private static TextMeshProUGUI AddText(Transform parent, string value, float x, float top, float width, float height, float fontSize, Color color, TextAlignmentOptions alignment)
    {
        GameObject go = TopObject(parent, value.Length > 0 ? value : "Label", x, top, width, height);
        TextMeshProUGUI text = go.AddComponent<TextMeshProUGUI>();
        text.font = Resources.Load<TMP_FontAsset>(GameStartMenuView.MenuFontResourcePath);
        if (text.font == null) throw new InvalidOperationException("Missing bundled UI font. Generate the tactical main menu font first.");
        text.text = value;
        text.fontSize = fontSize;
        text.color = color;
        text.alignment = alignment;
        text.raycastTarget = false;
        text.textWrappingMode = TextWrappingModes.Normal;
        return text;
    }

    private static GameObject TopObject(Transform parent, string name, float x, float top, float width, float height)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
        rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(x, top);
        rect.sizeDelta = new Vector2(width, height);
        return go;
    }

    private static GameObject Obj(Transform parent, string name)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go;
    }

    private static Image Image(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax, Color color)
    {
        GameObject go = Obj(parent, name);
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
        Image image = go.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static void SetArray(SerializedObject target, string name, IList<Object> values)
    {
        SerializedProperty property = target.FindProperty(name);
        property.arraySize = values.Count;
        for (int i = 0; i < values.Count; i++) property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
    }

    private static void SetObject(SerializedObject target, string name, Object value)
        => target.FindProperty(name).objectReferenceValue = value;

    private static string SafeName(string value)
    {
        foreach (char c in Path.GetInvalidFileNameChars()) value = value.Replace(c, '_');
        return value.Replace('/', '_').Replace('\\', '_').Replace(' ', '_');
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int split = path.LastIndexOf('/');
        string parent = path.Substring(0, split);
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, path.Substring(split + 1));
    }
}
