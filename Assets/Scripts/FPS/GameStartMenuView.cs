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
    public const string MapPrompt = "点击地图进入游戏";
    public const string MapLoadingMessage = "正在加载地图...";
    public const string EmptyMapMessage = "没有可用地图";
    public const string PauseTitle = "游戏已暂停";
    public const string PauseDescription = "按 Esc 或选择继续游戏返回战斗。";
    public const string PauseQuitLabel = "退出游戏（返回主菜单）";
    public const string PauseFooter = "退出后返回主菜单，本局进度将结束";
    private static TMP_FontAsset menuFont;
    private static readonly Color Panel = new Color32(18, 25, 29, 244);
    private static readonly Color ButtonColor = new Color32(49, 61, 66, 255);
    private static readonly Color Amber = new Color32(255, 190, 68, 255);
    private static readonly Color Cyan = new Color32(152, 217, 210, 255);
    private static readonly Color Body = new Color(.80f, .90f, .95f, 1f);

    [SerializeField] private GameObject mainPanel;
    [SerializeField] private GameObject settingsPanel;
    [SerializeField] private GameObject mapPanel;
    [SerializeField] private TextMeshProUGUI primaryLabel;
    [SerializeField] private Button primaryButton;
    [SerializeField] private Button settingsButton;
    [SerializeField] private Button quitButton;
    [SerializeField] private TextMeshProUGUI quitLabel;
    [SerializeField] private Button backButton;
    [SerializeField] private Button mapBackButton;
    [SerializeField] private Slider volumeSlider;
    [SerializeField] private Toggle fullscreenToggle;
    [SerializeField] private Transform mapListContainer;
    [SerializeField] private TextMeshProUGUI mapStatusLabel;
    [SerializeField] private Image backdrop;
    [SerializeField] private Image darkening;
    [SerializeField] private GameObject masthead;
    [SerializeField] private TextMeshProUGUI mainTitle;
    [SerializeField] private TextMeshProUGUI mainDescription;
    [SerializeField] private TextMeshProUGUI mainFooter;
    [SerializeField] private TextMeshProUGUI settingsTitle;
    [SerializeField] private GameObject statisticsPanel;
    [SerializeField] private TextMeshProUGUI[] statisticValues;
    [SerializeField] private TextMeshProUGUI pageLabel;
    [SerializeField] private TextMeshProUGUI volumeValue;
    [SerializeField] private TextMeshProUGUI strategicResourcesValue;
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
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        Add<GraphicRaycaster>(root);
        GameStartMenuView view = Add<GameStartMenuView>(root);

        Image bg = Image(root.transform, "Tactical Backdrop", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, background, Color.white);
        view.backdrop = bg;
        bg.preserveAspect = false;
        view.darkening = Image(root.transform, "Darkening", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, null, new Color(.005f, .015f, .028f, .44f));
        view.darkening.raycastTarget = true;
        if (background != null)
        {
            AspectRatioFitter crop = bg.gameObject.AddComponent<AspectRatioFitter>();
            crop.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            crop.aspectRatio = background.rect.width / background.rect.height;
        }
        view.masthead = BuildMasthead(root.transform, out view.pageLabel);

        view.mainPanel = PanelObject(root.transform, "Main Panel");
        BuildMain(view, view.mainPanel.transform);
        view.settingsPanel = PanelObject(root.transform, "Settings Panel");
        BuildSettings(view, view.settingsPanel.transform);
        view.mapPanel = PanelObject(root.transform, "Map Panel");
        BuildMapSelection(view, view.mapPanel.transform);
        BuildStatistics(view, root.transform);
        view.settingsPanel.SetActive(false);
        view.mapPanel.SetActive(false);
        return view;
    }

    public void Initialize(GameStartMenu owner)
    {
        if (primaryButton == null || mapPanel == null || pageLabel == null)
        {
            foreach (Transform child in transform)
            {
                child.gameObject.SetActive(false);
                Destroy(child.gameObject);
            }
            BuildLayout(gameObject, backdrop != null ? backdrop.sprite : LoadRuntimeBackground());
        }

        EnsureEventSystem();
        TMP_FontAsset font = LoadMenuFont();
        if (font != null)
            foreach (TextMeshProUGUI label in GetComponentsInChildren<TextMeshProUGUI>(true))
                label.font = font;
        if (initialized) return;
        initialized = true;
        primaryButton.onClick.AddListener(owner.BeginOrResume);
        settingsButton.onClick.AddListener(owner.OpenSettings);
        quitButton.onClick.AddListener(owner.QuitGame);
        backButton.onClick.AddListener(owner.CloseSettings);
        mapBackButton.onClick.AddListener(owner.CloseMapSelection);
        volumeSlider.onValueChanged.AddListener(owner.SetMasterVolume);
        volumeSlider.onValueChanged.AddListener(UpdateVolumeLabel);
        fullscreenToggle.onValueChanged.AddListener(owner.SetFullscreen);
        MapSelectionRequested += owner.SelectMap;
    }

    public void SetVisible(bool visible) => gameObject.SetActive(visible);

    public void SetState(bool started, bool showSettings, float volume, bool fullscreen)
    {
        SetState(started, showSettings, false, volume, fullscreen, false, MapSceneCatalog.GetMaps());
    }

    public void SetState(bool started, bool showSettings, bool showMaps, float volume, bool fullscreen,
        bool loading, IReadOnlyList<MapSceneCatalog.Entry> maps)
    {
        primaryLabel.text = started ? "继续游戏" : "开始游戏";
        primaryButton.interactable = !loading;
        settingsButton.interactable = !loading;
        quitButton.interactable = !loading;
        quitLabel.text = started ? PauseQuitLabel : "退出游戏";
        mainTitle.text = started ? PauseTitle : "行动打击";
        mainTitle.fontSize = started ? 69 : 118;
        mainDescription.text = started ? PauseDescription : "部署防御设施，守住边境前线。";
        mainFooter.text = loading ? MapLoadingMessage : PauseFooter;
        mainFooter.gameObject.SetActive(started);
        pageLabel.text = showMaps ? "大厅   /   地图选择" : showSettings ? "大厅   /   设置" : "作战大厅";
        settingsTitle.text = started ? PauseTitle + " · 设置" : "设置";
        settingsTitle.fontSize = started ? 46 : 65;
        backdrop.gameObject.SetActive(!started);
        masthead.SetActive(!started);
        darkening.color = new Color(.005f, .015f, .028f, started ? .28f : .14f);
        Color panelColor = new Color(Panel.r, Panel.g, Panel.b, started ? .72f : Panel.a);
        mainPanel.GetComponent<Image>().color = started ? panelColor : Color.clear;
        settingsPanel.GetComponent<Image>().color = panelColor;
        mainPanel.SetActive(!showSettings && !showMaps);
        settingsPanel.SetActive(showSettings);
        mapPanel.SetActive(showMaps);
        volumeSlider.SetValueWithoutNotify(Mathf.Clamp01(volume));
        UpdateVolumeLabel(volume);
        fullscreenToggle.SetIsOnWithoutNotify(fullscreen);
        statisticsPanel.SetActive(!started && !showSettings && !showMaps);
        if (statisticsPanel.activeSelf)
        {
            GameSaveSystem.Statistics statistics = GameSaveSystem.Current;
            double winRate = statistics.TotalGamesPlayed == 0 ? 0d
                : (double)statistics.GamesWon / statistics.TotalGamesPlayed * 100d;
            statisticValues[0].text = statistics.TotalGamesPlayed.ToString("N0");
            statisticValues[1].text = statistics.GamesWon.ToString("N0");
            statisticValues[2].text = statistics.GamesLost.ToString("N0");
            statisticValues[3].text = $"{winRate:F1}%";
            strategicResourcesValue.text = statistics.StrategicResources.ToString("N0");
        }
        if (showMaps) RefreshMaps(maps, loading);
    }

    private static GameObject BuildMasthead(Transform parent, out TextMeshProUGUI page)
    {
        Image bar = Image(parent, "Lobby Masthead", new Vector2(0, 1), Vector2.one,
            new Vector2(0, -98), Vector2.zero, null, Panel);
        for (int i = 0; i < 3; i++)
        {
            int x = 90 + i * 14;
            Image slash = Image(bar.transform, "Insignia", Vector2.zero, Vector2.zero,
                new Vector2(x, 35), new Vector2(x + 8, 63), null, Amber);
            slash.rectTransform.localRotation = Quaternion.Euler(0, 0, -25);
        }
        TextMeshProUGUI brand = Text(bar.transform, "ALPHA STRIKER", new Vector2(152, -96), new Vector2(-400, 0), 25, Color.white);
        brand.fontStyle = FontStyles.Bold;
        brand.characterSpacing = 5;
        page = Text(bar.transform, "作战大厅", new Vector2(600, -96), new Vector2(-88, 0), 23, Body, TextAlignmentOptions.Right);
        Image(bar.transform, "Hairline", Vector2.zero, Vector2.right, Vector2.zero, new Vector2(0, 1), null, ButtonColor);
        return bar.gameObject;
    }

    private static void BuildMain(GameStartMenuView view, Transform panel)
    {
        var series = Text(panel, "边 境 防 御 协 议", new Vector2(45, -119), new Vector2(-30, -77), 23, Amber);
        series.fontStyle = FontStyles.Bold;
        view.mainTitle = Text(panel, "行动打击", new Vector2(36, -275), new Vector2(-20, -105), 118, Color.white);
        view.mainTitle.fontStyle = FontStyles.Bold;
        view.mainDescription = Text(panel, "部署防御设施，守住边境前线。", new Vector2(45, -337), new Vector2(-20, -275), 27, Body);
        view.primaryButton = Button(panel, "Primary Action", new Vector2(44, -397), "开始游戏", Amber, 502, true);
        view.primaryLabel = view.primaryButton.GetComponentInChildren<TextMeshProUGUI>();
        view.settingsButton = Button(panel, "Settings", new Vector2(44, -489), "设置", Body, 162);
        view.quitButton = Button(panel, "Quit", new Vector2(218, -489), "退出游戏", Body, 328);
        view.quitLabel = view.quitButton.GetComponentInChildren<TextMeshProUGUI>();
        view.mainFooter = Footer(panel, PauseFooter);
        view.mainFooter.gameObject.SetActive(false);
    }

    private void UpdateVolumeLabel(float volume)
    {
        volumeValue.text = Mathf.RoundToInt(Mathf.Clamp01(volume) * 100) + "%";
    }

    private static void BuildSettings(GameStartMenuView view, Transform panel)
    {
        view.settingsTitle = Text(panel, "设置", new Vector2(40, -140), new Vector2(-40, -38), 65, Color.white);
        view.settingsTitle.fontStyle = FontStyles.Bold;
        Image(panel, "Rule", new Vector2(0, 1), Vector2.one, new Vector2(44, -174), new Vector2(-44, -172), null, ButtonColor);
        Text(panel, "主音量", new Vector2(44, -261), new Vector2(-150, -204), 28, Body);
        view.volumeValue = Text(panel, "100%", new Vector2(320, -261), new Vector2(-44, -204), 31, Amber, TextAlignmentOptions.Right);
        view.volumeSlider = Slider(panel, new Vector2(44, -281));
        view.fullscreenToggle = Toggle(panel, new Vector2(44, -375), "全屏模式");
        view.backButton = Button(panel, "Back", new Vector2(44, -507), "返回", Amber, 502, true);
        Footer(panel, "变更会立即生效");
    }

    private static void BuildStatistics(GameStartMenuView view, Transform parent)
    {
        view.statisticsPanel = Image(parent, "Statistics Panel", Vector2.zero, Vector2.right,
            new Vector2(84, 42), new Vector2(-84, 192), null, Panel).gameObject;
        Transform strip = view.statisticsPanel.transform;
        Image(strip, "Top Rule", new Vector2(0, 1), Vector2.one, new Vector2(0, -2), Vector2.zero, null, ButtonColor);
        Image(strip, "Accent", new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, -4), new Vector2(68, 0), null, Amber);
        Text(strip, "作战记录", new Vector2(32, -107), new Vector2(220, -42), 33, Color.white,
            TextAlignmentOptions.Left, Vector2.up, Vector2.up).fontStyle = FontStyles.Bold;
        string[] labels = { "已完成局数", "胜场", "败场", "胜率", "战略资源" };
        float[] columns = { .18f, .35f, .50f, .65f, .81f, 1f };
        view.statisticValues = new TextMeshProUGUI[4];
        for (int i = 0; i < labels.Length; i++)
        {
            Vector2 from = new Vector2(columns[i], 0);
            Vector2 to = new Vector2(columns[i + 1], 0);
            Image(strip, "Divider", from, from, new Vector2(0, 34), new Vector2(1, 116), null, ButtonColor);
            Text(strip, labels[i], new Vector2(30, 91), new Vector2(-12, 123), 20, Body, TextAlignmentOptions.Left, from, to);
            bool resource = i == labels.Length - 1;
            var value = Text(strip, i == 3 ? "0.0%" : "0", new Vector2(30, 20), new Vector2(-16, 90),
                46, resource ? Amber : Color.white, TextAlignmentOptions.Left, from, to);
            value.enableAutoSizing = true;
            value.fontSizeMin = 16;
            value.fontSizeMax = 46;
            value.textWrappingMode = TextWrappingModes.NoWrap;
            if (resource) view.strategicResourcesValue = value;
            else view.statisticValues[i] = value;
        }
    }

    private static void BuildMapSelection(GameStartMenuView view, Transform panel)
    {
        panel.GetComponent<RectTransform>().sizeDelta = new Vector2(990, 690);
        Text(panel, "地图选择", new Vector2(40, -140), new Vector2(-40, -38), 65, Color.white).fontStyle = FontStyles.Bold;
        view.mapStatusLabel = Text(panel, MapPrompt, new Vector2(44, -196), new Vector2(-44, -143), 24, Body);
        GameObject viewport = Obj(panel, "Map Viewport");
        RectTransform viewportRect = viewport.GetComponent<RectTransform>();
        viewportRect.anchorMin = Vector2.up;
        viewportRect.anchorMax = Vector2.one;
        viewportRect.offsetMin = new Vector2(44, -554);
        viewportRect.offsetMax = new Vector2(-44, -226);
        viewport.AddComponent<Image>().color = Color.clear;
        viewport.AddComponent<RectMask2D>();
        ScrollRect scroll = viewport.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 56;
        GameObject list = Obj(viewport.transform, "Map List");
        RectTransform listRect = list.GetComponent<RectTransform>();
        listRect.anchorMin = Vector2.up;
        listRect.anchorMax = Vector2.one;
        listRect.pivot = new Vector2(.5f, 1);
        listRect.sizeDelta = Vector2.zero;
        scroll.content = listRect;
        scroll.viewport = viewportRect;
        list.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        VerticalLayoutGroup layout = list.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 10f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        view.mapListContainer = list.transform;
        view.mapBackButton = Button(panel, "Map Back", new Vector2(44, -582), "返回", Body, 210);
    }

    private void RefreshMaps(IReadOnlyList<MapSceneCatalog.Entry> maps, bool loading)
    {
        for (int i = 0; i < mapButtons.Count; i++)
            if (mapButtons[i] != null)
            {
                mapButtons[i].gameObject.SetActive(false);
                if (Application.isPlaying) Destroy(mapButtons[i].gameObject);
                else DestroyImmediate(mapButtons[i].gameObject);
            }
        mapButtons.Clear();

        mapBackButton.interactable = !loading;
        mapStatusLabel.text = loading ? MapLoadingMessage : MapPrompt;
        if (maps == null || maps.Count == 0)
        {
            mapStatusLabel.text = EmptyMapMessage;
            return;
        }

        for (int i = 0; i < maps.Count; i++)
        {
            MapSceneCatalog.Entry entry = maps[i];
            Button button = MapButton(mapListContainer, entry, loading);
            button.name = "Map Option " + entry.BuildIndex;
            int buildIndex = entry.BuildIndex;
            button.onClick.AddListener(() => MapSelectionRequested?.Invoke(buildIndex));
            mapButtons.Add(button);
        }
    }

    private static Button MapButton(Transform parent, MapSceneCatalog.Entry entry, bool loading)
    {
        GameObject go = Obj(parent, "Map Option");
        Image image = go.AddComponent<Image>();
        image.color = Color.white;
        Button button = go.AddComponent<Button>();
        button.targetGraphic = image;
        ColorBlock colors = button.colors;
        colors.normalColor = ButtonColor;
        colors.highlightedColor = new Color32(74, 94, 101, 255);
        colors.selectedColor = colors.highlightedColor;
        colors.pressedColor = Panel;
        colors.disabledColor = Panel;
        button.colors = colors;
        button.interactable = !loading;
        LayoutElement element = go.AddComponent<LayoutElement>();
        bool factory = entry.Path == MapSceneCatalog.ThunderFactoryPath;
        element.preferredHeight = factory ? 221f : 110f;
        Image(go.transform, "Map Accent", Vector2.zero, Vector2.up, Vector2.zero, new Vector2(5, 0), null, Amber);
        float textEnd = factory ? .49f : 1f;
        var title = Text(go.transform, entry.DisplayName, new Vector2(28, 0), new Vector2(-18, factory ? 47 : 0),
            38, Color.white, TextAlignmentOptions.Left, Vector2.zero, new Vector2(textEnd, 1));
        title.enableAutoSizing = true;
        title.fontSizeMin = 18;
        title.fontSizeMax = 38;
        title.fontStyle = FontStyles.Bold;
        if (factory)
        {
            Text(go.transform, "双层设施  /  地空防守", new Vector2(28, 43), new Vector2(-8, 92), 21,
                Cyan, TextAlignmentOptions.Left, Vector2.zero, new Vector2(textEnd, 0));
            var art = Obj(go.transform, "Facility Plan").AddComponent<RawImage>();
            art.texture = Resources.Load<Texture2D>("UI/ThunderFactoryPlan");
            art.raycastTarget = false;
            art.rectTransform.anchorMin = new Vector2(.5f, 0);
            art.rectTransform.anchorMax = Vector2.one;
            art.rectTransform.offsetMin = Vector2.zero;
            art.rectTransform.offsetMax = Vector2.zero;
        }
        return button;
    }

    private static GameObject PanelObject(Transform parent, string name)
    {
        GameObject panel = Obj(parent, name);
        RectTransform r = panel.GetComponent<RectTransform>();
        r.anchorMin = r.anchorMax = new Vector2(0, .5f);
        r.pivot = new Vector2(0, .5f);
        r.anchoredPosition = new Vector2(44, 4);
        r.sizeDelta = new Vector2(590, 690);
        panel.AddComponent<Image>().color = Panel;
        return panel;
    }

    private static Button Button(Transform parent, string name, Vector2 pos, string label, Color accent, float width = 502, bool primary = false)
    {
        GameObject go = Obj(parent, name);
        RectTransform r = go.GetComponent<RectTransform>();
        r.anchorMin = r.anchorMax = new Vector2(0, 1);
        r.pivot = new Vector2(0, 1);
        r.anchoredPosition = pos;
        r.sizeDelta = new Vector2(width, primary ? 76 : 62);
        Image image = go.AddComponent<Image>(); image.color = ButtonColor;
        Button button = go.AddComponent<Button>(); button.targetGraphic = image;
        ColorBlock colors = button.colors;
        colors.highlightedColor = primary ? new Color32(255, 213, 118, 255) : new Color32(76, 96, 103, 255);
        colors.pressedColor = primary ? new Color32(233, 162, 54, 255) : new Color32(65, 80, 84, 255);
        colors.normalColor = primary ? Amber : ButtonColor;
        colors.selectedColor = colors.highlightedColor;
        colors.disabledColor = new Color32(100, 108, 110, 255);
        colors.fadeDuration = .10f;
        image.color = Color.white;
        button.colors = colors;
        var caption = Text(go.transform, label, new Vector2(24, 0), new Vector2(-18, 0), primary ? 32 : 24,
            primary ? Panel : accent, TextAlignmentOptions.Left, Vector2.zero);
        if (primary)
        {
            caption.fontStyle = FontStyles.Bold;
            Text(go.transform, "›", new Vector2(0, 0), new Vector2(-24, 4), 49, Panel, TextAlignmentOptions.Right, Vector2.zero);
        }
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
        slider.fillRect = Image(fillArea.transform, "Fill", Vector2.zero, Vector2.one, new Vector2(0, 16), new Vector2(0, -16), null, Amber).rectTransform;
        GameObject handleArea = Obj(root.transform, "Handle Slide Area");
        RectTransform hr = handleArea.GetComponent<RectTransform>(); hr.anchorMin = Vector2.zero; hr.anchorMax = Vector2.one; hr.offsetMin = new Vector2(10, 0); hr.offsetMax = new Vector2(-10, 0);
        Image handle = Image(handleArea.transform, "Handle", new Vector2(.5f, 0), new Vector2(.5f, 1), new Vector2(-8, 4), new Vector2(8, -4), null, Color.white);
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
        Image clickArea = root.AddComponent<Image>();
        clickArea.color = Color.clear;
        Image box = Image(root.transform, "Box", new Vector2(0, .5f), new Vector2(0, .5f), new Vector2(0, -15), new Vector2(30, 15), null, ButtonColor);
        box.raycastTarget = true;
        toggle.targetGraphic = box;
        toggle.graphic = Image(box.transform, "Checkmark", new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(-8, -8), new Vector2(8, 8), null, Cyan);
        Text(root.transform, label, new Vector2(48, 0), Vector2.zero, 24, Body, TextAlignmentOptions.Left, Vector2.zero);
        return toggle;
    }

    private static TextMeshProUGUI Footer(Transform panel, string content) => Text(panel, content, new Vector2(44, 18), new Vector2(-44, 50), 16, new Color(.55f, .72f, .78f, .95f), TextAlignmentOptions.Left, Vector2.zero, new Vector2(1, 0));

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
