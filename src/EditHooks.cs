using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using KingdomFoundation.Api;

namespace KingdomIncUndo
{
    internal static class EditHooks
    {
        internal static IEnumerable<IDisposable> Register()
        {
            yield return FoundationApi.ExecutionHooks.Subscribe(UndoPlugin.Id,GameExecutionPoints.WorkspaceEdit,Prefix,Finalizer);
            yield return FoundationApi.ExecutionHooks.Subscribe(UndoPlugin.Id,GameExecutionPoints.DeconstructionUpdate,Prefix,Finalizer);
            // Optional integration targets the other mod, never a native game type.
            var batchDelete=AppDomain.CurrentDomain.GetAssemblies().Select(assembly=>assembly.GetType("KingdomBetterDeconstruct.DeconstructSelection",false)).FirstOrDefault(type=>type!=null);
            var method=batchDelete?.GetMethod("ExecuteDeconstruct",BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance);
            if(method!=null)yield return FoundationApi.ExecutionHooks.SubscribeExternal(UndoPlugin.Id,method,Prefix,Finalizer);
        }
        private static object Prefix(GameExecutionCall call)
        {
            if(call.HookId==GameExecutionPoints.DeconstructionUpdate && !FoundationApi.UiAccess.InteractReleased)return false;
            return UndoPlugin.Instance != null && UndoPlugin.Instance.Begin();
        }
        private static void Finalizer(GameExecutionCall call,object state)
        { if(state is bool active && active)UndoPlugin.Instance?.End(call.Exception); }
    }
}
