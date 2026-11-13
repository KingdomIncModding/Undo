using KingdomFoundation.Api;
using KingdomFoundation.Json;
using System;
using System.Collections.Generic;
using System.Linq;





using UnityEngine;

namespace KingdomIncUndo
{
    internal sealed class HistoryEntry
    {
        internal readonly LayoutState Before, After;
        internal readonly long[] Currency;
        internal readonly InventoryChange[] Inventory;
        internal HistoryEntry(LayoutState before, LayoutState after, long[] currency, IEnumerable<InventoryChange> inventory)
        { Before = before; After = after; Currency = currency; Inventory = inventory.ToArray(); }

        internal bool CanPay(GameKingdom player, bool redo)
        {
            for (int i = 0; i < Currency.Length; i++)
            {
                long delta = redo ? Currency[i] : -Currency[i];
                if (delta < 0 && player.GetCurrency(i) < -delta) return false;
                if (delta > 0 && player.GetCurrency(i) > long.MaxValue - delta) return false;
            }
            return true;
        }

        internal void ApplyModel(bool redo, LayoutState current)
        {
            (redo ? After : Before).RestoreModel(current);
            foreach (var change in Inventory) change.Apply(redo);
            for (int i = 0; i < Currency.Length; i++)
                if (Currency[i] != 0) Before.Player.AddCurrency(i, redo ? Currency[i] : -Currency[i], false);
        }
    }
}
