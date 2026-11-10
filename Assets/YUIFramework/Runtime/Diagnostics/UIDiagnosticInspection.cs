using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace YUIFramework
{
    public sealed class UIContextDiagnosticSnapshot
    {
        internal UIContextDiagnosticSnapshot(BaseContext context, UIPoolScope scope, string resource, bool lease,
            string key = null)
        {
            InstanceId = context.DiagnosticInstanceId; Page = context.GetType().Name; Key = context.Id ?? key ?? "";
            State = context.State; Layer = context.Layer; SortingOrder = context.SortingLease?.SortingOrder;
            Visibility = context.VisibilityState; IsModal = context.IsModal;
            Scope = context.DiagnosticOperation?.Scope ?? context.DiagnosticScope ?? scope.ToString();
            Resource = resource; HasInstanceLease = lease; OperationId = context.CurrentOperationId;
            LifetimeCanceled = context.LifetimeToken.IsCancellationRequested;
        }
        public long InstanceId { get; }
        public string Page { get; }
        public string Key { get; }
        public UIContextState State { get; }
        public UILayer Layer { get; }
        public int? SortingOrder { get; }
        public UIVisibilityState Visibility { get; }
        public bool IsModal { get; }
        public string Scope { get; }
        public string Resource { get; }
        public bool HasInstanceLease { get; }
        public UIOperationId OperationId { get; }
        public bool LifetimeCanceled { get; }
        public override string ToString() =>
            $"{Page} #{InstanceId} key={Key} {UIDiagnostics.StateName(State)} {Layer}/{SortingOrder} {Visibility} " +
            $"scope={Scope} address={Resource} lease={HasInstanceLease} op={OperationId}";
    }

    public sealed class UISubscriptionDiagnosticSnapshot
    {
        internal UISubscriptionDiagnosticSnapshot(UIMessageSubscription subscription, int duplicates)
        {
            Topic = subscription.MessageName; Payload = subscription.PayloadType.Name;
            OwnerType = subscription.Owner?.GetType().Name ?? "unowned";
            InstanceId = (subscription.Owner as BaseContext)?.DiagnosticInstanceId ?? 0;
            OwnerEnded = UIDiagnostics.IsOwnerEnded(subscription.Owner);
            Handler = subscription.Handler.Method.DeclaringType?.FullName + "." + subscription.Handler.Method.Name;
            Sequence = subscription.Sequence; DuplicateCount = duplicates; Priority = subscription.Priority;
        }
        public string Topic { get; }
        public string Payload { get; }
        public string OwnerType { get; }
        public long InstanceId { get; }
        public string Handler { get; }
        public long Sequence { get; }
        public int Priority { get; }
        public int DuplicateCount { get; }
        public bool OwnerEnded { get; }
    }

    public sealed class UIInputLockDiagnosticSnapshot
    {
        internal UIInputLockDiagnosticSnapshot(UIInputLockInfo input)
        {
            Id = input.Id; OwnerType = input.Owner.GetType().Name;
            InstanceId = (input.Owner as BaseContext)?.DiagnosticInstanceId ?? 0;
            OwnerEnded = UIDiagnostics.IsOwnerEnded(input.Owner);
            Reason = input.Reason;
            AllowedLayers = new List<UILayer>(input.AllowedLayers).AsReadOnly();
        }
        public long Id { get; }
        public string OwnerType { get; }
        public long InstanceId { get; }
        public bool OwnerEnded { get; }
        public string Reason { get; }
        public IReadOnlyList<UILayer> AllowedLayers { get; }
    }

    public sealed class UIScopeDiagnosticSnapshot
    {
        internal UIScopeDiagnosticSnapshot(UIScope scope)
        { Identity = scope.Identity; IsEnded = scope.IsEnded; OperationCount = scope.OperationCount; }
        public UIPoolScope Identity { get; }
        public bool IsEnded { get; }
        public int OperationCount { get; }
    }

    public sealed class UITransitionDiagnosticSnapshot
    {
        internal UITransitionDiagnosticSnapshot(string target, int instance, UIOperationId operation,
            long generation, bool reversing)
        { Target = target; TargetId = instance; OperationId = operation; Generation = generation; IsReversing = reversing; }
        public string Target { get; }
        public int TargetId { get; }
        public UIOperationId OperationId { get; }
        public long Generation { get; }
        public bool IsReversing { get; }
    }

    public sealed class UIDiagnosticIssue
    {
        internal UIDiagnosticIssue(string code, string subject, string detail)
        { Code = code; Subject = subject; Detail = detail; }
        public string Code { get; }
        public string Subject { get; }
        public string Detail { get; }
        public override string ToString() => $"{Code}: {Subject}: {Detail}";
    }

    public sealed class UIDestroyedOwnerSnapshot
    {
        internal UIDestroyedOwnerSnapshot(UIContextDiagnosticSnapshot context, int subscriptions, int locks)
        { Context = context; RemainingSubscriptions = subscriptions; RemainingLocks = locks; }
        public UIContextDiagnosticSnapshot Context { get; }
        public int RemainingSubscriptions { get; }
        public int RemainingLocks { get; }
    }

    public sealed class UIHierarchyDiagnosticSnapshot
    {
        internal UIHierarchyDiagnosticSnapshot(Transform root)
        {
            if (root == null) return;
            foreach (var graphic in root.GetComponentsInChildren<Graphic>(true))
            {
                Graphics++;
                if (!graphic.isActiveAndEnabled) continue;
                ActiveGraphics++;
                if (graphic.raycastTarget) ActiveRaycastTargets++;
            }
            Canvases = root.GetComponentsInChildren<Canvas>(true).Length;
            LayoutGroups = root.GetComponentsInChildren<LayoutGroup>(true).Length;
        }
        public int Graphics { get; }
        public int ActiveGraphics { get; }
        public int ActiveRaycastTargets { get; }
        public int Canvases { get; }
        public int LayoutGroups { get; }
    }

    public sealed class UIDiagnosticSnapshot
    {
        internal UIDiagnosticSnapshot(
            IReadOnlyList<UIContextDiagnosticSnapshot> contexts, IReadOnlyList<long> pages, IReadOnlyList<long> modals,
            IReadOnlyList<UIScopeDiagnosticSnapshot> scopes, UIResourceDiagnosticsSnapshot resources,
            UIPoolDiagnosticsSnapshot pool, IReadOnlyList<UISubscriptionDiagnosticSnapshot> subscriptions,
            IReadOnlyList<UIInputLockDiagnosticSnapshot> locks, IReadOnlyList<UITransitionDiagnosticSnapshot> animations,
            IReadOnlyList<UIDiagnosticOperationSnapshot> operations, IReadOnlyList<UIDiagnosticEvent> events,
            IReadOnlyList<UIDestroyedOwnerSnapshot> destroyed, IReadOnlyList<UIDiagnosticIssue> issues,
            string focus, UIHierarchyDiagnosticSnapshot hierarchy, bool recordingComplete, long droppedEvents)
        {
            Contexts = contexts; Navigation = pages; Modals = modals; Scopes = scopes; Resources = resources;
            Pool = pool; Subscriptions = subscriptions; InputLocks = locks; Transitions = animations;
            Operations = operations; Events = events; Destroyed = destroyed; Issues = issues;
            Focus = focus; Hierarchy = hierarchy;
            RecordingComplete = recordingComplete; DroppedEvents = droppedEvents;
        }
        public IReadOnlyList<UIContextDiagnosticSnapshot> Contexts { get; }
        public IReadOnlyList<long> Navigation { get; }
        public IReadOnlyList<long> Modals { get; }
        public IReadOnlyList<UIScopeDiagnosticSnapshot> Scopes { get; }
        public UIResourceDiagnosticsSnapshot Resources { get; }
        public UIPoolDiagnosticsSnapshot Pool { get; }
        public IReadOnlyList<UISubscriptionDiagnosticSnapshot> Subscriptions { get; }
        public IReadOnlyList<UIInputLockDiagnosticSnapshot> InputLocks { get; }
        public IReadOnlyList<UITransitionDiagnosticSnapshot> Transitions { get; }
        public IReadOnlyList<UIDiagnosticOperationSnapshot> Operations { get; }
        public IReadOnlyList<UIDiagnosticEvent> Events { get; }
        public IReadOnlyList<UIDestroyedOwnerSnapshot> Destroyed { get; }
        public IReadOnlyList<UIDiagnosticIssue> Issues { get; }
        public string Focus { get; }
        public UIHierarchyDiagnosticSnapshot Hierarchy { get; }
        public bool RecordingComplete { get; }
        public long DroppedEvents { get; }
    }

    public sealed partial class UIDiagnostics
    {
        private readonly Queue<UIDestroyedOwnerSnapshot> _destroyed = new Queue<UIDestroyedOwnerSnapshot>();
        internal static bool IsOwnerEnded(object owner) =>
            owner is BaseContext context ? context.LifetimeToken.IsCancellationRequested :
            owner is UIScope scope && scope.IsEnded;

        internal void RecordDestroyed(BaseContext context, UIManager ui)
        {
            if (!Enabled || _disposed) return;
            var subscriptions = ui.MessageCenter?.CountDiagnosticOwner(context) ?? 0;
            var locks = 0;
            if (ui.InputLocks != null)
                foreach (var item in ui.InputLocks.ActiveLocks)
                    if (ReferenceEquals(item.Owner, context)) locks++;
            if (_destroyed.Count >= _activeCapacity) _destroyed.Dequeue();
            _destroyed.Enqueue(new UIDestroyedOwnerSnapshot(ui.CaptureContext(context), subscriptions, locks));
            context.DiagnosticOperation?.Mark($"Destroyed:subscriptions={subscriptions};locks={locks}");
        }

        public UIDiagnosticSnapshot Capture(UIManager ui, UIPerformanceBudget budget = null, bool includeHierarchy = false)
        {
            if (ui == null) throw new ArgumentNullException(nameof(ui));
            if (!ReferenceEquals(ui.Diagnostics, this))
                throw new InvalidOperationException("Capture requires the diagnostics instance injected into this UIManager.");
            var contexts = new List<UIContextDiagnosticSnapshot>();
            var live = new HashSet<BaseContext>(ui.GetDiagnosticContexts());
            foreach (var span in _active.Values)
                if (span.Owner != null) live.Add(span.Owner);
            foreach (var context in live) contexts.Add(ui.CaptureContext(context));
            var navigation = new List<long>();
            if (ui.Navigator != null)
                foreach (var page in ui.Navigator.GetDiagnosticStack()) navigation.Add(page.DiagnosticInstanceId);
            var modals = new List<long>();
            if (ui.Modals != null)
                foreach (var modal in ui.Modals.GetDiagnosticStack()) modals.Add(modal.DiagnosticInstanceId);
            var scopes = new List<UIScopeDiagnosticSnapshot>();
            if (ui.Scopes != null)
                foreach (var scope in ui.Scopes.GetDiagnosticScopes()) scopes.Add(new UIScopeDiagnosticSnapshot(scope));
            var subscriptions = ui.MessageCenter?.GetDiagnosticSubscriptions() ??
                Array.AsReadOnly(Array.Empty<UISubscriptionDiagnosticSnapshot>());
            var locks = new List<UIInputLockDiagnosticSnapshot>();
            if (ui.InputLocks != null)
                foreach (var item in ui.InputLocks.ActiveLocks) locks.Add(new UIInputLockDiagnosticSnapshot(item));
            var operations = GetOperations();
            var issues = new List<UIDiagnosticIssue>();
            foreach (var item in subscriptions)
            {
                if (item.DuplicateCount > 1)
                    issues.Add(new UIDiagnosticIssue("DuplicateSubscription", item.Topic,
                        $"owner={item.OwnerType}#{item.InstanceId} handler={item.Handler} count={item.DuplicateCount}"));
                if (item.OwnerEnded)
                    issues.Add(new UIDiagnosticIssue("OwnerSubscriptionLeak", item.Topic,
                        $"ended owner={item.OwnerType}#{item.InstanceId} sequence={item.Sequence}"));
            }
            foreach (var item in locks)
                if (item.OwnerEnded) issues.Add(new UIDiagnosticIssue("OwnerLockLeak", item.Reason,
                    $"ended owner={item.OwnerType}#{item.InstanceId} lock={item.Id}"));
            foreach (var operation in operations)
            {
                if (operation.OwnerEnded && operation.Kind != "Close" && operation.Kind != "Release")
                    issues.Add(new UIDiagnosticIssue(operation.Kind == "Lease" ? "OwnerLeaseLeak" : "OwnerWorkPending",
                        operation.Page + "#" + operation.InstanceId, $"op={operation.OperationId} {operation.Kind} " +
                        $"phase={operation.Phase} address={operation.Resource}; not declared cleaned"));
                if (budget != null && operation.Kind != "Lease" &&
                    operation.ElapsedMs > budget.OperationTimeoutMs)
                    issues.Add(new UIDiagnosticIssue("OperationTimeout", operation.Page,
                        $"op={operation.OperationId} {operation.Kind}/{operation.Phase} " +
                        $"{operation.ElapsedMs:F3}ms > {budget.OperationTimeoutMs:F3}ms; scope={operation.Scope}"));
            }
            var complete = Enabled && !_disposed && DroppedOperations == 0 && ClockFaultCount == 0;
            if (!complete)
                issues.Add(new UIDiagnosticIssue("RecordingIncomplete", "Diagnostics",
                    $"enabled={Enabled && !_disposed} droppedOperations={DroppedOperations} clockFaults={ClockFaultCount}"));
            var root = ui.RootRuntime?.Root?.transform;
            return new UIDiagnosticSnapshot(contexts.AsReadOnly(), navigation.AsReadOnly(), modals.AsReadOnly(),
                scopes.AsReadOnly(), ui.ResourceService?.GetDiagnostics(), ui.PoolDiagnostics,
                subscriptions, locks.AsReadOnly(), ui.Transitions?.GetDiagnostics() ??
                    Array.AsReadOnly(Array.Empty<UITransitionDiagnosticSnapshot>()), operations,
                GetEvents(), Array.AsReadOnly(_destroyed.ToArray()), issues.AsReadOnly(),
                ui.RootRuntime?.EventSystem?.currentSelectedGameObject?.name ?? "(none)",
                includeHierarchy && root != null ? new UIHierarchyDiagnosticSnapshot(root) : null,
                complete, DroppedEvents);
        }
    }
}
