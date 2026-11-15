using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Assets.Behaviour.Flow;
using Assets.Behaviour.UI;
using Assets.Source.Flow;
using Assets.Source.Flow.Elements;
using Assets.Source.Items;
using Assets.Source.Player;
using Assets.Source.Util;
using BepInEx;
using HarmonyLib;
using KingdomFoundation.Api;
using KingdomIncUndo;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;

[BepInPlugin("urakka.kingdominc.undo.probe", "Undo Isolated Verification", "1.0.1")]
[BepInDependency(UndoPlugin.Id)]
public sealed class RuntimeProbe : BaseUnityPlugin
{
    private readonly List<string> checks = new List<string>();
    private Keyboard keyboard;
    private Mouse mouse;
    private UndoPlugin Plugin => UndoPlugin.Instance;
    private GamePlayer Player => GamePlayer.Current;
    private WorldFlowManager World => WorldFlowManager.Instance;

    private void Awake()
    {
        if (!Environment.GetCommandLineArgs().Contains("-undo-verify")) throw new InvalidOperationException("Isolated verification argument required.");
        keyboard = InputSystem.AddDevice<Keyboard>(); keyboard.MakeCurrent();
        mouse = InputSystem.AddDevice<Mouse>(); mouse.MakeCurrent();
        var harmony = new Harmony("urakka.kingdominc.undo.probe");
        foreach (string name in new[] { "Store", "DoSave", "StoreAutosaveState" })
            harmony.Patch(AccessTools.Method(typeof(SaveGame), name), prefix: new HarmonyMethod(typeof(RuntimeProbe), nameof(Skip)));
        harmony.Patch(AccessTools.Method(typeof(SaveGame), "LoadLatestSave"), prefix: new HarmonyMethod(typeof(RuntimeProbe), nameof(False)));
        harmony.Patch(AccessTools.Method(typeof(SteamManager), "Awake"), prefix: new HarmonyMethod(typeof(RuntimeProbe), nameof(Skip)));
        harmony.Patch(AccessTools.PropertyGetter(typeof(SteamManager), "Initialized"), prefix: new HarmonyMethod(typeof(RuntimeProbe), nameof(False)));
        // Batchmode has no foreground window. Exercise the actual input path under a controlled focus state.
        harmony.Patch(AccessTools.PropertyGetter(typeof(Application), "isFocused"), prefix: new HarmonyMethod(typeof(RuntimeProbe), nameof(True)));
        string isolated = Path.Combine(Paths.GameRootPath, "IsolatedSaves");
        Directory.CreateDirectory(isolated);
        SaveGame.SavesPath = isolated;
        AccessTools.Field(typeof(SaveGame), "SavesDir").SetValue(null, new DirectoryInfo(isolated));
        AccessTools.Field(typeof(SaveGame), "_saves").SetValue(null, null);
        Application.runInBackground = true;
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        StartCoroutine(Run());
    }

