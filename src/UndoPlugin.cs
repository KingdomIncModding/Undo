using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using KingdomFoundation.Api;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Undo.Probe")]

namespace KingdomIncUndo
{
    [BepInDependency("urakka.kingdominc.foundation", "0.14.4")]
    [BepInPlugin(Id, "Undo", "1.0.2")]
    [BepInDependency("urakka.kingdominc.betterdeconstruct", BepInDependency.DependencyFlags.SoftDependency)]
    public sealed class UndoPlugin : BaseUnityPlugin
    {
        public const string Id = "urakka.kingdominc.undo";
        internal static UndoPlugin Instance;

        private readonly List<IDisposable> editHooks = new List<IDisposable>();
        private ConfigEntry<int> limit;
        private ConfigEntry<int> memoryBudgetMb;
        private readonly List<HistoryEntry> undo = new List<HistoryEntry>();
        private readonly List<HistoryEntry> redo = new List<HistoryEntry>();
        private GameKingdom player;
        private Component world;
        private LayoutState pending;
        private LayoutState lastBaseline;
        private decimal[] currencyDelta;
        private readonly Dictionary<WorkspacePort, InventoryChange> inventoryDelta = new Dictionary<WorkspacePort, InventoryChange>();
        private Dictionary<WorkspacePort, GameInventoryItem[]> scopeInventory;
        private long[] scopeCurrency;
        private int depth;
        private bool replaying;
        private int replayFrame = -1;
        private long[] replayCurrency;

        internal int LastRebuiltElementViews { get; private set; }
        internal int LastRebuiltPipeViews { get; private set; }
        internal int LastRefreshedPipeViews { get; private set; }
        internal int LastRebuiltDecoViews { get; private set; }
        internal bool LastRebuildUsedFallback { get; private set; }

        private void Awake()
        {
            Instance = this;
            limit = Config.Bind("History", "MaximumActions", 50, new ConfigDescription("Maximum edits kept in memory. History clears when a save or factory list changes.", new AcceptableValueRange<int>(1, 200)));
            memoryBudgetMb = Config.Bind("History", "MaximumMemoryMB", 64, new ConfigDescription("Approximate memory ceiling in megabytes for retained history snapshots.", new AcceptableValueRange<int>(4, 512)));
            editHooks.AddRange(EditHooks.Register());
            Logger.LogInfo("Undo 1.0.2 ready: Ctrl+Z Undo; Ctrl+Shift+Z Redo.");
        }

        private void OnDestroy()
        {
            foreach (var hook in editHooks) hook.Dispose();
            editHooks.Clear();
            Clear();
            UndoApi.ClearAllExtensions();
            if (Instance == this) Instance = null;
        }

        private bool Ready => FoundationApi.Kingdom != null && FoundationApi.WorkspaceAccess.Root && FoundationApi.UiAccess.Root;

        private void CheckSession()
        {
            if (!ReferenceEquals(player, FoundationApi.Kingdom) || world != FoundationApi.WorkspaceAccess.Root ||
                (pending != null && !pending.SameFlows(FoundationApi.Kingdom)))
            {
                Clear();
                player = FoundationApi.Kingdom;
                world = FoundationApi.WorkspaceAccess.Root;
            }
            if (undo.Count > 0 && !undo[undo.Count - 1].After.SameFlows(player)) Clear();
            if (redo.Count > 0 && !redo[redo.Count - 1].Before.SameFlows(player)) Clear();
        }

        internal int UndoCount => undo.Count;
        internal int RedoCount => redo.Count;

        internal long TotalRetainedEstimatedBytes()
        {
            long total = 0;
            for (int i = 0; i < undo.Count; i++) total += undo[i].RetainedEstimatedBytes;
            for (int i = 0; i < redo.Count; i++) total += redo[i].RetainedEstimatedBytes;
            return total;
        }

        internal void Clear()
        {
            undo.Clear();
            redo.Clear();
            DiscardPending();
            depth = 0;
        }

