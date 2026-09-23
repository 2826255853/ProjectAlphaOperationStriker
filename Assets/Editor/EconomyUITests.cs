#if UNITY_INCLUDE_TESTS
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public sealed class EconomyUITests
{
    [Test]
    public void ViewTracksRewardsPurchasesAndRunResetWithoutDuplicateSubscriptions()
    {
        EconomyManager wallet = EconomyManager.GetOrCreate();
        using (var view = new EconomyUIState())
        {
            try
            {
                wallet.BeginRun(100);
                int before = ListenerCount(wallet);
                view.Enable();
                view.Enable();
                view.Refresh();
                Assert.That(ListenerCount(wallet), Is.EqualTo(before + 1));
                Assert.That(view.BalanceText, Is.EqualTo("金币：100"));
                wallet.Grant(10);
                Assert.That(view.Balance, Is.EqualTo(110));
                wallet.TrySpend(40);
                Assert.That(view.Balance, Is.EqualTo(70));
                wallet.BeginRun(25);
                Assert.That(view.Balance, Is.EqualTo(25));
                Assert.That(ListenerCount(wallet), Is.EqualTo(before + 1));
                view.Dispose();
                Assert.That(ListenerCount(wallet), Is.EqualTo(before));
                wallet.Grant(10);
                view.Enable();
                Assert.That(view.Balance, Is.EqualTo(35));
                Assert.That(ListenerCount(wallet), Is.EqualTo(before + 1));
            }
            finally { Object.DestroyImmediate(wallet.gameObject); }
        }
    }

    [Test]
    public void ReplacedWalletUnsubscribesOldWalletAndRebindsToNewRun()
    {
        EconomyManager oldWallet = EconomyManager.GetOrCreate();
        var newHost = new GameObject("Replacement wallet");
        newHost.SetActive(false);
        var replacement = newHost.AddComponent<EconomyManager>();
        using (var view = new EconomyUIState())
        {
            try
            {
                view.Enable();
                int oldCount = ListenerCount(oldWallet);
                replacement.BeginRun(12);
                typeof(EconomyManager).GetProperty("Instance").SetValue(null, replacement);
                view.Refresh();
                Assert.That(ListenerCount(oldWallet), Is.EqualTo(oldCount - 1));
                Assert.That(view.Balance, Is.EqualTo(12));
                oldWallet.Grant(10);
                Assert.That(view.Balance, Is.EqualTo(12));
                replacement.Grant(3);
                Assert.That(view.Balance, Is.EqualTo(15));
            }
            finally
            {
                Object.DestroyImmediate(oldWallet.gameObject);
                Object.DestroyImmediate(newHost);
            }
        }
    }

    [Test]
    public void MissingWalletAndDisabledDomainReloadDoNotLeaveStaleBalance()
    {
        EconomyManager wallet = EconomyManager.GetOrCreate();
        using (var view = new EconomyUIState())
        {
            try
            {
                view.Enable();
                InvokeStatic(typeof(EconomyManager), "ResetRuntimeState");
                view.Refresh();
                Assert.That(view.BalanceText, Is.EqualTo("金币：--"));
                Invoke(wallet, "Awake");
                view.Refresh();
                Assert.That(ListenerCount(wallet), Is.EqualTo(1));
                wallet.TrySpend(40);
                Assert.That(view.Balance, Is.EqualTo(60));
            }
            finally { Object.DestroyImmediate(wallet.gameObject); }
        }
    }

    [TestCase(39, 40, false, "金币不足，还差 1")]
    [TestCase(40, 40, true, "")]
    [TestCase(0, 0, true, "")]
    [TestCase(100, -1, false, "价格配置无效")]
    public void PriceHintsMatchPurchaseBoundaries(int balance, int cost, bool affordable, string hint)
    {
        EconomyManager wallet = EconomyManager.GetOrCreate();
        var definition = ScriptableObject.CreateInstance<TrapDefinition>();
        using (var view = new EconomyUIState())
        {
            try
            {
                wallet.BeginRun(balance);
                Set(definition, "cost", cost);
                view.Enable();
                Assert.That(view.CanAfford(definition), Is.EqualTo(affordable));
                Assert.That(view.PurchaseHint(definition), Is.EqualTo(hint));
                Assert.That(wallet.Balance, Is.EqualTo(balance), "Inspecting prices never spends money.");
            }
            finally
            {
                Object.DestroyImmediate(definition);
                Object.DestroyImmediate(wallet.gameObject);
            }
        }
    }

    [UnityTest]
    public IEnumerator RuntimeFeedbackTracksBalanceAndSceneReplacement()
    {
        yield return new EnterPlayMode();
        yield return VerifyRuntimeFeedback();
    }

    private static IEnumerator VerifyRuntimeFeedback()
    {
        var objects = new List<Object>();
        GameObject Host(string name)
        {
            var host = new GameObject(name);
            objects.Add(host);
            return host;
        }
        var originalScene = SceneManager.GetActiveScene();
        Scene walletScene = default;
        Scene nextScene = default;
        var definition = ScriptableObject.CreateInstance<TrapDefinition>();
        objects.Add(definition);
        var controller = Object.FindAnyObjectByType<TrapPlacementController>();
        var menu = Object.FindAnyObjectByType<TrapSelectionMenu>();
        var hud = Object.FindAnyObjectByType<EconomyHUD>();
        try
        {
            Assert.That(controller != null && menu != null && hud != null, Is.True);
            EconomyManager wallet = EconomyManager.GetOrCreate();
            wallet.BeginRun(39);
            Set(definition, "displayName", "测试陷阱");
            Set(definition, "cost", 40);
            var grid = Host("Economy UI test grid").AddComponent<TrapPlacementGrid>();
            grid.ConfigureLayout(3, 1, 2f, 0f, new[] { true, true, true });
            var camera = Host("Economy UI preview camera").AddComponent<Camera>();
            camera.transform.position = new Vector3(0f, 7f, -9f);
            camera.transform.LookAt(Vector3.zero);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.12f, 0.16f, 0.21f);
            camera.depth = 100;
            Set(controller, "placementCamera", camera);
            Set(controller, "grid", grid);
            Set(controller, "autoDiscoverGrids", false);
            controller.AssignTrapToSlot(0, definition);
            controller.SelectTrapSlot(0);
            Invoke(controller, "SetPlacementMode", true);
            Set(controller, "activeGrid", grid);
            Set(controller, "hasHoveredCell", true);
            Set(controller, "hoveredCell", Vector2Int.zero);
            Invoke(controller, "EnsurePreview");
            Invoke(controller, "UpdatePreviewColor");
            Assert.That(hud.BalanceText, Is.EqualTo("金币：39"));
            Assert.That(menu.BalanceText, Is.EqualTo(hud.BalanceText));
            Assert.That(controller.HasValidPreview, Is.False);
            Assert.That(controller.PurchaseHint, Does.Contain("还差 1"));
            var preview = (GameObject)Get(controller, "preview");
            Color invalid = preview.GetComponent<Renderer>().sharedMaterial.color;
            Assert.That(invalid.r, Is.GreaterThan(invalid.g));
            Assert.That((bool)Invoke(controller, "PlaceAt", Vector2Int.zero), Is.False);
            Assert.That(controller.LastPlacementFailure, Does.Contain("还差 1"));
            Assert.That(wallet.Balance, Is.EqualTo(39));
            Cursor.lockState = CursorLockMode.None;
            Mouse.current?.WarpCursorPosition(camera.WorldToScreenPoint(new Vector3(0.1f, 0f, 0.1f)));
            yield return CapturePreview("placement-insufficient.png");

            wallet.Grant(1);
            Set(controller, "hasHoveredCell", true);
            Set(controller, "hoveredCell", Vector2Int.zero);
            Invoke(controller, "UpdatePreviewColor");
            Assert.That(controller.HasValidPreview, Is.True);
            Color valid = preview.GetComponent<Renderer>().sharedMaterial.color;
            Assert.That(valid.g, Is.GreaterThan(valid.r));
            Assert.That(controller.PurchaseHint, Is.Empty);
            Assert.That(hud.BalanceText, Is.EqualTo("金币：40"));
            yield return CapturePreview("placement-affordable.png");
            Assert.That((bool)Invoke(controller, "PlaceAt", Vector2Int.zero), Is.True);
            Assert.That(hud.BalanceText, Is.EqualTo("金币：0"));
            Assert.That(menu.BalanceText, Is.EqualTo(hud.BalanceText));

            Invoke(menu, "SetOpen", true);
            Assert.That(TrapSelectionMenu.CursorOwned, Is.True);
            Assert.That(Cursor.lockState, Is.EqualTo(CursorLockMode.None));
            // Show a controlled price in the real menu without changing any definition asset.
            var available = (List<TrapDefinition>)Get(menu, "available");
            available.Insert(0, definition);
            Set(menu, "styledCellSize", -1f);
            Assert.That(controller.AssignTrapToSlot(1, definition), Is.True);
            Assert.That(wallet.Balance, Is.Zero);
            yield return CapturePreview("menu-insufficient.png");
            Invoke(menu, "SetOpen", false);
            Assert.That(TrapSelectionMenu.CursorOwned, Is.True, "The close frame must still own the cursor.");
            int count = ListenerCount(wallet);
            hud.enabled = false;
            menu.enabled = false;
            Assert.That(ListenerCount(wallet), Is.EqualTo(count - 2));
            hud.enabled = true;
            menu.enabled = true;
            Assert.That(ListenerCount(wallet), Is.EqualTo(count));
            wallet.BeginRun(55);
            Assert.That(hud.BalanceText, Is.EqualTo("金币：55"));
            Assert.That(menu.BalanceText, Is.EqualTo(hud.BalanceText));

            // Unload the actual wallet scene while the persistent UI survives.
            walletScene = SceneManager.CreateScene("Economy UI old wallet scene");
            SceneManager.MoveGameObjectToScene(wallet.gameObject, walletScene);
            yield return SceneManager.UnloadSceneAsync(walletScene);
            yield return null;
            Assert.That(hud.BalanceText, Is.EqualTo("金币：--"));
            nextScene = SceneManager.CreateScene("Economy UI next wallet scene");
            SceneManager.SetActiveScene(nextScene);
            EconomyManager replacement = EconomyManager.GetOrCreate();
            replacement.BeginRun(23);
            yield return null;
            Assert.That(hud.BalanceText, Is.EqualTo("金币：23"));
            Assert.That(menu.BalanceText, Is.EqualTo(hud.BalanceText));
            replacement.Grant(7);
            Assert.That(hud.BalanceText, Is.EqualTo("金币：30"));
            Assert.That(menu.BalanceText, Is.EqualTo(hud.BalanceText));
        }
        finally
        {
            if (menu != null) Invoke(menu, "SetOpen", false);
            if (controller != null) Invoke(controller, "SetPlacementMode", false);
            if (originalScene.IsValid() && originalScene.isLoaded) SceneManager.SetActiveScene(originalScene);
            for (int i = objects.Count - 1; i >= 0; i--)
                if (objects[i] != null) Object.Destroy(objects[i]);
        }
        if (walletScene.IsValid() && walletScene.isLoaded) yield return SceneManager.UnloadSceneAsync(walletScene);
        if (nextScene.IsValid() && nextScene.isLoaded) yield return SceneManager.UnloadSceneAsync(nextScene);
        yield return new ExitPlayMode();
    }

    private static IEnumerator CapturePreview(string filename)
    {
        string directory = Environment.GetEnvironmentVariable("ALPHA_ECONOMY_PREVIEW_DIR");
        if (string.IsNullOrEmpty(directory)) yield break;
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, filename);
        yield return null;
        // Batch Mode does not run the normal screen-present/screenshot path.
        // Render the real Game View cameras and IMGUI into its offscreen target instead.
        Type gameViewType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
        EditorWindow gameView = EditorWindow.GetWindow(gameViewType);
        MethodInfo render = null;
        for (Type type = gameViewType; type != null && render == null; type = type.BaseType)
            render = Array.Find(type.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public
                | BindingFlags.DeclaredOnly), method => method.Name == "RenderView");
        Assert.That(render, Is.Not.Null, "Unity must provide the Game View render method.");
        PropertyInfo targetSize = render.DeclaringType.GetProperty("targetSize", BindingFlags.Instance
            | BindingFlags.Public | BindingFlags.NonPublic);
        PropertyInfo renderIMGUI = render.DeclaringType.GetProperty("renderIMGUI", BindingFlags.Instance
            | BindingFlags.Public | BindingFlags.NonPublic);
        object oldSize = targetSize.GetValue(gameView);
        object oldIMGUI = renderIMGUI.GetValue(gameView);
        object gameSize = gameViewType.GetProperty("currentGameViewSize", BindingFlags.Instance
            | BindingFlags.NonPublic).GetValue(gameView);
        PropertyInfo width = gameSize.GetType().GetProperty("width");
        PropertyInfo height = gameSize.GetType().GetProperty("height");
        PropertyInfo sizeType = gameSize.GetType().GetProperty("sizeType");
        object oldWidth = width.GetValue(gameSize), oldHeight = height.GetValue(gameSize);
        object oldSizeType = sizeType.GetValue(gameSize);
        // Change only the in-memory size while capturing; do not save editor preferences.
        width.SetValue(gameSize, 1280);
        height.SetValue(gameSize, 720);
        sizeType.SetValue(gameSize, Enum.ToObject(sizeType.PropertyType, 1));
        targetSize.SetValue(gameView, new Vector2(1280f, 720f));
        renderIMGUI.SetValue(gameView, true);
        ParameterInfo[] parameters = render.GetParameters();
        var arguments = new object[parameters.Length];
        for (int i = 0; i < parameters.Length; i++)
        {
            Type type = parameters[i].ParameterType;
            if (type == typeof(Vector2)) arguments[i] = parameters[i].Name.ToLowerInvariant().Contains("mouse")
                ? Vector2.zero : new Vector2(1280f, 720f);
            else if (type == typeof(bool)) arguments[i] = false;
            else if (type == typeof(int)) arguments[i] = 0;
            else throw new NotSupportedException("Game View render parameter: " + parameters[i]);
        }
        Event previousEvent = Event.current;
        RenderTexture rendered;
        try
        {
            Event.current = new Event { type = EventType.Repaint };
            rendered = render.Invoke(gameView, arguments) as RenderTexture;
        }
        finally
        {
            Event.current = previousEvent;
        }
        // SRP renders on the following editor player-loop ticks, not inside RenderView.
        for (int frame = 0; frame < 10; frame++)
        {
            gameView.Repaint();
            EditorApplication.QueuePlayerLoopUpdate();
            yield return null;
        }
        rendered = render.Invoke(gameView, arguments) as RenderTexture;
        Assert.That(rendered != null, Is.True, "Game View must return its rendered target.");
        Assert.That(rendered.width, Is.EqualTo(1280));
        Assert.That(rendered.height, Is.EqualTo(720));
        RenderTexture previous = RenderTexture.active;
        var screenshot = new Texture2D(rendered.width, rendered.height, TextureFormat.RGB24, false);
        try
        {
            RenderTexture.active = rendered;
            screenshot.ReadPixels(new Rect(0f, 0f, rendered.width, rendered.height), 0, 0);
            // Game View targets use the graphics API's top-down projection on D3D.
            if (SystemInfo.graphicsUVStartsAtTop)
            {
                Color32[] pixels = screenshot.GetPixels32();
                var flipped = new Color32[pixels.Length];
                for (int y = 0; y < rendered.height; y++)
                    Array.Copy(pixels, y * rendered.width, flipped,
                        (rendered.height - y - 1) * rendered.width, rendered.width);
                screenshot.SetPixels32(flipped);
            }
            screenshot.Apply();
            var distinctColors = new HashSet<Color32>();
            Color32[] captured = screenshot.GetPixels32();
            for (int i = 0; i < captured.Length; i += 113) distinctColors.Add(captured[i]);
            Assert.That(distinctColors.Count, Is.GreaterThan(10), "Reject an unrendered black frame.");
            File.WriteAllBytes(path, screenshot.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previous;
            Object.Destroy(screenshot);
            width.SetValue(gameSize, oldWidth);
            height.SetValue(gameSize, oldHeight);
            sizeType.SetValue(gameSize, oldSizeType);
            targetSize.SetValue(gameView, oldSize);
            renderIMGUI.SetValue(gameView, oldIMGUI);
        }
        Assert.That(File.Exists(path), Is.True, "The rendered preview must be saved.");
    }

    private static int ListenerCount(EconomyManager wallet) =>
        ((Delegate)typeof(EconomyManager).GetField("BalanceChanged", BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(wallet))?.GetInvocationList().Length ?? 0;
    private static object Get(object target, string name) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
    private static void Set(object target, string name, object value) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    private static object Invoke(object target, string name, params object[] args) =>
        target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);
    private static void InvokeStatic(Type type, string name) =>
        type.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
}
#endif
