using System;
using System.Collections.Generic;
using System.Linq;
using KingdomFoundation.Api;

namespace KingdomIncUndo
{
    internal sealed class HistoryEntry
    {
        internal readonly LayoutState Before, After;
        internal readonly long[] Currency;
        internal readonly InventoryChange[] Inventory;
        internal readonly long RetainedEstimatedBytes;

        internal HistoryEntry(LayoutState before, LayoutState after, long[] currency, IEnumerable<InventoryChange> inventory)
        {
            Before = before;
            After = after;
            Currency = currency;
            Inventory = inventory.ToArray();
            RetainedEstimatedBytes = after.EstimatedDeltaBytes + Currency.Length * 8L + Inventory.Length * 48L + 64L;
        }

        internal static bool TryCreate(LayoutState before, LayoutState after, decimal[] currencyDelta, IEnumerable<InventoryChange> inventory, out HistoryEntry entry)
        {
            entry = null;
            if (currencyDelta == null) return false;
            var converted = new long[currencyDelta.Length];
            for (int i = 0; i < currencyDelta.Length; i++)
            {
                decimal value = currencyDelta[i];
                // Must fit in [-long.MaxValue, long.MaxValue] so both redo (+delta) and undo (-delta) are representable.
                if (value < -long.MaxValue || value > long.MaxValue || decimal.Truncate(value) != value)
                    return false;
                converted[i] = (long)value;
            }
            entry = new HistoryEntry(before, after, converted, inventory);
            return true;
        }

        internal bool CanPay(GameKingdom player, bool redo)
        {
            if (player == null || Currency == null) return false;
            for (int i = 0; i < Currency.Length; i++)
            {
                long raw = Currency[i];
                if (!redo && raw == long.MinValue) return false;
                long delta;
                try
                {
                    delta = redo ? raw : checked(-raw);
                }
                catch (OverflowException)
                {
                    return false;
                }

                if (delta < 0)
                {
                    if (delta == long.MinValue) return false;
                    long needed = checked(-delta);
                    if (player.GetCurrency(i) < needed) return false;
                }
                else if (delta > 0 && player.GetCurrency(i) > long.MaxValue - delta)
                {
                    return false;
                }
            }
            return true;
        }

        internal void ApplyModel(bool redo, LayoutState current)
        {
            (redo ? After : Before).RestoreModel(current);
            foreach (var change in Inventory) change.Apply(redo);
            for (int i = 0; i < Currency.Length; i++)
            {
                long raw = Currency[i];
                if (raw == 0) continue;
                if (!redo && raw == long.MinValue)
                    throw new OverflowException("Currency delta is not reversible.");
                long delta = redo ? raw : checked(-raw);
                Before.Player.AddCurrency(i, delta, false);
            }
        }
    }
}
