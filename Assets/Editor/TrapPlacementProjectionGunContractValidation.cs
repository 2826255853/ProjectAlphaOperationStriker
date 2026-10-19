using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Batch-only checks for projection-gun plan steps 1-2; saves no assets.</summary>
public static class TrapPlacementProjectionGunContractValidation
{
    public const string PassMarker = "PROJECTION_GUN_STEPS_1_2_PASS";
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    public static void Run()
    {
        if (!Application.isBatchMode || Application.isPlaying)
            throw new InvalidOperationException("Run in a batch editor outside Play mode.");

        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var owned = new List<Object>();
        TrapPlacementController controller = null;
        bool trapMenuWasOpen = TrapSelectionMenu.IsOpen;
        bool startMenuWasOpen = GameStartMenu.CursorOwned;
        int checks = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            checks++;
        }
        T Own<T>(T value) where T : Object { owned.Add(value); return value; }

        try
        {
            SetStatic(typeof(TrapSelectionMenu), nameof(TrapSelectionMenu.IsOpen), false);
            SetStatic(typeof(GameStartMenu), nameof(GameStartMenu.CursorOwned), false);
            controller = Own(new GameObject("Projection gun contract")).AddComponent<TrapPlacementController>();
            // Do not depend on edit-mode MonoBehaviour lifecycle dispatch.
            Set(controller, "selectedTrap", null);
            Set(controller, "trapSlots", new TrapDefinition[4]);
            int modeChanges = 0;
            controller.PlacementModeChanged += _ => modeChanges++;
            Check(!controller.PlacementMode, "A fresh controller starts outside placement mode.");
            Check(!controller.OnProjectionGunPrimaryAction(), "Primary input is ignored outside the mode.");
            Invoke(controller, "SetPlacementMode", true);
            Check(controller.PlacementMode && TrapPlacementController.IsPlacementModeActive,
                "Instance and shooter state agree on entry.");
            Check(controller.IsProjectionGunInputActive, "Input is available without a grid or valid hover.");
            Invoke(controller, "SetPlacementMode", true);
            Check(modeChanges == 1, "Repeated entry does not publish a second mode transition.");

            var prefab = Own(new GameObject("Contract trap body"));
            var first = Own(ScriptableObject.CreateInstance<TrapDefinition>());
            var second = Own(ScriptableObject.CreateInstance<TrapDefinition>());
            Set(first, "prefab", prefab);
            Set(second, "prefab", prefab);
            Set(controller, "trapSlots", new[] { first, second, null, null });
            Check(controller.SelectTrapSlot(0), "The first configured slot can be selected.");
            ClearPreview(controller);
            Check(controller.SelectTrapSlot(1), "A second configured slot can be selected.");
            ClearPreview(controller);
            Check(controller.PlacementMode && controller.SelectedTrap == second && modeChanges == 1,
                "Switching traps preserves the mode without an exit/entry notification.");
            // The scene-only body above is only for preview checks. Purchases
            // use the existing primitive fallback instead of passing a scene
            // object to the editor-only PrefabUtility.InstantiatePrefab path.
            Set(second, "prefab", null);

            var wallet = EconomyManager.GetOrCreate();
            Own(wallet.gameObject);
            wallet.BeginRun(100);
            var grid = Own(new GameObject("Contract grid")).AddComponent<TrapPlacementGrid>();
            grid.ConfigureLayout(3, 1, 1f, 0f, new[] { true, true, true });
            Set(controller, "activeGrid", grid);
            Set(controller, "hasHoveredCell", true);
            Set(controller, "hoveredCell", Vector2Int.zero);
            bool purchased = controller.OnProjectionGunPrimaryAction();
            Check(purchased, "Primary input reaches the existing purchase service: " + controller.LastPlacementFailure);
            Check(wallet.Balance == 60 && grid.PlacedTraps.Count == 1,
                "A primary action creates one trap and charges its configured cost once.");
            Check(controller.PlacementMode && TrapPlacementController.IsPlacementModeActive && modeChanges == 1,
                "A successful purchase keeps placement mode active.");
            Check(!controller.OnProjectionGunPrimaryAction(), "An occupied cell still rejects placement.");
            Check(wallet.Balance == 60 && grid.PlacedTraps.Count == 1,
                "Rejected placement changes neither funds nor occupancy.");

            // Continue the original drag path; the third cell exceeds the remaining funds.
            Invoke(controller, "PlaceAlongLine", Vector2Int.zero, new Vector2Int(2, 0));
            Check(wallet.Balance == 20 && grid.PlacedTraps.Count == 2,
                "Dragging charges each successful cell and stops when funds run out.");
            Check(!(bool)Get(controller, "dragPlacement") && !(bool)Get(controller, "hasLastPlacedCell"),
                "Insufficient funds ends the drag stroke.");
            Check(controller.PlacementMode, "Finishing a drag stroke preserves placement mode.");

            grid.TryGetTrapAtCell(Vector2Int.zero, out TrapInstance existingTrap);
            Set(controller, "hoveredTrap", existingTrap);
            int secondaryActions = 0;
            controller.ProjectionGunSecondaryAction += () => secondaryActions++;
            string failure = controller.LastPlacementFailure;
            controller.OnProjectionGunSecondaryAction();
            Check(secondaryActions == 1, "Secondary input publishes the reserved event once.");
            Check(wallet.Balance == 20 && grid.PlacedTraps.Count == 2 && grid.OwnsTrap(existingTrap),
                "Secondary input neither dismantles nor sells a hovered trap.");
            Check(controller.PlacementMode && controller.LastPlacementFailure == failure,
                "Secondary input leaves mode and placement feedback unchanged.");

            SetStatic(typeof(TrapSelectionMenu), nameof(TrapSelectionMenu.IsOpen), true);
            controller.OnProjectionGunSecondaryAction();
            Check(!controller.IsProjectionGunInputActive && !controller.OnProjectionGunPrimaryAction()
                && secondaryActions == 1 && controller.PlacementMode,
                "The trap menu pauses both actions without exiting the mode.");
            SetStatic(typeof(TrapSelectionMenu), nameof(TrapSelectionMenu.IsOpen), false);
            SetStatic(typeof(GameStartMenu), nameof(GameStartMenu.CursorOwned), true);
            controller.OnProjectionGunSecondaryAction();
            Check(!controller.IsProjectionGunInputActive && !controller.OnProjectionGunPrimaryAction()
                && secondaryActions == 1, "The start menu also blocks forwarded actions.");
            SetStatic(typeof(GameStartMenu), nameof(GameStartMenu.CursorOwned), false);

            Invoke(controller, "SetPlacementMode", false);
            Check(!controller.PlacementMode && !TrapPlacementController.IsPlacementModeActive && modeChanges == 2,
                "Cancellation updates both state queries and publishes one exit.");
            controller.OnProjectionGunSecondaryAction();
            Check(secondaryActions == 1 && !controller.OnProjectionGunPrimaryAction(),
                "Both actions are ignored after cancellation.");

            // Avoid rebuilding visuals: only state/input teardown is under test here.
            Set(controller, "selectedTrap", null);
            ((List<TrapPlacementGrid>)Get(controller, "grids")).Clear();
            Object.DestroyImmediate(grid.gameObject);
            Invoke(controller, "SetPlacementMode", true);
            Set(controller, "dragPlacement", true);
            Set(controller, "hasLastPlacedCell", true);
            controller.enabled = false;
            Invoke(controller, "OnDisable");
            Check(!controller.PlacementMode && !TrapPlacementController.IsPlacementModeActive
                && !controller.IsProjectionGunInputActive, "Disable clears mode and blocks stale input.");
            Check(!(bool)Get(controller, "dragPlacement") && !(bool)Get(controller, "hasLastPlacedCell"),
                "Disable clears a partially completed drag.");
            controller.enabled = true;
            Invoke(controller, "OnEnable");
            Check(!controller.PlacementMode && !TrapPlacementController.IsPlacementModeActive,
                "Re-enabling starts outside placement mode.");
        }
        finally
        {
            SetStatic(typeof(TrapSelectionMenu), nameof(TrapSelectionMenu.IsOpen), trapMenuWasOpen);
            SetStatic(typeof(GameStartMenu), nameof(GameStartMenu.CursorOwned), startMenuWasOpen);
            if (controller != null)
            {
                ClearPreview(controller);
                Invoke(controller, "OnDisable");
                foreach (string field in new[] { "openMaterial", "blockedMaterial", "footprintMaterial",
                    "invalidFootprintMaterial", "hoveredMaterial" })
                {
                    var material = Get(controller, field) as Material;
                    if (material != null) Object.DestroyImmediate(material);
                }
            }
            for (int i = owned.Count - 1; i >= 0; i--)
                if (owned[i] != null) Object.DestroyImmediate(owned[i]);
        }

        Debug.Log($"{PassMarker}: {checks} assertions");
    }

    private static void ClearPreview(TrapPlacementController controller)
    {
        var preview = Get(controller, "preview") as GameObject;
        if (preview != null) Object.DestroyImmediate(preview);
        Set(controller, "preview", null);
    }

    private static object Get(object target, string field) =>
        target.GetType().GetField(field, PrivateInstance).GetValue(target);
    private static void Set(object target, string field, object value) =>
        target.GetType().GetField(field, PrivateInstance).SetValue(target, value);
    private static object Invoke(object target, string method, params object[] args) =>
        target.GetType().GetMethod(method, PrivateInstance).Invoke(target, args);
    private static void SetStatic(Type type, string property, bool value) =>
        type.GetProperty(property, BindingFlags.Public | BindingFlags.Static).SetValue(null, value);
}
