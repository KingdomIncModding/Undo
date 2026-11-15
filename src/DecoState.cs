using System;
using KingdomFoundation.Api;
using KingdomFoundation.Json;
using UnityEngine;

namespace KingdomIncUndo
{
    internal sealed class DecoState
    {
        internal readonly Vector2Int Position;
        internal readonly Vector2 Size;
        internal readonly int StyleIndex;
        internal readonly string Label;
        internal readonly bool Locked;
        internal readonly bool IsParent;
        internal readonly bool HasSticker;
        internal readonly string StickerIconName;
        private readonly string stickerJson;

        internal DecoState(WorkspaceObject deco)
        {
            Position = deco.Position;
            Size = deco.Size;
            StyleIndex = deco.StyleIndex;
            Label = deco.Label;
            Locked = deco.Locked;
            IsParent = deco.IsAnchoredParent;
            HasSticker = deco.HasSticker;
            StickerIconName = deco.StickerIconName;
            stickerJson = HasSticker ? FoundationApi.WorkspaceAccess.Serialize(deco).ToString() : null;
        }

        internal long EstimatedBytes => 96 + (Label?.Length ?? 0) * 2 + (StickerIconName?.Length ?? 0) * 2 + (stickerJson?.Length ?? 0) * 2;

        internal bool MatchesLive(WorkspaceObject deco)
        {
            if (Position != deco.Position || Size != deco.Size || StyleIndex != deco.StyleIndex ||
                Label != deco.Label || Locked != deco.Locked || IsParent != deco.IsAnchoredParent ||
                HasSticker != deco.HasSticker || StickerIconName != deco.StickerIconName)
                return false;
            return !HasSticker || stickerJson == FoundationApi.WorkspaceAccess.Serialize(deco).ToString();
        }

        internal bool Same(DecoState other)
        {
            if (ReferenceEquals(this, other)) return true;
            if (other == null) return false;
            return Position == other.Position && Size == other.Size && StyleIndex == other.StyleIndex &&
                   Label == other.Label && Locked == other.Locked && IsParent == other.IsParent &&
                   HasSticker == other.HasSticker && StickerIconName == other.StickerIconName &&
                   stickerJson == other.stickerJson;
        }

        internal void Restore(WorkspaceObject deco)
        {
            if (stickerJson != null)
            {
                deco.ApplyDecorationData(JsonValue.Parse(stickerJson));
                return;
            }
            var json = new JsonObject()
                .Add("Position", new JsonArray().Add(Position.x).Add(Position.y))
                .Add("Size", new JsonArray().Add((int)Math.Round(Size.x)).Add((int)Math.Round(Size.y)))
                .Add("StyleIndex", StyleIndex);
            if (Label != null) json.Add("Label", Label);
            if (Locked) json.Add("Locked", true);
            if (IsParent) json.Add("IsParent", true);
            deco.ApplyDecorationData(json);
        }
    }
}