        private void DiscardPending()
        {
            pending = null;
            lastBaseline = null;
            currencyDelta = null;
            inventoryDelta.Clear();
            scopeInventory = null;
            scopeCurrency = null;
        }

        // One scope is a synchronous UI operation; a pending edit can span many drag frames.
        internal bool Begin()
        {
            if (replaying || !Ready) return false;
            CheckSession();
            if (depth++ > 0) return true;
            try
            {
                if (pending == null)
                {
                    pending = new LayoutState(player);
                    lastBaseline = pending;
                    currencyDelta = new decimal[GameKingdom.CurrencyCount];
                    inventoryDelta.Clear();
                }
                else
                {
                    lastBaseline = new LayoutState(player, lastBaseline);
                }

                scopeCurrency = ReadCurrency();
                var ioSources = ReferenceEquals(lastBaseline, pending)
                    ? pending.IO
                    : lastBaseline.IO.Concat(pending.IO).Distinct();
                scopeInventory = new Dictionary<WorkspacePort, GameInventoryItem[]>();
                foreach (var io in ioSources)
                    if (!scopeInventory.ContainsKey(io))
                        scopeInventory.Add(io, io.BufferedItems.ToArray());
                return true;
            }
            catch (Exception ex) { Fail(ex); return false; }
        }

        internal void End(Exception error)
        {
            if (depth <= 0 || --depth > 0) return;
            if (error != null) { Fail(error); return; }
            try
            {
                if (pending == null || !pending.SameFlows(FoundationApi.Kingdom)) { Clear(); return; }
                var afterCurrency = ReadCurrency();
                for (int i = 0; i < currencyDelta.Length; i++)
                    currencyDelta[i] += (decimal)afterCurrency[i] - (decimal)scopeCurrency[i];
                foreach (var pair in scopeInventory)
                {
                    var after = pair.Key.BufferedItems.ToArray();
                    if (pair.Value.SequenceEqual(after)) continue;
                    if (inventoryDelta.TryGetValue(pair.Key, out var change)) change.After = after;
                    else inventoryDelta.Add(pair.Key, new InventoryChange(pair.Key, pair.Value, after));
                }
                scopeInventory = null;
                FinishIfIdle();
            }
            catch (Exception ex) { Fail(ex); }
        }

        private long[] ReadCurrency() => Enumerable.Range(0, GameKingdom.CurrencyCount).Select(i => player.GetCurrency(i)).ToArray();

        private void FinishIfIdle()
        {
            if (pending == null || depth != 0) return;
            bool dragging = FoundationApi.WorkspaceVisuals.HasNativeDragTarget;
            // Repeated Shift-placement is one action per completed placement.
            if (dragging && !FoundationApi.UiAccess.IsDraggingGhost) return;
            var after = new LayoutState(player, lastBaseline ?? pending);
            lastBaseline = after;
            if (!pending.SameLayout(after))
            {
                if (!HistoryEntry.TryCreate(pending, after, currencyDelta, inventoryDelta.Values, out var entry))
                {
                    Logger.LogWarning("Undo rejected an edit whose currency delta exceeds representable bounds.");
                    Clear();
                    return;
                }
                undo.Add(entry);
                redo.Clear();
                EnforceHistoryLimits();
            }
            DiscardPending();
        }

        private void EnforceHistoryLimits()
        {
            long budgetBytes = Math.Max(1L, memoryBudgetMb.Value) * 1024L * 1024L;
            while (undo.Count > limit.Value || (undo.Count > 1 && TotalRetainedEstimatedBytes() > budgetBytes))
                undo.RemoveAt(0);
        }

