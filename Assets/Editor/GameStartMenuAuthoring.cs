using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

public static class GameStartMenuAuthoring
{
    private const string BackgroundPath = "Assets/UI/MainMenuBackground.png";
    private const string PrefabPath = "Assets/Resources/UI/GameStartMenu.prefab";
    private const string FontPath = "Assets/Resources/UI/Fonts/MenuChinese SDF.asset";
    private const string SourceFontPath = "Assets/Resources/UI/Fonts/NotoSansCJKsc-Regular.otf";

    [MenuItem("Tools/行动打击/生成战术主菜单")]
    public static void Generate()
    {
        EnsureFolder("Assets/UI"); EnsureFolder("Assets/Resources"); EnsureFolder("Assets/Resources/UI");
        TMP_FontAsset font = EnsureChineseFont();
        TextureImporter importer = AssetImporter.GetAtPath(BackgroundPath) as TextureImporter;
        if (importer != null) { importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Single; importer.mipmapEnabled = false; importer.wrapMode = TextureWrapMode.Clamp; importer.filterMode = FilterMode.Bilinear; importer.SaveAndReimport(); }
        Sprite background = AssetDatabase.LoadAssetAtPath<Sprite>(BackgroundPath);
        if (background == null) throw new System.InvalidOperationException("Missing menu Sprite: " + BackgroundPath);
        GameObject root = new GameObject("Game Start Menu");
        try
        {
            GameStartMenuView.BuildLayout(root, background);
            string characters = string.Concat(root.GetComponentsInChildren<TextMeshProUGUI>(true).Select(t => t.text)) + "继续游戏";
            if (!font.TryAddCharacters(characters, out string missing))
                throw new InvalidOperationException("Chinese menu font is missing characters: " + missing);
            SaveFontSubAssets(font);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally { Object.DestroyImmediate(root); }
        AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
    }

    [MenuItem("Tools/行动打击/验证战术主菜单")]
    public static void Validate()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null || prefab.GetComponent<Canvas>() == null || prefab.GetComponent<GameStartMenuView>() == null)
            throw new System.InvalidOperationException("Menu prefab is missing or incomplete: " + PrefabPath);
        if (AssetDatabase.LoadAssetAtPath<Sprite>(BackgroundPath) == null)
            throw new System.InvalidOperationException("Menu background is missing: " + BackgroundPath);
        TMP_FontAsset font = Resources.Load<TMP_FontAsset>(GameStartMenuView.MenuFontResourcePath);
        if (font == null || font.sourceFontFile == null)
            throw new InvalidOperationException("Chinese menu font or bundled source font is missing.");
        if (font.atlasPopulationMode != AtlasPopulationMode.Dynamic)
            throw new InvalidOperationException("Menu font must retain dynamic glyph support for future Chinese text.");
        TextMeshProUGUI[] labels = prefab.GetComponentsInChildren<TextMeshProUGUI>(true);
        if (labels.Length == 0) throw new InvalidOperationException("Menu prefab has no text labels.");
        foreach (TextMeshProUGUI label in labels)
        {
            if (label.font != font) throw new InvalidOperationException("Menu label uses the wrong font: " + label.text);
            ValidateCharacters(font, label.text);
        }
        ValidateCharacters(font, "继续游戏");
        string output = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "GameStartMenu", "font-validation.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        File.WriteAllText(output, "PASS: bundled Chinese font covers all " + labels.Length + " menu labels, including inactive settings and resume text.\nFont: " + font.name + "\n" + string.Join("\n", labels.Select(t => t.text)) + "\n继续游戏\n");
        Debug.Log("Tactical menu Chinese font validation passed. " + output);
    }

    public static void GenerateAndValidate()
    {
        Generate();
        Validate();
        RenderFontPreview();
    }

    private static void RenderFontPreview()
    {
        var scene = EditorSceneManager.NewPreviewScene();
        RenderTexture previous = RenderTexture.active;
        var target = new RenderTexture(1339, 753, 24);
        var pixels = new Texture2D(1339, 753, TextureFormat.RGB24, false);
        try
        {
            GameObject root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
            var cameraObject = new GameObject("Menu Font Preview Camera", typeof(Camera));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraObject, scene);
            Camera camera = cameraObject.GetComponent<Camera>();
            camera.scene = scene;
            camera.targetTexture = target;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            Canvas canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 1f;
            var view = root.GetComponent<GameStartMenuView>();
            string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "GameStartMenu");
            for (int page = 0; page < 2; page++)
            {
                view.SetState(false, page == 1, 1f, true);
                Canvas.ForceUpdateCanvases();
                foreach (TextMeshProUGUI text in root.GetComponentsInChildren<TextMeshProUGUI>()) text.ForceMeshUpdate();
                camera.Render();
                camera.Render();
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                pixels.Apply();
                File.WriteAllBytes(Path.Combine(directory, page == 0 ? "font-main-menu.png" : "font-settings.png"), pixels.EncodeToPNG());
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
    }

    private static TMP_FontAsset EnsureChineseFont()
    {
        AssetDatabase.ImportAsset(SourceFontPath, ImportAssetOptions.ForceSynchronousImport);
        Font source = AssetDatabase.LoadAssetAtPath<Font>(SourceFontPath);
        if (source == null) throw new InvalidOperationException("Missing bundled Chinese font: " + SourceFontPath);
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (font != null) return font;
        font = TMP_FontAsset.CreateFontAsset(source);
        font.name = "MenuChinese SDF";
        font.isMultiAtlasTexturesEnabled = true;
        AssetDatabase.CreateAsset(font, FontPath);
        return font;
    }

    private static void SaveFontSubAssets(TMP_FontAsset font)
    {
        if (!AssetDatabase.Contains(font.material)) AssetDatabase.AddObjectToAsset(font.material, font);
        EditorUtility.SetDirty(font.material);
        foreach (Texture2D atlas in font.atlasTextures)
        {
            if (atlas == null) continue;
            if (!AssetDatabase.Contains(atlas)) AssetDatabase.AddObjectToAsset(atlas, font);
            EditorUtility.SetDirty(atlas);
        }
        EditorUtility.SetDirty(font);
    }

    private static void ValidateCharacters(TMP_FontAsset font, string text)
    {
        foreach (char character in text)
            if (!char.IsWhiteSpace(character) && !font.HasCharacter(character, false, true))
                throw new InvalidOperationException("Chinese menu font is missing U+" + ((int)character).ToString("X4") + ": " + character);
    }

    private static void EnsureFolder(string path)
    { if (AssetDatabase.IsValidFolder(path)) return; int split = path.LastIndexOf('/'); if (split <= 0) throw new System.InvalidOperationException(path); string parent = path.Substring(0, split); EnsureFolder(parent); AssetDatabase.CreateFolder(parent, path.Substring(split + 1)); }
}