    private static bool Skip() => false;
    private static bool False(ref bool __result) { __result = false; return false; }
    private static bool True(ref bool __result) { __result = true; return false; }
    private void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
        checks.Add("PASS: " + description);
        Logger.LogInfo(checks[checks.Count - 1]);
    }
    private void Keys(params Key[] keys)
    {
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
        InputSystem.QueueStateEvent(mouse, new MouseState { position = new Vector2(-10000, -10000) });
        InputSystem.Update();
    }
    private void Edit(Action action)
    {
        bool active = Plugin.Begin();
        Exception error = null;
        try { action(); } catch (Exception ex) { error = ex; throw; }
        finally { if (active) Plugin.End(error); }
    }
    private long Gold => Player.GetCurrency(Currency.Gold);
    private Forge ForgeAt(int x, int y)
    {
        var forge = new Forge { Position = new Vector2Int(x, y) };
        forge.SetupRecipe("iron_ingot");
        World.CreateNewElement(forge);
        return forge;
    }
    private IEnumerator Run()
    {
        var steps = Verify();
        string error = null;
        while (true)
        {
            bool more = false;
            try { more = steps.MoveNext(); }
            catch (Exception ex) { error = ex.ToString(); }
            if (error != null || !more) break;
            yield return steps.Current;
        }
        File.WriteAllLines(Path.Combine(Paths.GameRootPath, "result.txt"), checks.Concat(new[] { error == null ? "ALL CHECKS PASSED" : "FAIL: " + error }));
        if (error != null) Logger.LogError(error);
        Application.Quit(error == null ? 0 : 1);
    }

    private IEnumerator Verify()
    {
        yield return new WaitForSecondsRealtime(3);
        Check(SaveGame.GetSaveGames().Count == 0 && !SaveGame.LoadLatestSave(), "Normal saves cannot be read or written");
        var player = GamePlayer.CreateNewPlayer();
        player.AddTech("element_stacking");
        player.SetUpgradeSlotCount("Forge", 1);
        foreach (Currency currency in Enum.GetValues(typeof(Currency))) player.AddCurrency(currency, 100000000, false);
        player.Flows.Add(new FlowDiagram { Label = "Second", IconName = "iron_ingot" });
        var secondForge = new Forge { Position = new Vector2Int(256, 256) };
        secondForge.SetupRecipe("iron_ingot");
        player.Flows[1].Elements.Add(secondForge);
        SceneManager.LoadScene("Game");
        yield return new WaitForSecondsRealtime(3);
        Time.timeScale = 0;
        TutorialUI.Instance.NewGameContent.transform.parent.gameObject.SetActive(false);
        Keys();
        Check(World && GameUI.Instance && EventSystem.current, "Native gameplay scene loaded");
        foreach (var method in Harmony.GetAllPatchedMethods().Where(method => Harmony.GetPatchInfo(method).Owners.Contains("urakka.kingdominc.foundation.execution-hooks")))
        {
            var patches = Harmony.GetPatchInfo(method);
            Check(patches != null && patches.Prefixes.Any(p => p.owner == "urakka.kingdominc.foundation.execution-hooks") && patches.Finalizers.Any(p => p.owner == "urakka.kingdominc.foundation.execution-hooks") && !patches.Owners.Contains(UndoPlugin.Id), "Foundation action hook: " + method.DeclaringType.Name + "." + method.Name);
        }

        var a = (Forge)player.ActiveFlow.Elements[0];
        var b = ForgeAt(512, 64);
        var c = ForgeAt(1024, 64);
        // Make a valid native connection between the selected buildings.
        var pipe = new FlowPipe(); pipe.Connect(a.Outputs[0], b.Inputs[0]);
        pipe.Nodes.Add(new Vector2Int(256, -192));
        World.CreateNewPipe(pipe);
        Plugin.Clear();

        var initial = a.Position;
        Edit(() => a.World.UpdatePosition(initial + new Vector2Int(0, 512)));
        a.Progress = 0.625f;
        player.AddCurrency(Currency.Gold, 137, false);
        long goldAfterIncome = Gold;
        Plugin.Replay(false);
        Check(a.Position == initial && a.Progress == 0.625f && Gold == goldAfterIncome, "Undo movement preserves production progress and later income");
        Check(a.World.Position == initial && a.World.gameObject.activeSelf, "Movement Undo rebuilds the native view");
        Plugin.Replay(true);
        Check(a.Position == initial + new Vector2Int(0, 512), "Redo movement");
        Plugin.Replay(false);
        Edit(() => a.World.UpdatePosition(initial + new Vector2Int(0, 256)));
        Check(Plugin.RedoCount == 0, "A new edit invalidates Redo");
        Plugin.Replay(false);
        Plugin.Clear();

        // Native multi-select area move uses a temporary parent decoration and its mover.
        var area = new FlowDeco { Position = new Vector2Int(-256, 256), Size = new Vector2Int(1536, 512), IsParent = true };
        var selection = World.ShowDeco(area);
        yield return null; // Let native colliders and decoration Start register.
        var mover = selection.GetComponentInChildren<WorldFlowDecoMover>();
        var beforeMove = new LayoutState(KingdomFoundation.Api.FoundationApi.Kingdom);
        var positions = player.ActiveFlow.Elements.ToDictionary(e => e, e => e.Position);
        var nodes = pipe.Nodes.ToArray();
        mover.StartDragDrop(false, true);
        var selected = mover.GetAnchoredParts().Item1.ToArray();
        Check(selected.Length == 3, "Native multi-selection contains all three buildings");
        Keys(Key.LeftCtrl);
        mover.OnDragDropMove(area.Position + new Vector2Int(0, -768), Vector2Int.zero);
        DragDropManager.Instance.ClearDragDrop();
        mover.OnDragDropEnd();
        Check(positions.All(p => p.Key.Position == p.Value + new Vector2Int(0, -768)), "Multi-select move moves every selected building");
        Check(Plugin.UndoCount == 1, "Multi-select move creates exactly one history action");
        Check(pipe.Nodes.SequenceEqual(nodes.Select(n => n + new Vector2Int(0, -768))), "Multi-select move includes internal pipe handles");
        var afterMove = new LayoutState(KingdomFoundation.Api.FoundationApi.Kingdom);
        Plugin.Replay(false);
        Check(beforeMove.SameLayout(new LayoutState(KingdomFoundation.Api.FoundationApi.Kingdom)), "One Undo restores the entire multi-select move and pipe route");
        Plugin.Replay(true);
        Check(afterMove.SameLayout(new LayoutState(KingdomFoundation.Api.FoundationApi.Kingdom)), "One Redo repeats the entire multi-select move");
        Plugin.Replay(false);
        yield return null;
        Plugin.Clear();
        Keys();

        // Native drag-to-deconstruct groups an anchored selection and its refund in one call.
        var container = new FlowDeco { Position = new Vector2Int(-256, 256), Size = new Vector2Int(1536, 512), IsParent = true };
        World.CreateNewDeco(container);
        yield return null;
        var beforeDelete = new LayoutState(KingdomFoundation.Api.FoundationApi.Kingdom);
        long beforeDeleteGold = Gold;
        mover = container.World.GetComponentInChildren<WorldFlowDecoMover>();
        mover.StartDragDrop(false);
        Check(mover.GetAnchoredParts().Item1.Count() == 3, "Multi-delete selection contains all three buildings");
        GameUI.Instance.ToggleDeconstructMode(true);
        Check(player.ActiveFlow.Elements.Count == 0 && player.ActiveFlow.Pipes.Count == 0 && !player.ActiveFlow.Decorations.Contains(container), "Multi-select delete removes every building, pipe, and parent");
        Check(Plugin.UndoCount == 1 && Gold > beforeDeleteGold, "Multi-delete records one action and its combined refund");
        long afterDeleteGold = Gold;
        Plugin.Replay(false);
        Check(beforeDelete.SameLayout(new LayoutState(KingdomFoundation.Api.FoundationApi.Kingdom)) && Gold == beforeDeleteGold, "One Undo restores multi-delete, connections, IO identities, and exact refund");
        Check(a.Inputs.Count > 0 && b.Inputs[0].ConnectedPipe == pipe && a.Outputs[0].ConnectedPipe == pipe, "Restored deleted buildings retain functioning native IO");
        Plugin.Replay(true);
        Check(player.ActiveFlow.Elements.Count == 0 && Gold == afterDeleteGold, "One Redo repeats multi-delete and exact refund");
        Plugin.Replay(false);
        yield return null;
        Plugin.Clear();

        // Cancelled multi-moves must not consume history or change models.
        mover = container.World.GetComponentInChildren<WorldFlowDecoMover>();
        var beforeCancel = new LayoutState(KingdomFoundation.Api.FoundationApi.Kingdom);
        mover.StartDragDrop(false);
        Keys(Key.LeftCtrl);
        mover.OnDragDropMove(container.Position + new Vector2Int(0, -512), Vector2Int.zero);
        DragDropManager.Instance.CancelDragDrop();
        Check(beforeCancel.SameLayout(new LayoutState(KingdomFoundation.Api.FoundationApi.Kingdom)) && Plugin.UndoCount == 0, "Cancel multi-select move restores every selected part without a history entry");
        Keys();

        var batchType = AccessTools.TypeByName("KingdomBetterDeconstruct.DeconstructSelection");
        if (batchType != null)
        {
            yield return null;
            Physics2D.SyncTransforms();
            GameUI.Instance.ToggleDeconstructMode(true);
            Plugin.Clear();
            var batch = AccessTools.Method(batchType, "FromBox").Invoke(null, new object[] { new Vector2Int(-512, 512), new Vector2Int(1600, -512), false });
            Check((int)AccessTools.PropertyGetter(batchType, "TotalBuildingNodes").Invoke(batch, null) == 3, "Better Deconstruct box-select finds all three buildings");
            var modalType = AccessTools.TypeByName("KingdomBetterDeconstruct.DeconstructConfirmModal");
            var modal = AccessTools.PropertyGetter(modalType, "Instance").Invoke(null, null);
            AccessTools.Method(modalType, "Show").Invoke(modal, new[] { batch });
            AccessTools.Method(modalType, "Cancel").Invoke(modal, null);
            Check(player.ActiveFlow.Elements.Count == 3 && Plugin.UndoCount == 0, "Cancel Better Deconstruct batch confirmation leaves history and buildings unchanged");
            AccessTools.Method(modalType, "Show").Invoke(modal, new[] { batch });
            var beforeBatch = new LayoutState(KingdomFoundation.Api.FoundationApi.Kingdom);
            long beforeBatchGold = Gold;
            AccessTools.Method(modalType, "Confirm").Invoke(modal, null);
            Check(player.ActiveFlow.Elements.Count == 0 && Plugin.UndoCount == 1 && Gold > beforeBatchGold, "Better Deconstruct confirmation deletes the box selection as one recorded action");
            long afterBatchGold = Gold;
            Plugin.Replay(false);
            Check(beforeBatch.SameLayout(new LayoutState(KingdomFoundation.Api.FoundationApi.Kingdom)) && Gold == beforeBatchGold, "One Undo restores Better Deconstruct box-delete, pipes, decorations, and exact refund");
            Plugin.Replay(true);
            Check(player.ActiveFlow.Elements.Count == 0 && Gold == afterBatchGold, "One Redo repeats Better Deconstruct box-delete and refund");
            Plugin.Replay(false);
            yield return null;
            Plugin.Clear();
            // Filtering out buildings should preserve all buildings and their pipe.
            AccessTools.Method(batchType, "ExecuteDeconstruct").Invoke(batch, new object[] { false, true, false, false });
            Check(player.ActiveFlow.Elements.Count == 3 && player.ActiveFlow.Pipes.Contains(pipe) && !player.ActiveFlow.Decorations.Contains(container) && Plugin.UndoCount == 1,
                "Better Deconstruct category filtering records only selected containers");
            Plugin.Replay(false);
            Check(beforeBatch.SameLayout(new LayoutState(KingdomFoundation.Api.FoundationApi.Kingdom)), "Undo filtered batch restores only the selected categories");
            yield return null;
            Plugin.Clear();
        }

        // Currency replay uses captured deltas, not current prices or a wallet snapshot.
        long beforePurchase = Gold;
        Forge placed = null;
        Edit(() => { player.SpendCurrency(Currency.Gold, 1000); placed = ForgeAt(2048, 64); });
        player.AddCurrency(Currency.Gold, 77, false);
        Plugin.Replay(false);
        Check(!player.ActiveFlow.Elements.Contains(placed) && Gold == beforePurchase + 77, "Undo placement restores the exact cost and preserves subsequent income");
        Plugin.Replay(true);
        Check(player.ActiveFlow.Elements.Contains(placed) && Gold == beforePurchase - 1000 + 77, "Redo placement charges the exact original cost");
        Plugin.Replay(false);
        player.SpendCurrency(Currency.Gold, Gold - 10);
        var unaffordable = new LayoutState(KingdomFoundation.Api.FoundationApi.Kingdom);
        Plugin.Replay(true);
        Check(unaffordable.SameLayout(new LayoutState(KingdomFoundation.Api.FoundationApi.Kingdom)) && Gold == 10 && Plugin.RedoCount == 1, "Insufficient funds rejects Redo without changing the world or consuming history");
        player.AddCurrency(Currency.Gold, 100000000, false);
        Plugin.Clear();

        Keys(Key.LeftCtrl);
        int stack = a.StackSize;
        a.World.AttemptStackElement();
        Check(a.StackSize == stack + 1 && Plugin.UndoCount == 1, "Native stacking is recorded with its purchase");
        Plugin.Replay(false);
        Check(a.StackSize == stack, "Undo stacking");
        Plugin.Replay(true);
        Check(a.StackSize == stack + 1, "Redo stacking");
        Plugin.Clear();
        Keys(Key.LeftAlt);
        Check(Keyboard.current == keyboard && keyboard.altKey.isPressed,
            "Mirror fixture delivers Alt through the active synthetic keyboard");
        Check(Assets.Behaviour.Input.PlayerInput.Instance.TraverseDistance <= 50,
            "Mirror fixture starts as a click rather than a camera-pan gesture");
        bool mirrored = a.Mirrored;
        a.World.OnPointerClick(new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left });
        Check(a.Mirrored != mirrored && Plugin.UndoCount == 1, "Native mirroring happens and is recorded before replay");
        Plugin.Replay(false);
        Check(a.Mirrored == mirrored, "Undo native mirroring");
        Plugin.Replay(true);
        Check(a.Mirrored != mirrored, "Redo native mirroring");
        Keys();
        Plugin.Clear();

        // Use the actual blueprint placement path and its real price.
        Keys();
        var blueprintModel = new Forge(); blueprintModel.SetupRecipe("iron_ingot");
        long nativeCost = blueprintModel.GetCopyCost().GetCurrency().Where(v => v.c == Currency.Gold).Sum(v => v.amt);
        long nativeGold = Gold;
        int nativeCount = player.ActiveFlow.Elements.Count;
        var ghost = World.ShowPurchaseBlueprint(blueprintModel, false);
        ((RectTransform)ghost.transform).anchoredPosition = new Vector2(3072, 64);
        DragDropManager.Instance.ClearDragDrop();
        ghost.OnDragDropEnd();
        Check(player.ActiveFlow.Elements.Count == nativeCount + 1 && Plugin.UndoCount == 1 && Gold == nativeGold - nativeCost,
            "Native blueprint placement records one edit with its actual cost");
        Plugin.Replay(false);
        Check(player.ActiveFlow.Elements.Count == nativeCount && Gold == nativeGold, "Undo native blueprint placement");
        Plugin.Replay(true);
        Check(player.ActiveFlow.Elements.Count == nativeCount + 1 && Gold == nativeGold - nativeCost, "Redo native blueprint placement");
        Plugin.Replay(false);
        yield return null;
        Plugin.Clear();

        var upgradeView = a.World.GetComponentsInChildren<WorldFlowUpgrade>(true).First(v => v.Slot == 0);
        var oldUpgrade = a.GetUpgrade(0);
        long upgradeGold = Gold;
        upgradeView.SelectNewUpgrade(new Assets.Source.Flow.Upgrades.Worker());
        Check(a.GetUpgrade(0) is Assets.Source.Flow.Upgrades.Worker && Plugin.UndoCount == 1, "Native upgrade selection is recorded");
        Plugin.Replay(false);
        Check(ReferenceEquals(a.GetUpgrade(0), oldUpgrade) && Gold == upgradeGold, "Undo upgrade restores the previous slot and exact currency");
        Plugin.Replay(true);
        Check(a.GetUpgrade(0) is Assets.Source.Flow.Upgrades.Worker, "Redo upgrade");
        Plugin.Replay(false);
        Plugin.Clear();

        a.World.GetComponentsInChildren<WorldFlowIO>(true).First(v => ReferenceEquals(v.IO, a.Outputs[0])).OnPointerClick(
            new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Right });
        Check(!player.ActiveFlow.Pipes.Contains(pipe) && Plugin.UndoCount == 1, "Native pipe disconnect is recorded");
        Plugin.Replay(false);
        Check(player.ActiveFlow.Pipes.Contains(pipe) && a.Outputs[0].ConnectedPipe == pipe && b.Inputs[0].ConnectedPipe == pipe, "Undo reconnects both pipe endpoints");
        Plugin.Replay(true);
        Check(!player.ActiveFlow.Pipes.Contains(pipe), "Redo disconnects the pipe");
        Plugin.Replay(false);
        Plugin.Clear();

        // A deletion may transfer inventory. Reject replay once those items have been consumed.
        var item = new InventoryItem("iron_ore");
        Edit(() => { a.Inputs[0].BufferedItems.Add(item); a.Position += new Vector2Int(0, 64); });
        a.Inputs[0].BufferedItems.Remove(item);
        var blocked = new LayoutState(KingdomFoundation.Api.FoundationApi.Kingdom);
        Plugin.Replay(false);
        Check(blocked.SameLayout(new LayoutState(KingdomFoundation.Api.FoundationApi.Kingdom)) && Plugin.UndoCount == 1, "Changed transferred inventory blocks replay instead of duplicating items");
        Plugin.Clear();

        // Repeated operations retain no more than the configured capacity.
        for (int i = 0; i < 55; i++) Edit(() => a.Position += new Vector2Int(0, 64));
        Check(Plugin.UndoCount == 50, "History is bounded to the default 50 actions");
        Plugin.Clear();

        // Actual configured shortcut path, including right-hand Ctrl/Shift and text focus.
        Edit(() => a.Position += new Vector2Int(0, 64));
        var hotkeyPosition = a.Position;
        yield return null;
        Keys(Key.RightCtrl, Key.Z);
        AccessTools.Method(typeof(UndoPlugin), "LateUpdate").Invoke(Plugin, null);
        Check(a.Position != hotkeyPosition && Plugin.RedoCount == 1, "Ctrl+Z input invokes Undo");
        Keys();
        yield return null;
        Keys(Key.RightCtrl, Key.RightShift, Key.Z);
        AccessTools.Method(typeof(UndoPlugin), "LateUpdate").Invoke(Plugin, null);
        Check(a.Position == hotkeyPosition && Plugin.RedoCount == 0, "Ctrl+Shift+Z input invokes Redo");
        Keys();
        yield return null;
        var inputObject = new GameObject("Undo text-focus test", typeof(RectTransform), typeof(TMPro.TMP_InputField));
        var input = inputObject.GetComponent<TMPro.TMP_InputField>();
        EventSystem.current.SetSelectedGameObject(inputObject);
        AccessTools.Field(typeof(TMPro.TMP_InputField), "m_AllowInput").SetValue(input, true);
        Keys(Key.LeftCtrl, Key.Z);
        AccessTools.Method(typeof(UndoPlugin), "LateUpdate").Invoke(Plugin, null);
        Check(a.Position == hotkeyPosition && Plugin.RedoCount == 0, "Ctrl+Z leaves focused text editing to the text field");
        EventSystem.current.SetSelectedGameObject(null);
        Destroy(inputObject);
        Keys();

        // Layout edits from another source fail closed rather than overwriting them.
        a.Position += new Vector2Int(64, 0);
        var external = a.Position;
        Plugin.Replay(false);
        Check(a.Position == external && Plugin.UndoCount == 0 && Plugin.RedoCount == 0, "External layout changes clear stale history without overwriting the change");

        // Selective delta view rebuild: editing 'a' preserves 'b', 'c', and secondFlow views without fallback.
        Plugin.Clear();
        var originalBView = b.World;
        var originalSecondView = secondForge.World;
        var beforeSelective = new LayoutState(FoundationApi.Kingdom);
        Edit(() => a.World.UpdatePosition(a.Position + new Vector2Int(0, 128)));
        var afterSelective = new LayoutState(FoundationApi.Kingdom, beforeSelective);
        Check(object.ReferenceEquals(beforeSelective.Workspaces[1], afterSelective.Workspaces[1]), "Structural sharing reuses unchanged workspace snapshot");
        Plugin.Replay(false);
        Check(!Plugin.LastRebuildUsedFallback && Plugin.LastRebuiltElementViews == 1 && ReferenceEquals(b.World, originalBView) && ReferenceEquals(secondForge.World, originalSecondView),
            "Selective view rebuild updates only changed element and preserves untouched views and workspaces");
        Plugin.Clear();

        // UndoApi: RegisterObjectExtension with affectsConnectedPipeEndpoints + OpenEditScope + RunEdit rollback.
        var customValues = new Dictionary<string, string>();
        using (var extReg = UndoApi.RegisterObjectExtension(
            "undo.probe.ext",
            obj => obj.Guid != null && customValues.TryGetValue(obj.Guid, out var v) ? v : "default",
            (obj, val) => { if (obj.Guid != null) customValues[obj.Guid] = (string)val; },
            (l, r) => string.Equals((string)l, (string)r, StringComparison.Ordinal),
            affectsConnectedPipeEndpoints: true))
        {
            customValues[a.Guid] = "alpha";
            using (UndoApi.OpenEditScope())
            {
                customValues[a.Guid] = "beta";
            }
            Check(Plugin.UndoCount == 1, "UndoApi.OpenEditScope records extension change");
            Plugin.Replay(false);
            Check(customValues[a.Guid] == "alpha" && Plugin.LastRefreshedPipeViews >= 1, "Undo extension restores value and refreshes connected pipe view");
            Plugin.Replay(true);
            Check(customValues[a.Guid] == "beta", "Redo extension restores updated value");
            Plugin.Replay(false);
            Plugin.Clear();

            bool threw = false;
            try
            {
                UndoApi.RunEdit(() =>
                {
                    a.Position += new Vector2Int(64, 0);
                    throw new InvalidOperationException("intentional abort");
                });
            }
            catch (InvalidOperationException)
            {
                threw = true;
            }
            Check(threw && Plugin.UndoCount == 0, "UndoApi.RunEdit exception aborts recording");
            Plugin.Clear();
        }

        // Currency delta overflow / long.MinValue bound guard.
        var minValueResult = HistoryEntry.TryCreate(beforeSelective, beforeSelective, new[] { (decimal)long.MinValue }, Array.Empty<InventoryChange>(), out _);
        Check(!minValueResult, "HistoryEntry.TryCreate rejects long.MinValue overflow delta");
        SaveGame.StoreAutosaveState("must-not-save");
        Check(!Directory.EnumerateFiles(SaveGame.SavesPath).Any(), "No save files created during verification");
        checks.Add("Game version: " + Application.version);
    }
}

