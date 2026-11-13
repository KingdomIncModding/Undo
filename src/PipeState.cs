using KingdomFoundation.Api;
using KingdomFoundation.Json;
using System;
using System.Collections.Generic;
using System.Linq;





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
            Output = pipe.ConnectedOutput; Input = pipe.ConnectedInput; Nodes = pipe.Nodes.ToArray();
        }
        internal bool Same(PipeState other) => ReferenceEquals(Output, other.Output) && ReferenceEquals(Input, other.Input) && Nodes.SequenceEqual(other.Nodes);
        internal void Restore(WorkspacePipe pipe)
        {
            pipe.SetNodes(Nodes);
            pipe.Connect(Output, Input);
        }
    }
}
