using KingdomFoundation.Api;
using KingdomFoundation.Json;
using System;
using System.Collections.Generic;
using System.Linq;





using UnityEngine;

namespace KingdomIncUndo
{
    internal sealed class ElementState
    {
        internal readonly Vector2Int Position;
        internal readonly bool Mirrored;
        internal readonly int Stack;
        internal readonly WorkspacePort[] Inputs;
        internal readonly WorkspacePort[] Outputs;
        private readonly GameOpaqueValue[] upgrades;

        internal ElementState(WorkspaceObject element)
        {
            Position = element.Position; Mirrored = element.Mirrored; Stack = element.StackSize;
            Inputs = element.Inputs.ToArray(); Outputs = element.Outputs.ToArray();
            upgrades = element.Upgrades?.ToArray();
        }

        internal bool Same(ElementState other) => Position == other.Position && Mirrored == other.Mirrored && Stack == other.Stack &&
            Inputs.SequenceEqual(other.Inputs) && Outputs.SequenceEqual(other.Outputs) &&
            (upgrades ?? Array.Empty<GameOpaqueValue>()).SequenceEqual(other.upgrades ?? Array.Empty<GameOpaqueValue>());

        internal void Restore(WorkspaceObject element)
        {
            bool update = element.StackSize != Stack || !(element.Upgrades ?? Array.Empty<GameOpaqueValue>()).SequenceEqual(upgrades ?? Array.Empty<GameOpaqueValue>());
            element.SetPosition(Position); element.Mirrored = Mirrored; element.SetStackCount(Stack);
            element.SetInputs(Inputs);
            element.SetOutputs(Outputs);
            element.SetUpgrades(upgrades);
            if (update) element.UpdateUpgrades();
        }
    }
}
