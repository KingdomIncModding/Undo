using KingdomFoundation.Api;
using KingdomFoundation.Json;
using System;
using System.Collections.Generic;
using System.Linq;





using UnityEngine;

namespace KingdomIncUndo
{
    internal sealed class InventoryChange
    {
        internal readonly WorkspacePort IO;
        internal readonly GameInventoryItem[] Before;
        internal GameInventoryItem[] After;
        internal InventoryChange(WorkspacePort io, GameInventoryItem[] before, GameInventoryItem[] after) { IO = io; Before = before; After = after; }
        internal bool CanApply(bool redo) => IO.BufferedItems.SequenceEqual(redo ? Before : After);
        internal void Apply(bool redo)
        {
            IO.SetBuffer(redo ? After : Before);
        }
    }
}
