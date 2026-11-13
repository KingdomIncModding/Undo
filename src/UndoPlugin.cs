using KingdomFoundation.Api;
using KingdomFoundation.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;





using BepInEx;
using BepInEx.Configuration;

using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Undo.Probe")]

namespace KingdomIncUndo
{
    [BepInDependency("urakka.kingdominc.foundation", "0.14.0")]
    [BepInPlugin(Id, "Undo", "1.0.1")]
    [BepInDependency("urakka.kingdominc.betterdeconstruct", BepInDependency.DependencyFlags.SoftDependency)]
    public sealed class UndoPlugin : BaseUnityPlugin
    {
        public const string Id = "urakka.kingdominc.undo";
        internal static UndoPlugin Instance;
        private readonly List<IDisposable> editHooks = new List<IDisposable>();
        private ConfigEntry<int> limit;
        private readonly List<HistoryEntry> undo = new List<HistoryEntry>();
        private readonly List<HistoryEntry> redo = new List<HistoryEntry>();
        private GameKingdom player;
        private Component world;
        private LayoutState pending;
        private long[] currencyDelta;
        private readonly Dictionary<WorkspacePort, InventoryChange> inventoryDelta = new Dictionary<WorkspacePort, InventoryChange>();
        private Dictionary<WorkspacePort, GameInventoryItem[]> scopeInventory;
        private long[] scopeCurrency;
        private int depth;
        private bool replaying;
        private int replayFrame = -1;

        private void Awake()
        {
            Instance = this;
            limit = Config.Bind("History", "MaximumActions", 50, new ConfigDescription("Maximum edits kept in memory. History clears when a save or factory list changes.", new AcceptableValueRange<int>(1, 200)));
            editHooks.AddRange(EditHooks.Register());
            Logger.LogInfo("Undo 1.0.1 ready: Ctrl+Z Undo; Ctrl+Shift+Z Redo.");
        }

        private void OnDestroy()
        {
            foreach(var hook in editHooks) hook.Dispose();
            editHooks.Clear();
            Clear();
            if (Instance == this) Instance = null;
        }

        private bool Ready => FoundationApi.Kingdom != null && FoundationApi.WorkspaceAccess.Root && FoundationApi.UiAccess.Root;

        private void CheckSession()
        {
            if (!ReferenceEquals(player, FoundationApi.Kingdom) || world != FoundationApi.WorkspaceAccess.Root ||
                (pending != null && !pending.SameFlows(FoundationApi.Kingdom)))
            {
                Clear(); player = FoundationApi.Kingdom; world = FoundationApi.WorkspaceAccess.Root;
            }
            if (undo.Count > 0 && !undo[undo.Count - 1].After.SameFlows(player)) Clear();
            if (redo.Count > 0 && !redo[redo.Count - 1].Before.SameFlows(player)) Clear();
        }

        internal int UndoCount => undo.Count;
        internal int RedoCount => redo.Count;

        internal void Clear()
        {
            undo.Clear(); redo.Clear(); pending = null; currencyDelta = null;
            inventoryDelta.Clear(); scopeInventory = null; scopeCurrency = null; depth = 0;
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
                    currencyDelta = new long[GameKingdom.CurrencyCount];
                    inventoryDelta.Clear();
                }
                scopeCurrency = ReadCurrency();
                var current = new LayoutState(player);
                scopeInventory = current.IO.Concat(pending.IO).Distinct().ToDictionary(io => io, io => io.BufferedItems.ToArray());
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
                if (!pending.SameFlows(FoundationApi.Kingdom)) { Clear(); return; }
                var afterCurrency = ReadCurrency();
                for (int i = 0; i < currencyDelta.Length; i++) currencyDelta[i] += afterCurrency[i] - scopeCurrency[i];
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
            var after = new LayoutState(player);
            if (!pending.SameLayout(after))
            {
                undo.Add(new HistoryEntry(pending, after, currencyDelta, inventoryDelta.Values));
                while (undo.Count > limit.Value) undo.RemoveAt(0);
                redo.Clear();
            }
            pending = null; currencyDelta = null; inventoryDelta.Clear();
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
                Clear(); Message("Undo history cleared: the factory changed outside a recorded edit."); return;
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
                source.RemoveAt(source.Count - 1); destination.Add(entry);
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
                    // Currency is restored from the pre-replay values below.
                    for (int i = 0; i < now.Length; i++) player.AddCurrency(i, replayCurrency[i] - now[i], false);
                    RebuildWorld(current, current);
                }
                catch (Exception rollback) { Logger.LogError("Undo recovery failed: " + rollback); }
                Fail(ex); Message("Undo failed; history cleared. See the BepInEx log.");
            }
            finally { replaying = false; }
        }

        private long[] replayCurrency;

        private void RebuildWorld(LayoutState oldState, LayoutState target)
        {
            foreach (var element in oldState.ElementData.Keys.Concat(target.ElementData.Keys).Distinct())
            {
                if (element.World) { element.World.gameObject.SetActive(false); Destroy(element.World.gameObject); }
                element.ClearViewReference();
            }
            foreach (var pipe in oldState.PipeData.Keys.Concat(target.PipeData.Keys).Distinct())
            {
                if (pipe.World) { pipe.World.gameObject.SetActive(false); Destroy(pipe.World.gameObject); }
                pipe.ClearViewReference();
            }
            foreach (var deco in oldState.DecoData.Keys.Concat(target.DecoData.Keys).Distinct())
            {
                if (deco.World) { deco.World.gameObject.SetActive(false); Destroy(deco.World.gameObject); }
                deco.ClearViewReference();
            }
            foreach (var node in FoundationApi.WorkspaceAccess.PipeNodeViews)
            { node.gameObject.SetActive(false); Destroy(node.gameObject); }
            FoundationApi.WorkspaceAccess.ClearViewCaches();
            for (int i = 0; i < player.Workspaces.Count; i++) FoundationApi.WorkspaceAccess.SetupWorkspace(i);
            FoundationApi.WorkspaceAccess.ShowWorkspace(player.CurrentWorkspace);
        }

        private void Fail(Exception ex) { Logger.LogError("Undo recording/replay error: " + ex); Clear(); }
        private void Message(string text)
        {
            Logger.LogInfo(text);
            FoundationApi.UiAccess.ShowStatusMessage(text);
        }
    }

}
