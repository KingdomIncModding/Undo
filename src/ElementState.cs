using System;
using System.Collections.Generic;
using System.Linq;
using KingdomFoundation.Api;
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
            Position = element.Position;
            Mirrored = element.Mirrored;
            Stack = element.StackSize;
            Inputs = element.Inputs.ToArray();
            Outputs = element.Outputs.ToArray();
            upgrades = element.Upgrades?.ToArray();
        }

        internal long EstimatedBytes => 96 + (Inputs.Length + Outputs.Length + (upgrades?.Length ?? 0)) * 16;

        internal bool MatchesLive(WorkspaceObject element)
        {
            if (Position != element.Position || Mirrored != element.Mirrored || Stack != element.StackSize)
                return false;
            if (!PortsEqual(Inputs, element.Inputs) || !PortsEqual(Outputs, element.Outputs))
                return false;
            var liveUpgrades = element.Upgrades;
            int expected = upgrades?.Length ?? 0;
            int actual = liveUpgrades?.Count ?? 0;
            if (expected != actual) return false;
            for (int i = 0; i < expected; i++)
                if (!Equals(upgrades[i], liveUpgrades[i])) return false;
            return true;
        }

        internal bool Same(ElementState other)
        {
            if (ReferenceEquals(this, other)) return true;
            if (other == null) return false;
            return Position == other.Position && Mirrored == other.Mirrored && Stack == other.Stack &&
                   Inputs.SequenceEqual(other.Inputs) && Outputs.SequenceEqual(other.Outputs) &&
                   (upgrades ?? Array.Empty<GameOpaqueValue>()).SequenceEqual(other.upgrades ?? Array.Empty<GameOpaqueValue>());
        }

        internal void Restore(WorkspaceObject element)
        {
            bool update = element.StackSize != Stack || !(element.Upgrades ?? Array.Empty<GameOpaqueValue>()).SequenceEqual(upgrades ?? Array.Empty<GameOpaqueValue>());
            element.SetPosition(Position);
            element.Mirrored = Mirrored;
            element.SetStackCount(Stack);
            element.SetInputs(Inputs);
            element.SetOutputs(Outputs);
            element.SetUpgrades(upgrades);
            if (update) element.UpdateUpgrades();
        }

        private static bool PortsEqual(WorkspacePort[] expected, IReadOnlyList<WorkspacePort> actual)
        {
            if (expected.Length != actual.Count) return false;
            for (int i = 0; i < expected.Length; i++)
                if (!Equals(expected[i], actual[i])) return false;
            return true;
        }
    }
}
