using System;
using System.Collections.Generic;
using System.Linq;
using KingdomFoundation.Api;

namespace KingdomIncUndo
{
    internal sealed class LayoutState
    {
        internal readonly GameKingdom Player;
        internal readonly GameWorkspace[] Flows;
        internal readonly WorkspaceSnapshot[] Workspaces;
        internal readonly long EstimatedDeltaBytes;

        internal LayoutState(GameKingdom player, LayoutState previous = null)
        {
            Player = player;
            Flows = player.Workspaces.ToArray();
            Workspaces = new WorkspaceSnapshot[Flows.Length];
            bool canShare = previous != null && ReferenceEquals(Player, previous.Player) && Flows.SequenceEqual(previous.Flows);
            long totalBytes = Flows.Length * 8L + 64L;
            for (int i = 0; i < Flows.Length; i++)
            {
                Workspaces[i] = WorkspaceSnapshot.Capture(Flows[i], canShare ? previous.Workspaces[i] : null, out long wsBytes);
                totalBytes += wsBytes;
            }
            EstimatedDeltaBytes = totalBytes;
        }

        internal bool SameFlows(GameKingdom player) => ReferenceEquals(Player, player) && Flows.SequenceEqual(player.Workspaces);

        internal bool SameLayout(LayoutState other)
        {
            if (ReferenceEquals(this, other)) return true;
            if (!ReferenceEquals(Player, other.Player) || !Flows.SequenceEqual(other.Flows)) return false;
            for (int i = 0; i < Workspaces.Length; i++)
                if (!Workspaces[i].SameWorkspace(other.Workspaces[i])) return false;
            return true;
        }

        internal IEnumerable<WorkspaceObject> AllElements => Workspaces.SelectMany(w => w.Elements);
        internal IEnumerable<WorkspacePipe> AllPipes => Workspaces.SelectMany(w => w.Pipes);
        internal IEnumerable<WorkspaceObject> AllDecos => Workspaces.SelectMany(w => w.Decos);
        internal IEnumerable<WorkspacePort> IO => Workspaces.SelectMany(w => w.IO);

        internal bool TryGetElement(WorkspaceObject item, out int workspaceIndex, out ElementState state, out Dictionary<string, object> extensions)
        {
            for (int i = 0; i < Workspaces.Length; i++)
            {
                if (Workspaces[i].ElementData.TryGetValue(item, out state))
                {
                    workspaceIndex = i;
                    extensions = null;
                    Workspaces[i].ExtensionData?.TryGetValue(item, out extensions);
                    return true;
                }
            }
            workspaceIndex = -1;
            state = null;
            extensions = null;
            return false;
        }

        internal bool TryGetPipe(WorkspacePipe pipe, out int workspaceIndex, out PipeState state)
        {
            for (int i = 0; i < Workspaces.Length; i++)
            {
                if (Workspaces[i].PipeData.TryGetValue(pipe, out state))
                {
                    workspaceIndex = i;
                    return true;
                }
            }
            workspaceIndex = -1;
            state = null;
            return false;
        }

        internal bool TryGetDeco(WorkspaceObject deco, out int workspaceIndex, out DecoState state, out Dictionary<string, object> extensions)
        {
            for (int i = 0; i < Workspaces.Length; i++)
            {
                if (Workspaces[i].DecoData.TryGetValue(deco, out state))
                {
                    workspaceIndex = i;
                    extensions = null;
                    Workspaces[i].ExtensionData?.TryGetValue(deco, out extensions);
                    return true;
                }
            }
            workspaceIndex = -1;
            state = null;
            extensions = null;
            return false;
        }

        internal void RestoreModel(LayoutState current)
        {
            bool flowsMatch = current != null && ReferenceEquals(Player, current.Player) && Flows.SequenceEqual(current.Flows);
            if (!flowsMatch)
            {
                if (current != null)
                    foreach (var io in current.IO.Concat(IO).Distinct()) io.SetConnectedPipe(null);
                else
                    foreach (var io in IO.Distinct()) io.SetConnectedPipe(null);

                for (int i = 0; i < Flows.Length; i++)
                {
                    var flow = Flows[i];
                    var ws = Workspaces[i];
                    flow.SetBuildings(ws.Elements);
                    flow.SetPipes(ws.Pipes);
                    flow.SetDecorations(ws.Decos);
                    foreach (var pair in ws.ElementData)
                    {
                        pair.Value.Restore(pair.Key);
                        if (ws.ExtensionData != null && ws.ExtensionData.TryGetValue(pair.Key, out var ext))
                            UndoApi.RestoreExtensions(pair.Key, ext);
                    }
                    foreach (var pair in ws.PipeData) pair.Value.Restore(pair.Key);
                    foreach (var pair in ws.DecoData)
                    {
                        pair.Value.Restore(pair.Key);
                        if (ws.ExtensionData != null && ws.ExtensionData.TryGetValue(pair.Key, out var ext))
                            UndoApi.RestoreExtensions(pair.Key, ext);
                    }
                }
                return;
            }

            // Disconnect only IO ports belonging to modified workspaces before restoring element IO & pipes.
            for (int i = 0; i < Flows.Length; i++)
            {
                var targetWs = Workspaces[i];
                var currentWs = current.Workspaces[i];
                if (ReferenceEquals(targetWs, currentWs)) continue;
                foreach (var io in currentWs.IO.Concat(targetWs.IO).Distinct())
                    io.SetConnectedPipe(null);
            }

            for (int i = 0; i < Flows.Length; i++)
            {
                var flow = Flows[i];
                var targetWs = Workspaces[i];
                var currentWs = current.Workspaces[i];
                if (ReferenceEquals(targetWs, currentWs)) continue;

                flow.SetBuildings(targetWs.Elements);
                flow.SetPipes(targetWs.Pipes);
                flow.SetDecorations(targetWs.Decos);

                foreach (var pair in targetWs.ElementData)
                {
                    bool sameElem = currentWs.ElementData.TryGetValue(pair.Key, out var curElem) && pair.Value.Same(curElem);
                    bool sameExt = targetWs.ObjectExtensionsEqual(currentWs, pair.Key, out _);
                    if (!sameElem) pair.Value.Restore(pair.Key);
                    if (!sameExt && targetWs.ExtensionData != null && targetWs.ExtensionData.TryGetValue(pair.Key, out var ext))
                        UndoApi.RestoreExtensions(pair.Key, ext);
                }

                // Reconnect all pipes in modified workspaces because their endpoint IO was cleared above.
                foreach (var pair in targetWs.PipeData)
                    pair.Value.Restore(pair.Key);

                foreach (var pair in targetWs.DecoData)
                {
                    bool sameDeco = currentWs.DecoData.TryGetValue(pair.Key, out var curDeco) && pair.Value.Same(curDeco);
                    bool sameExt = targetWs.ObjectExtensionsEqual(currentWs, pair.Key, out _);
                    if (!sameDeco) pair.Value.Restore(pair.Key);
                    if (!sameExt && targetWs.ExtensionData != null && targetWs.ExtensionData.TryGetValue(pair.Key, out var ext))
                        UndoApi.RestoreExtensions(pair.Key, ext);
                }
            }
        }
    }
}