        private void LateUpdate()
        {
            if (!Ready) { if (player != null) { Clear(); player = null; world = null; } return; }
            CheckSession();
            try { FinishIfIdle(); }
            catch (Exception ex) { Fail(ex); }
            var keys = Keyboard.current;
            if (replayFrame == Time.frameCount || keys == null || !keys.ctrlKey.isPressed || !keys.zKey.wasPressedThisFrame ||
                keys.altKey.isPressed || !Application.isFocused || !FoundationApi.UiAccess.IsGameInputEnabled || !FoundationApi.UiAccess.IsWorkspaceVisible ||
                FoundationApi.UiAccess.IsMenuVisible || FoundationApi.UiAccess.IsTutorialActive || EditingText()) return;
            if (FoundationApi.WorkspaceVisuals.HasNativeDragTarget)
            {
                FoundationApi.UiAccess.CancelDragDrop();
                FinishIfIdle();
                Message("Canceled current edit.");
                return;
            }
            Replay(keys.shiftKey.isPressed);
        }

        private static bool EditingText()
        {
            var selected = EventSystem.current ? EventSystem.current.currentSelectedGameObject : null;
            if (!selected) return false;
            var tmp = selected.GetComponentInParent<TMP_InputField>();
            var legacy = selected.GetComponentInParent<InputField>();
            return (tmp && tmp.isFocused) || (legacy && legacy.isFocused);
        }

        internal void Replay(bool forward)
        {
            var source = forward ? redo : undo;
            var destination = forward ? undo : redo;
            if (source.Count == 0) { Message(forward ? "Nothing to redo." : "Nothing to undo."); return; }
            var entry = source[source.Count - 1];
            var current = new LayoutState(player);
            if (!(forward ? entry.Before : entry.After).SameLayout(current))
            {
                Clear();
                Message("Undo history cleared: the factory changed outside a recorded edit.");
                return;
            }
            if (!entry.CanPay(player, forward)) { Message("Cannot replay this edit: insufficient currency."); return; }
            if (entry.Inventory.Any(c => !c.CanApply(forward)))
            { Message("Cannot replay this edit: items moved by the edit have since changed."); return; }
            replaying = true;
            replayFrame = Time.frameCount;
            replayCurrency = ReadCurrency();
            try
            {
                FoundationApi.UiAccess.CloseAllMenus();
                entry.ApplyModel(forward, current);
                RebuildWorld(current, forward ? entry.After : entry.Before);
                source.RemoveAt(source.Count - 1);
                destination.Add(entry);
                Message(forward ? "Redo." : "Undo.");
            }
            catch (Exception ex)
            {
                // Restore the live state if a visual/model extension rejects replay.
                try
                {
                    current.RestoreModel(new LayoutState(player));
                    foreach (var change in entry.Inventory) change.Apply(!forward);
                    var now = ReadCurrency();
                    for (int i = 0; i < now.Length; i++)
                    {
                        decimal restoreDelta = (decimal)replayCurrency[i] - (decimal)now[i];
                        player.AddCurrency(i, (long)restoreDelta, false);
                    }
                    RebuildAllWorkspacesFallback(current, current);
                }
                catch (Exception rollback) { Logger.LogError("Undo recovery failed: " + rollback); }
                Fail(ex);
                Message("Undo failed; history cleared. See the BepInEx log.");
            }
            finally { replaying = false; }
        }

        private void RebuildWorld(LayoutState oldState, LayoutState target)
        {
            LastRebuiltElementViews = 0;
            LastRebuiltPipeViews = 0;
            LastRefreshedPipeViews = 0;
            LastRebuiltDecoViews = 0;
            LastRebuildUsedFallback = false;

            if (!ReferenceEquals(oldState.Player, target.Player) || !oldState.Flows.SequenceEqual(target.Flows))
            {
                RebuildAllWorkspacesFallback(oldState, target);
                return;
            }

            try
            {
                RebuildDeltaViews(oldState, target);
            }
            catch (Exception ex)
            {
                Logger.LogWarning("Undo selective view rebuild fell back to full rebuild: " + ex.Message);
                RebuildAllWorkspacesFallback(oldState, target);
            }
        }

