using System;

namespace YUIFramework
{
    public sealed class UIOperationInProgressException : InvalidOperationException
    {
        public UIOperationInProgressException(
            Type contextType,
            UIOperationId operationId,
            UIOperationKind operationKind)
            : base(
                $"UI context {contextType?.Name ?? "Unknown"} is already executing " +
                $"{operationKind} operation {operationId}.")
        {
            ContextType = contextType;
            OperationId = operationId;
            OperationKind = operationKind;
        }

        public Type ContextType { get; }
        public UIOperationId OperationId { get; }
        public UIOperationKind OperationKind { get; }
    }
}
