using System;

namespace YUIFramework
{
    public sealed class UILifecycleException : Exception
    {
        public UILifecycleException(
            Type contextType,
            UIContextState state,
            UIOperationId operationId,
            UIOperationKind operationKind,
            string phase,
            Exception innerException)
            : base(
                $"UI lifecycle failed: context={contextType?.Name ?? "Unknown"}, " +
                $"state={state}, operation={operationKind}#{operationId}, phase={phase}.",
                innerException)
        {
            ContextType = contextType;
            State = state;
            OperationId = operationId;
            OperationKind = operationKind;
            Phase = phase;
        }

        public Type ContextType { get; }
        public UIContextState State { get; }
        public UIOperationId OperationId { get; }
        public UIOperationKind OperationKind { get; }
        public string Phase { get; }
    }
}
