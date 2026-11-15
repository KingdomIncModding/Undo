using System.Linq;
using KingdomFoundation.Api;
using UnityEngine;

namespace KingdomIncUndo
{
    internal sealed class PipeState
    {
        internal readonly WorkspacePort Output;
        internal readonly WorkspacePort Input;
        internal readonly Vector2Int[] Nodes;

        internal PipeState(WorkspacePipe pipe)
        {
            Output = pipe.ConnectedOutput;
            Input = pipe.ConnectedInput;
            Nodes = pipe.Nodes.ToArray();
        }

        internal long EstimatedBytes => 64 + Nodes.Length * 8;

        internal bool MatchesLive(WorkspacePipe pipe)
        {
            if (!Equals(Output, pipe.ConnectedOutput) || !Equals(Input, pipe.ConnectedInput))
                return false;
            var liveNodes = pipe.Nodes;
            if (Nodes.Length != liveNodes.Count) return false;
            for (int i = 0; i < Nodes.Length; i++)
                if (Nodes[i] != liveNodes[i]) return false;
            return true;
        }

        internal bool Same(PipeState other)
        {
            if (ReferenceEquals(this, other)) return true;
            if (other == null) return false;
            return Equals(Output, other.Output) && Equals(Input, other.Input) && Nodes.SequenceEqual(other.Nodes);
        }

        internal void Restore(WorkspacePipe pipe)
        {
            pipe.SetNodes(Nodes);
            pipe.Connect(Output, Input);
        }
    }
}