        private void RebuildDeltaViews(LayoutState oldState, LayoutState target)
        {
            var elementsToDestroy = new HashSet<WorkspaceObject>();
            var elementsToCreate = new List<(int workspaceIndex, WorkspaceObject item)>();
            var decosToDestroy = new HashSet<WorkspaceObject>();
            var decosToCreate = new List<(int workspaceIndex, WorkspaceObject item)>();
            var pipesToDestroy = new HashSet<WorkspacePipe>();
            var pipesToCreate = new List<(int workspaceIndex, WorkspacePipe pipe)>();
            var rebuiltPorts = new HashSet<WorkspacePort>();
            var candidatePipesToRefresh = new List<WorkspacePipe>();

            for (int w = 0; w < target.Flows.Length; w++)
            {
                var oldWs = oldState.Workspaces[w];
                var newWs = target.Workspaces[w];
                if (ReferenceEquals(oldWs, newWs)) continue;

                foreach (var oldElem in oldWs.Elements)
                {
                    bool inNewSameWs = newWs.ElementData.TryGetValue(oldElem, out var newElemState);
                    int targetWsIdx = w;
                    if (!inNewSameWs && target.TryGetElement(oldElem, out targetWsIdx, out newElemState, out _))
                    {
                        elementsToDestroy.Add(oldElem);
                        elementsToCreate.Add((targetWsIdx, oldElem));
                        AddAllPorts(rebuiltPorts, oldWs.ElementData[oldElem], newElemState);
                        continue;
                    }
                    if (!inNewSameWs)
                    {
                        elementsToDestroy.Add(oldElem);
                        AddAllPorts(rebuiltPorts, oldWs.ElementData[oldElem], null);
                        continue;
                    }
                    bool sameExt = oldWs.ObjectExtensionsEqual(newWs, oldElem, out bool affectsPipes);
                    if (!oldWs.ElementData[oldElem].Same(newElemState) || !sameExt)
                    {
                        elementsToDestroy.Add(oldElem);
                        elementsToCreate.Add((w, oldElem));
                        AddAllPorts(rebuiltPorts, oldWs.ElementData[oldElem], newElemState);
                        if (affectsPipes) AddAllPorts(rebuiltPorts, newElemState, null);
                    }
                }

                foreach (var newElem in newWs.Elements)
                {
                    if (!oldWs.ElementData.ContainsKey(newElem) && !oldState.TryGetElement(newElem, out _, out _, out _))
                    {
                        elementsToCreate.Add((w, newElem));
                        AddAllPorts(rebuiltPorts, newWs.ElementData[newElem], null);
                    }
                }

                foreach (var oldDeco in oldWs.Decos)
                {
                    if (!newWs.DecoData.TryGetValue(oldDeco, out var newDecoState))
                    {
                        decosToDestroy.Add(oldDeco);
                        if (target.TryGetDeco(oldDeco, out int movedWs, out _, out _))
                            decosToCreate.Add((movedWs, oldDeco));
                        continue;
                    }
                    if (!oldWs.DecoData[oldDeco].Same(newDecoState) || !oldWs.ObjectExtensionsEqual(newWs, oldDeco, out _))
                    {
                        decosToDestroy.Add(oldDeco);
                        decosToCreate.Add((w, oldDeco));
                    }
                }

                foreach (var newDeco in newWs.Decos)
                {
                    if (!oldWs.DecoData.ContainsKey(newDeco) && !oldState.TryGetDeco(newDeco, out _, out _, out _))
                        decosToCreate.Add((w, newDeco));
                }

                foreach (var oldPipe in oldWs.Pipes)
                {
                    if (!newWs.PipeData.TryGetValue(oldPipe, out var newPipeState))
                    {
                        pipesToDestroy.Add(oldPipe);
                        if (target.TryGetPipe(oldPipe, out int movedPipeWs, out _))
                            pipesToCreate.Add((movedPipeWs, oldPipe));
                        continue;
                    }
                    if (!oldWs.PipeData[oldPipe].Same(newPipeState))
                    {
                        pipesToDestroy.Add(oldPipe);
                        pipesToCreate.Add((w, oldPipe));
                    }
                    else
                    {
                        candidatePipesToRefresh.Add(oldPipe);
                    }
                }

                foreach (var newPipe in newWs.Pipes)
                {
                    if (!oldWs.PipeData.ContainsKey(newPipe) && !oldState.TryGetPipe(newPipe, out _, out _))
                        pipesToCreate.Add((w, newPipe));
                }
            }

            var access = FoundationApi.WorkspaceAccess;
            foreach (var elem in elementsToDestroy) access.DestroyView(elem);
            foreach (var pipe in pipesToDestroy) access.DestroyView(pipe);
            foreach (var deco in decosToDestroy) access.DestroyView(deco);

            foreach (var (wsIndex, elem) in elementsToCreate)
            {
                access.CreateView(wsIndex, elem);
                LastRebuiltElementViews++;
            }
            foreach (var (wsIndex, deco) in decosToCreate)
            {
                access.CreateView(wsIndex, deco);
                LastRebuiltDecoViews++;
            }
            foreach (var (wsIndex, pipe) in pipesToCreate)
            {
                access.CreateView(wsIndex, pipe);
                LastRebuiltPipeViews++;
            }
            foreach (var pipe in candidatePipesToRefresh)
            {
                if (pipesToDestroy.Contains(pipe)) continue;
                if ((pipe.ConnectedOutput != null && rebuiltPorts.Contains(pipe.ConnectedOutput)) ||
                    (pipe.ConnectedInput != null && rebuiltPorts.Contains(pipe.ConnectedInput)))
                {
                    access.RefreshView(pipe);
                    LastRefreshedPipeViews++;
                }
            }
        }

