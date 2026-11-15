using System;
using System.Collections.Generic;
using System.Linq;
using KingdomFoundation.Api;

namespace KingdomIncUndo
{
    internal sealed class WorkspaceSnapshot
    {
        internal readonly WorkspaceObject[] Elements;
        internal readonly WorkspacePipe[] Pipes;
        internal readonly WorkspaceObject[] Decos;
        internal readonly Dictionary<WorkspaceObject, ElementState> ElementData;
        internal readonly Dictionary<WorkspacePipe, PipeState> PipeData;
        internal readonly Dictionary<WorkspaceObject, DecoState> DecoData;
        internal readonly Dictionary<WorkspaceObject, Dictionary<string, object>> ExtensionData;

        private WorkspaceSnapshot(
            WorkspaceObject[] elements,
            WorkspacePipe[] pipes,
            WorkspaceObject[] decos,
            Dictionary<WorkspaceObject, ElementState> elementData,
            Dictionary<WorkspacePipe, PipeState> pipeData,
            Dictionary<WorkspaceObject, DecoState> decoData,
            Dictionary<WorkspaceObject, Dictionary<string, object>> extensionData)
        {
            Elements = elements;
            Pipes = pipes;
            Decos = decos;
            ElementData = elementData;
            PipeData = pipeData;
            DecoData = decoData;
            ExtensionData = extensionData;
        }

        internal static WorkspaceSnapshot Capture(GameWorkspace flow, WorkspaceSnapshot previous, out long unsharedBytes)
        {
            unsharedBytes = 0;
            var buildings = flow.Buildings;
            var livePipes = flow.Pipes;
            var liveDecos = flow.Decorations;

            if (previous != null && CanReuseWholeSnapshot(flow, previous))
                return previous;

            var elements = buildings.ToArray();
            var pipes = livePipes.ToArray();
            var decos = liveDecos.ToArray();
            unsharedBytes += (elements.Length + pipes.Length + decos.Length) * 8L + 256L;

            var elementData = new Dictionary<WorkspaceObject, ElementState>(elements.Length);
            var pipeData = new Dictionary<WorkspacePipe, PipeState>(pipes.Length);
            var decoData = new Dictionary<WorkspaceObject, DecoState>(decos.Length);
            var extData = UndoApi.HasExtensions
                ? new Dictionary<WorkspaceObject, Dictionary<string, object>>(elements.Length + decos.Length)
                : null;

            foreach (var element in elements)
            {
                if (previous != null && previous.ElementData.TryGetValue(element, out var prevState) && prevState.MatchesLive(element))
                {
                    elementData.Add(element, prevState);
                }
                else
                {
                    var state = new ElementState(element);
                    elementData.Add(element, state);
                    unsharedBytes += state.EstimatedBytes;
                }
                CaptureObjectExtensions(element, previous, extData, ref unsharedBytes);
            }

            foreach (var pipe in pipes)
            {
                if (previous != null && previous.PipeData.TryGetValue(pipe, out var prevState) && prevState.MatchesLive(pipe))
                {
                    pipeData.Add(pipe, prevState);
                }
                else
                {
                    var state = new PipeState(pipe);
                    pipeData.Add(pipe, state);
                    unsharedBytes += state.EstimatedBytes;
                }
            }

            foreach (var deco in decos)
            {
                if (previous != null && previous.DecoData.TryGetValue(deco, out var prevState) && prevState.MatchesLive(deco))
                {
                    decoData.Add(deco, prevState);
                }
                else
                {
                    var state = new DecoState(deco);
                    decoData.Add(deco, state);
                    unsharedBytes += state.EstimatedBytes;
                }
                CaptureObjectExtensions(deco, previous, extData, ref unsharedBytes);
            }

            return new WorkspaceSnapshot(elements, pipes, decos, elementData, pipeData, decoData, extData);
        }

