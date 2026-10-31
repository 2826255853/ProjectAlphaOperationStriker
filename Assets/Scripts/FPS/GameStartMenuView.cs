using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

public sealed class GameStartMenuView : MonoBehaviour
{
    public const string MenuFontResourcePath = "UI/Fonts/MenuChinese SDF";
    private static TMP_FontAsset menuFont;
    private static readonly Color Panel = new Color(.025f, .07f, .105f, .93f);
    private static readonly Color ButtonColor = new Color(.045f, .14f, .20f, .96f);
    private static readonly Color Cyan = new Color(.16f, .88f, 1f, 1f);
    private static readonly Color Amber = new Color(1f, .61f, .16f, 1f);
    private static readonly Color Body = new Color(.80f, .90f, .95f, 1f);

    [SerializeField] private GameObject mainPanel;
    [SerializeField] private GameObject settingsPanel;
    [SerializeField] private GameObject mapPanel;
    [SerializeField] private TextMeshProUGUI primaryLabel;
    [SerializeField] private Button primaryButton;
    [SerializeField] private Button mapButton;
    [SerializeField] private Button settingsButton;
    [SerializeField] private Button quitButton;
    [SerializeField] private Button backButton;
    [SerializeField] private Button mapBackButton;
    [SerializeField] private Slider volumeSlider;
    [SerializeField] private Toggle fullscreenToggle;
    [SerializeField] private Transform mapListContainer;
    [SerializeField] private TextMeshProUGUI mapStatusLabel;
    private readonly List<Button> mapButtons = new List<Button>();
    private bool initialized;

    public event Action<int> MapSelectionRequested;

    public static GameStartMenuView CreateFallback()
    {
        Debug.LogWarning("GameStartMenu prefab missing; using the generated-layout fallback without artwork.");
        return BuildLayout(new GameObject("Game Start Menu Fallback", typeof(RectTransform)), LoadRuntimeBackground());
    }

    public static GameStartMenuView BuildLayout(GameObject root, Sprite background)
    {
        root.name = "Game Start Menu";
        Canvas canvas = Add<Canvas>(root);
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 500;
        CanvasScaler scaler = Add<CanvasScaler>(root);
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = .5f;
        Add<GraphicRaycaster>(root);
        GameStartMenuView view = Add<GameStartMenuView>(root);

        Image bg = Image(root.transform, "Tactical Backdrop", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, background, Color.white);
        bg.preserveAspect = false;
        Image(root.transform, "Darkening", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, null, new Color(.005f, .015f, .028f, .44f));
        Image(root.transform, "Left Shadow", Vector2.zero, new Vector2(.59f, 1f), Vector2.zero, Vector2.zero, null, new Color(.006f, .018f, .032f, .70f));
        Image(root.transform, "Top Accent", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -54f), new Vector2(0f, -51f), null, Cyan);