        private static void AddAllPorts(HashSet<WorkspacePort> target, ElementState first, ElementState second)
        {
            if (first != null)
            {
                for (int i = 0; i < first.Inputs.Length; i++) target.Add(first.Inputs[i]);
                for (int i = 0; i < first.Outputs.Length; i++) target.Add(first.Outputs[i]);
            }
            if (second != null)
            {
                for (int i = 0; i < second.Inputs.Length; i++) target.Add(second.Inputs[i]);
                for (int i = 0; i < second.Outputs.Length; i++) target.Add(second.Outputs[i]);
            }
        }

        // Documented recovery fallback when workspace structure changes or selective diffing hits an unexpected state.
        private void RebuildAllWorkspacesFallback(LayoutState oldState, LayoutState target)
        {
            LastRebuildUsedFallback = true;
            var allElements = oldState.AllElements.Concat(target.AllElements).Distinct().ToArray();
            var allPipes = oldState.AllPipes.Concat(target.AllPipes).Distinct().ToArray();
            var allDecos = oldState.AllDecos.Concat(target.AllDecos).Distinct().ToArray();
            foreach (var element in allElements)
            {
                if (element.World) { element.World.gameObject.SetActive(false); Destroy(element.World.gameObject); }
                element.ClearViewReference();
            }
            foreach (var pipe in allPipes)
            {
                if (pipe.World) { pipe.World.gameObject.SetActive(false); Destroy(pipe.World.gameObject); }
                pipe.ClearViewReference();
            }
            foreach (var deco in allDecos)
            {
                if (deco.World) { deco.World.gameObject.SetActive(false); Destroy(deco.World.gameObject); }
                deco.ClearViewReference();
            }
            foreach (var node in FoundationApi.WorkspaceAccess.PipeNodeViews)
            {
                node.gameObject.SetActive(false);
                Destroy(node.gameObject);
            }
            FoundationApi.WorkspaceAccess.ClearViewCaches();
            for (int i = 0; i < player.Workspaces.Count; i++) FoundationApi.WorkspaceAccess.SetupWorkspace(i);
            FoundationApi.WorkspaceAccess.ShowWorkspace(player.CurrentWorkspace);
            LastRebuiltElementViews = allElements.Length;
            LastRebuiltPipeViews = allPipes.Length;
            LastRebuiltDecoViews = allDecos.Length;
        }

        private void Fail(Exception ex) { Logger.LogError("Undo recording/replay error: " + ex); Clear(); }

        private void Message(string text)
        {
            Logger.LogInfo(text);
            FoundationApi.UiAccess.ShowStatusMessage(text);
        }
    }
}
