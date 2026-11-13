using KingdomFoundation.Api;
using KingdomFoundation.Json;
using System;
using System.Collections.Generic;
using System.Linq;





using UnityEngine;

namespace KingdomIncUndo
{
    internal sealed class LayoutState
    {
        internal readonly GameKingdom Player;
        internal readonly GameWorkspace[] Flows;
        internal readonly Dictionary<GameWorkspace, WorkspaceObject[]> Elements = new Dictionary<GameWorkspace, WorkspaceObject[]>();
        internal readonly Dictionary<GameWorkspace, WorkspacePipe[]> Pipes = new Dictionary<GameWorkspace, WorkspacePipe[]>();
        internal readonly Dictionary<GameWorkspace, WorkspaceObject[]> Decos = new Dictionary<GameWorkspace, WorkspaceObject[]>();
        internal readonly Dictionary<WorkspaceObject, ElementState> ElementData = new Dictionary<WorkspaceObject, ElementState>();
        internal readonly Dictionary<WorkspacePipe, PipeState> PipeData = new Dictionary<WorkspacePipe, PipeState>();
        internal readonly Dictionary<WorkspaceObject, string> DecoData = new Dictionary<WorkspaceObject, string>();

        internal LayoutState(GameKingdom player)
        {
            Player = player;
            Flows = player.Workspaces.ToArray();
            foreach (var flow in Flows)
            {
                Elements.Add(flow, flow.Buildings.ToArray());
                Pipes.Add(flow, flow.Pipes.ToArray());
                Decos.Add(flow, flow.Decorations.ToArray());
                foreach (var element in flow.Buildings) ElementData.Add(element, new ElementState(element));
                foreach (var pipe in flow.Pipes) PipeData.Add(pipe, new PipeState(pipe));
                foreach (var deco in flow.Decorations) DecoData.Add(deco, FoundationApi.WorkspaceAccess.Serialize(deco).ToString());
            }
        }

        internal bool SameFlows(GameKingdom player) => ReferenceEquals(Player, player) && Flows.SequenceEqual(player.Workspaces);

        internal bool SameLayout(LayoutState other)
        {
            if (!ReferenceEquals(Player, other.Player) || !Flows.SequenceEqual(other.Flows)) return false;
            foreach (var flow in Flows)
                if (!Elements[flow].SequenceEqual(other.Elements[flow]) || !Pipes[flow].SequenceEqual(other.Pipes[flow]) ||
                    !Decos[flow].SequenceEqual(other.Decos[flow])) return false;
            return ElementData.All(p => p.Value.Same(other.ElementData[p.Key])) &&
                   PipeData.All(p => p.Value.Same(other.PipeData[p.Key])) &&
                   DecoData.All(p => p.Value == other.DecoData[p.Key]);
        }

        internal IEnumerable<WorkspacePort> IO => ElementData.Values.SelectMany(e => e.Inputs.Cast<WorkspacePort>().Concat(e.Outputs));

        internal void RestoreModel(LayoutState current)
        {
            // Disconnect first; reconnect only after every building's IO is back.
            foreach (var io in current.IO.Concat(IO).Distinct()) io.SetConnectedPipe(null);
            foreach (var flow in Flows)
            {
                flow.SetBuildings(Elements[flow]);
                flow.SetPipes(Pipes[flow]);
                flow.SetDecorations(Decos[flow]);
            }
            foreach (var pair in ElementData) pair.Value.Restore(pair.Key);
            foreach (var pair in PipeData) pair.Value.Restore(pair.Key);
            foreach (var pair in DecoData)
            {
                pair.Key.ApplyDecorationData(JsonValue.Parse(pair.Value));
            }
        }
    }
}