        view.mainPanel = PanelObject(root.transform, "Main Panel");
        BuildMain(view, view.mainPanel.transform);
        view.settingsPanel = PanelObject(root.transform, "Settings Panel");
        BuildSettings(view, view.settingsPanel.transform);
        view.mapPanel = PanelObject(root.transform, "Map Panel");
        BuildMapSelection(view, view.mapPanel.transform);
        view.settingsPanel.SetActive(false);
        view.mapPanel.SetActive(false);
        return view;
    }

    public void Initialize(GameStartMenu owner)
    {
        if (primaryButton == null || mapButton == null || mapPanel == null)
            BuildLayout(gameObject, LoadRuntimeBackground());

        EnsureEventSystem();
        TMP_FontAsset font = LoadMenuFont();
        if (font != null)
            foreach (TextMeshProUGUI label in GetComponentsInChildren<TextMeshProUGUI>(true))
                label.font = font;
        if (initialized) return;
        initialized = true;
        primaryButton.onClick.AddListener(owner.BeginOrResume);
        mapButton.onClick.AddListener(owner.OpenMapSelection);
        settingsButton.onClick.AddListener(owner.OpenSettings);
        quitButton.onClick.AddListener(owner.QuitGame);
        backButton.onClick.AddListener(owner.CloseSettings);
        mapBackButton.onClick.AddListener(owner.CloseMapSelection);
        volumeSlider.onValueChanged.AddListener(owner.SetMasterVolume);
        fullscreenToggle.onValueChanged.AddListener(owner.SetFullscreen);
        MapSelectionRequested += owner.SelectMap;
    }

    public void SetVisible(bool visible) => gameObject.SetActive(visible);

    public void SetState(bool started, bool showSettings, float volume, bool fullscreen)
    {
        SetState(started, showSettings, false, volume, fullscreen, -1, false, MapSceneCatalog.GetMaps());
    }

    public void SetState(bool started, bool showSettings, bool showMaps, float volume, bool fullscreen,
        int selectedMapBuildIndex, bool loading, IReadOnlyList<MapSceneCatalog.Entry> maps)
    {
        primaryLabel.text = started ? "继续游戏" : "开始游戏";
        mainPanel.SetActive(!showSettings && !showMaps);
        settingsPanel.SetActive(showSettings);
        mapPanel.SetActive(showMaps);
        volumeSlider.SetValueWithoutNotify(Mathf.Clamp01(volume));
        fullscreenToggle.SetIsOnWithoutNotify(fullscreen);
        if (showMaps) RefreshMaps(maps, selectedMapBuildIndex, loading);
    }

    private static void BuildMain(GameStartMenuView view, Transform panel)
    {
        Text(panel, "作战指挥终端  //  01", new Vector2(44, -76), new Vector2(-44, -38), 18, Amber);
        Text(panel, "行动打击", new Vector2(40, -214), new Vector2(-40, -76), 84, Color.white);
        Text(panel, "边境防御协议", new Vector2(44, -260), new Vector2(-44, -218), 25, Cyan);
        Image(panel, "Divider", new Vector2(0, 1), new Vector2(0, 1), new Vector2(44, -286), new Vector2(300, -282), null, Cyan);
        Text(panel, "部署防御设施，守住边境前线。", new Vector2(44, -340), new Vector2(-44, -292), 22, Body);
        view.primaryButton = Button(panel, "Primary Action", new Vector2(44, -430), "开始游戏", Cyan);
        view.primaryLabel = view.primaryButton.GetComponentInChildren<TextMeshProUGUI>();
        view.mapButton = Button(panel, "Map Selection", new Vector2(44, -500), "地图选择", Body);
        view.settingsButton = Button(panel, "Settings", new Vector2(44, -570), "设置", Body);
        view.quitButton = Button(panel, "Quit", new Vector2(44, -640), "退出游戏", Body);
        Footer(panel, "系统在线  ·  防线状态稳定");
    }

    private static void BuildSettings(GameStartMenuView view, Transform panel)
    {
        Text(panel, "系统配置  //  音频与显示", new Vector2(44, -76), new Vector2(-44, -38), 18, Amber);
        Text(panel, "设置", new Vector2(40, -188), new Vector2(-40, -76), 72, Color.white);
        Image(panel, "Divider", new Vector2(0, 1), new Vector2(0, 1), new Vector2(44, -218), new Vector2(300, -214), null, Cyan);
        Text(panel, "主音量", new Vector2(44, -286), new Vector2(-44, -246), 24, Body);
        view.volumeSlider = Slider(panel, new Vector2(44, -326));
        view.fullscreenToggle = Toggle(panel, new Vector2(44, -418), "全屏模式");
        view.backButton = Button(panel, "Back", new Vector2(44, -536), "返回", Cyan);
        Footer(panel, "变更会立即生效");
    }

    private static void BuildMapSelection(GameStartMenuView view, Transform panel)
    {
        Text(panel, "MAP SELECT  //  BUILD SETTINGS", new Vector2(44, -76), new Vector2(-44, -38), 18, Amber);
        Text(panel, "地图选择", new Vector2(40, -188), new Vector2(-40, -76), 72, Color.white);
        Image(panel, "Divider", new Vector2(0, 1), new Vector2(0, 1), new Vector2(44, -218), new Vector2(300, -214), null, Cyan);
        view.mapStatusLabel = Text(panel, "选择作战地图", new Vector2(44, -266), new Vector2(-44, -224), 22, Body);
        GameObject list = Obj(panel, "Map List");
        RectTransform listRect = list.GetComponent<RectTransform>();
        listRect.anchorMin = new Vector2(0, 1);
        listRect.anchorMax = new Vector2(1, 1);
        listRect.pivot = new Vector2(.5f, 1);
        listRect.offsetMin = new Vector2(44, -520);
        listRect.offsetMax = new Vector2(-44, -300);
        VerticalLayoutGroup layout = list.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 10f;
        layout.childControlWidth = true;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        view.mapListContainer = list.transform;
        view.mapBackButton = Button(panel, "Map Back", new Vector2(44, -584), "返回", Cyan);
        Footer(panel, "地图来自 Unity Build Settings");
    }

    private void RefreshMaps(IReadOnlyList<MapSceneCatalog.Entry> maps, int selectedBuildIndex, bool loading)
    {
        for (int i = 0; i < mapButtons.Count; i++)
            if (mapButtons[i] != null) Destroy(mapButtons[i].gameObject);
        mapButtons.Clear();

        if (mapStatusLabel != null) mapStatusLabel.text = loading ? "正在加载地图..." : "选择作战地图";
        if (maps == null || maps.Count == 0)
        {
            if (mapStatusLabel != null) mapStatusLabel.text = "Build Settings 中没有可用地图";
            return;
        }

        for (int i = 0; i < maps.Count; i++)
        {
            MapSceneCatalog.Entry entry = maps[i];
            Button button = MapButton(mapListContainer, entry.DisplayName, entry.BuildIndex == selectedBuildIndex, loading);
            button.name = "Map Option " + entry.BuildIndex;
            int buildIndex = entry.BuildIndex;
            button.onClick.AddListener(() => MapSelectionRequested?.Invoke(buildIndex));
            mapButtons.Add(button);
        }
    }

    private static Button MapButton(Transform parent, string label, bool selected, bool loading)
    {
        GameObject go = Obj(parent, "Map Option");
        Image image = go.AddComponent<Image>();
        image.color = selected ? new Color(.10f, .34f, .42f, .98f) : ButtonColor;
        Button button = go.AddComponent<Button>();
        button.targetGraphic = image;
        button.interactable = !loading;
        LayoutElement element = go.AddComponent<LayoutElement>();
        element.preferredHeight = 58f;
        Text(go.transform, (selected ? ">  " : string.Empty) + label,
            new Vector2(25, 0), new Vector2(-18, 0), 25, Color.white,
            TextAlignmentOptions.Left, Vector2.zero);
        return button;
    }

    private static GameObject PanelObject(Transform parent, string name)
    {
        GameObject panel = Obj(parent, name);
        RectTransform r = panel.GetComponent<RectTransform>();
        r.anchorMin = r.anchorMax = new Vector2(0, .5f);
        r.pivot = new Vector2(0, .5f);
        r.anchoredPosition = new Vector2(112, 0);
        r.sizeDelta = new Vector2(590, 690);
        panel.AddComponent<Image>().color = Panel;
        return panel;
    }

    private static Button Button(Transform parent, string name, Vector2 pos, string label, Color accent)
    {
        GameObject go = Obj(parent, name);
        RectTransform r = go.GetComponent<RectTransform>();
        r.anchorMin = r.anchorMax = new Vector2(0, 1);
        r.pivot = new Vector2(0, 1);
        r.anchoredPosition = pos;
        r.sizeDelta = new Vector2(502, 58);
        Image image = go.AddComponent<Image>(); image.color = ButtonColor;
        Button button = go.AddComponent<Button>(); button.targetGraphic = image;
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(.45f, .92f, 1, 1);
        colors.pressedColor = new Color(.22f, .58f, .70f, 1);
        colors.selectedColor = colors.highlightedColor;
        colors.fadeDuration = .08f;
        button.colors = colors;
        Image(go.transform, "Accent", new Vector2(0, 0), new Vector2(0, 1), Vector2.zero, new Vector2(5, 0), null, accent);
        Text(go.transform, label, new Vector2(25, 0), new Vector2(-18, 0), 25, Color.white, TextAlignmentOptions.Left, Vector2.zero);
        return button;
    }

    private static Slider Slider(Transform parent, Vector2 pos)
    {
        GameObject root = Obj(parent, "Master Volume");
        RectTransform r = root.GetComponent<RectTransform>();
        r.anchorMin = r.anchorMax = new Vector2(0, 1); r.pivot = new Vector2(0, 1); r.anchoredPosition = pos; r.sizeDelta = new Vector2(502, 40);
        Slider slider = root.AddComponent<Slider>(); slider.minValue = 0; slider.maxValue = 1; slider.value = 1;
        Image sliderBackground = Image(root.transform, "Background", new Vector2(0, .5f), new Vector2(1, .5f), new Vector2(0, -4), new Vector2(0, 4), null, new Color(.01f, .03f, .05f, .95f));
        sliderBackground.raycastTarget = true;
        GameObject fillArea = Obj(root.transform, "Fill Area");
        RectTransform fr = fillArea.GetComponent<RectTransform>(); fr.anchorMin = Vector2.zero; fr.anchorMax = Vector2.one; fr.offsetMin = new Vector2(10, 0); fr.offsetMax = new Vector2(-10, 0);
        slider.fillRect = Image(fillArea.transform, "Fill", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, null, Cyan).rectTransform;
        GameObject handleArea = Obj(root.transform, "Handle Slide Area");
        RectTransform hr = handleArea.GetComponent<RectTransform>(); hr.anchorMin = Vector2.zero; hr.anchorMax = Vector2.one; hr.offsetMin = new Vector2(10, 0); hr.offsetMax = new Vector2(-10, 0);
        Image handle = Image(handleArea.transform, "Handle", new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(-10, -13), new Vector2(10, 13), null, Color.white);
        handle.raycastTarget = true;
        slider.handleRect = handle.rectTransform; slider.targetGraphic = handle;
        return slider;
    }

    private static Toggle Toggle(Transform parent, Vector2 pos, string label)
    {
        GameObject root = Obj(parent, "Fullscreen");
        RectTransform r = root.GetComponent<RectTransform>();
        r.anchorMin = r.anchorMax = new Vector2(0, 1); r.pivot = new Vector2(0, 1); r.anchoredPosition = pos; r.sizeDelta = new Vector2(502, 46);
        Toggle toggle = root.AddComponent<Toggle>();
        Image box = Image(root.transform, "Box", new Vector2(0, .5f), new Vector2(0, .5f), new Vector2(0, -15), new Vector2(30, 15), null, ButtonColor);
        box.raycastTarget = true;
        toggle.targetGraphic = box;
        toggle.graphic = Image(box.transform, "Checkmark", new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(-8, -8), new Vector2(8, 8), null, Cyan);
        Text(root.transform, label, new Vector2(48, 0), Vector2.zero, 24, Body, TextAlignmentOptions.Left, Vector2.zero);
        return toggle;
    }

    private static void Footer(Transform panel, string content) => Text(panel, content, new Vector2(44, 38), new Vector2(-44, 72), 16, new Color(.55f, .72f, .78f, .95f), TextAlignmentOptions.Left, Vector2.zero, new Vector2(1, 0));

    private static Image Image(Transform p, string n, Vector2 amin, Vector2 amax, Vector2 omin, Vector2 omax, Sprite s, Color c)
    {
        GameObject go = Obj(p, n);
        RectTransform r = go.GetComponent<RectTransform>(); r.anchorMin = amin; r.anchorMax = amax; r.offsetMin = omin; r.offsetMax = omax;
        Image i = go.AddComponent<Image>(); i.sprite = s; i.color = c; i.raycastTarget = false; return i;
    }

    private static TextMeshProUGUI Text(Transform p, string text, Vector2 omin, Vector2 omax, float size, Color color,
        TextAlignmentOptions align = TextAlignmentOptions.Left, Vector2? amin = null, Vector2? amax = null)
    {
        GameObject go = Obj(p, "Text");
        RectTransform r = go.GetComponent<RectTransform>(); r.anchorMin = amin ?? new Vector2(0, 1); r.anchorMax = amax ?? new Vector2(1, 1); r.offsetMin = omin; r.offsetMax = omax;
        TextMeshProUGUI t = go.AddComponent<TextMeshProUGUI>(); t.font = LoadMenuFont(); t.text = text; t.fontSize = size; t.color = color; t.alignment = align; t.raycastTarget = false; return t;
    }

    private static TMP_FontAsset LoadMenuFont()
    {
        if (menuFont == null) menuFont = Resources.Load<TMP_FontAsset>(MenuFontResourcePath);
        if (menuFont != null) return menuFont;
        Debug.LogError("Missing Chinese menu font. Run the menu authoring tool to generate it.");
        return TMP_Settings.defaultFontAsset;
    }

    private static GameObject Obj(Transform parent, string name) { GameObject go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false); return go; }
    private static T Add<T>(GameObject go) where T : Component { T value = go.GetComponent<T>(); return value != null ? value : go.AddComponent<T>(); }
    private static Sprite LoadRuntimeBackground()
    {
        Texture2D texture = Resources.Load<Texture2D>("UI/MainMenuBackground");
        return texture == null ? null : Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(.5f, .5f));
    }

    private static void EnsureEventSystem()
    {
        EventSystem system = FindAnyObjectByType<EventSystem>();
        if (system == null) system = new GameObject("EventSystem", typeof(EventSystem)).GetComponent<EventSystem>();
        if (system.GetComponent<InputSystemUIInputModule>() == null) system.gameObject.AddComponent<InputSystemUIInputModule>();
    }
}
