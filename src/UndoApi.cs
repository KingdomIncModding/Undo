using System;
using System.Collections.Generic;
using KingdomFoundation.Api;

namespace KingdomIncUndo
{
    /// <summary>Public extension and edit-scope integration points owned by Undo.</summary>
    public static class UndoApi
    {
        private sealed class ExtensionRegistration
        {
            internal readonly string Id;
            internal readonly Func<WorkspaceObject, object> Capture;
            internal readonly Action<WorkspaceObject, object> Restore;
            internal readonly Func<object, object, bool> EqualsFn;
            internal readonly bool AffectsConnectedPipeEndpoints;

            internal ExtensionRegistration(
                string id,
                Func<WorkspaceObject, object> capture,
                Action<WorkspaceObject, object> restore,
                Func<object, object, bool> equals,
                bool affectsConnectedPipeEndpoints)
            {
                Id = id;
                Capture = capture;
                Restore = restore;
                EqualsFn = equals ?? ((left, right) => object.Equals(left, right));
                AffectsConnectedPipeEndpoints = affectsConnectedPipeEndpoints;
            }
        }

        private sealed class DelegateDisposable : IDisposable
        {
            private Action dispose;
            internal DelegateDisposable(Action dispose) => this.dispose = dispose;
            public void Dispose()
            {
                var action = dispose;
                dispose = null;
                action?.Invoke();
            }
        }

        private sealed class EditScopeHandle : IDisposable
        {
            private bool active;
            private bool disposed;

            internal EditScopeHandle(bool active) => this.active = active;

            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                if (!active) return;
                active = false;
                UndoPlugin.Instance?.End(null);
            }
        }

        private static readonly List<ExtensionRegistration> Extensions = new List<ExtensionRegistration>();

        internal static bool HasExtensions => Extensions.Count > 0;

        /// <summary>Registers capture and restore callbacks for mod-owned workspace object state.</summary>
        public static IDisposable RegisterObjectExtension(
            string extensionId,
            Func<WorkspaceObject, object> capture,
            Action<WorkspaceObject, object> restore,
            Func<object, object, bool> equals = null)
            => RegisterObjectExtension(extensionId, capture, restore, equals, false);

        /// <summary>Registers capture and restore callbacks for mod-owned workspace object state, optionally refreshing connected pipe endpoints on replay.</summary>
        public static IDisposable RegisterObjectExtension(
            string extensionId,
            Func<WorkspaceObject, object> capture,
            Action<WorkspaceObject, object> restore,
            Func<object, object, bool> equals,
            bool affectsConnectedPipeEndpoints)
        {
            if (string.IsNullOrWhiteSpace(extensionId))
                throw new ArgumentException("Extension id is required.", nameof(extensionId));
            if (capture == null)
                throw new ArgumentNullException(nameof(capture));
            if (restore == null)
                throw new ArgumentNullException(nameof(restore));

            for (int i = Extensions.Count - 1; i >= 0; i--)
            {
                if (Extensions[i].Id == extensionId)
                    throw new InvalidOperationException("An Undo extension is already registered for: " + extensionId);
            }

            var entry = new ExtensionRegistration(extensionId, capture, restore, equals, affectsConnectedPipeEndpoints);
            Extensions.Add(entry);
            return new DelegateDisposable(() =>
            {
                Extensions.Remove(entry);
            });
        }

        /// <summary>Starts or joins a synchronous Undo edit scope.</summary>
        public static bool TryBeginEditScope() => UndoPlugin.Instance != null && UndoPlugin.Instance.Begin();

        /// <summary>Completes or aborts a synchronous Undo edit scope.</summary>
        public static void EndEditScope(Exception error = null) => UndoPlugin.Instance?.End(error);

        /// <summary>Opens a disposable Undo edit scope for clean single-action recording around synchronous edits.</summary>
        public static IDisposable OpenEditScope() => new EditScopeHandle(TryBeginEditScope());

        /// <summary>Executes an edit action inside an atomic Undo scope and aborts recording if it throws.</summary>
        public static void RunEdit(Action edit)
        {
            if (edit == null) throw new ArgumentNullException(nameof(edit));
            bool active = TryBeginEditScope();
            Exception error = null;
            try
            {
                edit();
            }
            catch (Exception ex)
            {
                error = ex;
                throw;
            }
            finally
            {
                if (active) EndEditScope(error);
            }
        }

        internal static void ClearAllExtensions() => Extensions.Clear();

        internal static bool MatchesPrevious(WorkspaceObject item, Dictionary<string, object> previous)
        {
            if (Extensions.Count == 0)
                return previous == null || previous.Count == 0;
            if (previous == null || previous.Count != Extensions.Count)
                return false;
            for (int i = 0; i < Extensions.Count; i++)
            {
                var ext = Extensions[i];
                if (!previous.TryGetValue(ext.Id, out var prevValue))
                    return false;
                var liveValue = ext.Capture(item);
                if (!ext.EqualsFn(prevValue, liveValue))
                    return false;
            }
            return true;
        }

        internal static Dictionary<string, object> CaptureExtensions(WorkspaceObject item)
        {
            if (Extensions.Count == 0)
                return null;
            var result = new Dictionary<string, object>(Extensions.Count, StringComparer.Ordinal);
            for (int i = 0; i < Extensions.Count; i++)
            {
                var ext = Extensions[i];
                result[ext.Id] = ext.Capture(item);
            }
            return result;
        }

        internal static bool ExtensionsEqual(Dictionary<string, object> left, Dictionary<string, object> right, out bool affectsConnectedPipes)
        {
            affectsConnectedPipes = false;
            if (ReferenceEquals(left, right))
                return true;
            if (Extensions.Count == 0)
                return (left == null || left.Count == 0) && (right == null || right.Count == 0);
            if (left == null || right == null || left.Count != right.Count)
            {
                for (int i = 0; i < Extensions.Count; i++)
                    if (Extensions[i].AffectsConnectedPipeEndpoints) affectsConnectedPipes = true;
                return false;
            }

            bool same = true;
            for (int i = 0; i < Extensions.Count; i++)
            {
                var ext = Extensions[i];
                bool hasLeft = left.TryGetValue(ext.Id, out var leftVal);
                bool hasRight = right.TryGetValue(ext.Id, out var rightVal);
                if (hasLeft != hasRight || (hasLeft && !ext.EqualsFn(leftVal, rightVal)))
                {
                    same = false;
                    if (ext.AffectsConnectedPipeEndpoints)
                        affectsConnectedPipes = true;
                }
            }
            return same;
        }

        internal static void RestoreExtensions(WorkspaceObject item, Dictionary<string, object> values)
        {
            if (Extensions.Count == 0 || values == null)
                return;
            for (int i = 0; i < Extensions.Count; i++)
            {
                var ext = Extensions[i];
                if (values.TryGetValue(ext.Id, out var value))
                    ext.Restore(item, value);
            }
        }
    }
}