        private static bool CanReuseWholeSnapshot(GameWorkspace flow, WorkspaceSnapshot previous)
        {
            if (UndoApi.HasExtensions && previous.ExtensionData == null) return false;
            if (!UndoApi.HasExtensions && previous.ExtensionData != null && previous.ExtensionData.Count > 0) return false;

            var buildings = flow.Buildings;
            if (buildings.Count != previous.Elements.Length) return false;
            for (int i = 0; i < buildings.Count; i++)
            {
                var b = buildings[i];
                if (!Equals(b, previous.Elements[i])) return false;
                if (!previous.ElementData[b].MatchesLive(b)) return false;
                Dictionary<string, object> prevExt = null;
                previous.ExtensionData?.TryGetValue(b, out prevExt);
                if (!UndoApi.MatchesPrevious(b, prevExt)) return false;
            }

            var pipes = flow.Pipes;
            if (pipes.Count != previous.Pipes.Length) return false;
            for (int i = 0; i < pipes.Count; i++)
            {
                var p = pipes[i];
                if (!Equals(p, previous.Pipes[i])) return false;
                if (!previous.PipeData[p].MatchesLive(p)) return false;
            }

            var decos = flow.Decorations;
            if (decos.Count != previous.Decos.Length) return false;
            for (int i = 0; i < decos.Count; i++)
            {
                var d = decos[i];
                if (!Equals(d, previous.Decos[i])) return false;
                if (!previous.DecoData[d].MatchesLive(d)) return false;
                Dictionary<string, object> prevExt = null;
                previous.ExtensionData?.TryGetValue(d, out prevExt);
                if (!UndoApi.MatchesPrevious(d, prevExt)) return false;
            }

            return true;
        }

        private static void CaptureObjectExtensions(
            WorkspaceObject item,
            WorkspaceSnapshot previous,
            Dictionary<WorkspaceObject, Dictionary<string, object>> target,
            ref long unsharedBytes)
        {
            if (target == null) return;
            Dictionary<string, object> prevExt = null;
            if (previous?.ExtensionData != null && previous.ExtensionData.TryGetValue(item, out prevExt) && UndoApi.MatchesPrevious(item, prevExt))
            {
                target.Add(item, prevExt);
                return;
            }
            var captured = UndoApi.CaptureExtensions(item);
            if (captured != null)
            {
                target.Add(item, captured);
                unsharedBytes += 64L + captured.Count * 32L;
            }
        }

        internal bool SameWorkspace(WorkspaceSnapshot other)
        {
            if (ReferenceEquals(this, other)) return true;
            if (!Elements.SequenceEqual(other.Elements) || !Pipes.SequenceEqual(other.Pipes) || !Decos.SequenceEqual(other.Decos))
                return false;
            foreach (var pair in ElementData)
            {
                if (!pair.Value.Same(other.ElementData[pair.Key])) return false;
                if (!ObjectExtensionsEqual(other, pair.Key, out _)) return false;
            }
            foreach (var pair in PipeData)
                if (!pair.Value.Same(other.PipeData[pair.Key])) return false;
            foreach (var pair in DecoData)
            {
                if (!pair.Value.Same(other.DecoData[pair.Key])) return false;
                if (!ObjectExtensionsEqual(other, pair.Key, out _)) return false;
            }
            return true;
        }

        internal bool ObjectExtensionsEqual(WorkspaceSnapshot other, WorkspaceObject item, out bool affectsConnectedPipes)
        {
            Dictionary<string, object> left = null;
            Dictionary<string, object> right = null;
            ExtensionData?.TryGetValue(item, out left);
            other?.ExtensionData?.TryGetValue(item, out right);
            return UndoApi.ExtensionsEqual(left, right, out affectsConnectedPipes);
        }

        internal IEnumerable<WorkspacePort> IO => ElementData.Values.SelectMany(e => e.Inputs.Cast<WorkspacePort>().Concat(e.Outputs));
    }
}

