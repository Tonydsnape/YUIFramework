using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace YUIFramework
{
    public interface IUIPoolClock
    {
        long Timestamp { get; }
        long Frequency { get; }
    }

    public sealed class StopwatchUIPoolClock : IUIPoolClock
    {
        public long Timestamp => Stopwatch.GetTimestamp();
        public long Frequency => Stopwatch.Frequency;
    }

    public enum UIPoolScopeKind
    {
        Global = 0,
        Scene = 1,
        Module = 2
    }

    public readonly struct UIPoolScope : IEquatable<UIPoolScope>
    {
        private UIPoolScope(Guid id, UIPoolScopeKind kind, string name)
        {
            Id = id;
            Kind = kind;
            Name = name ?? string.Empty;
        }

        public static UIPoolScope Global { get; } =
            new UIPoolScope(Guid.Empty, UIPoolScopeKind.Global, "Global");

        public Guid Id { get; }
        public UIPoolScopeKind Kind { get; }
        public string Name { get; }
        public bool IsValid => Kind == UIPoolScopeKind.Global || Id != Guid.Empty;

        public static UIPoolScope CreateScene(int sceneHandle, string sceneName)
        {
            return new UIPoolScope(
                Guid.NewGuid(),
                UIPoolScopeKind.Scene,
                string.IsNullOrWhiteSpace(sceneName) ? $"Scene {sceneHandle}" : sceneName);
        }

        public static UIPoolScope CreateModule(string moduleName)
        {
            if (string.IsNullOrWhiteSpace(moduleName))
            {
                throw new ArgumentException("A module scope name is required.", nameof(moduleName));
            }

            return new UIPoolScope(Guid.NewGuid(), UIPoolScopeKind.Module, moduleName.Trim());
        }

        public bool Equals(UIPoolScope other) => Id == other.Id && Kind == other.Kind;
        public override bool Equals(object obj) => obj is UIPoolScope other && Equals(other);
        public override int GetHashCode() => (Id.GetHashCode() * 397) ^ (int)Kind;
        public override string ToString() => $"{Kind}:{Name} ({Id})";
        public static bool operator ==(UIPoolScope left, UIPoolScope right) => left.Equals(right);
        public static bool operator !=(UIPoolScope left, UIPoolScope right) => !left.Equals(right);
    }

    public enum UIPoolReturnRejection
    {
        None = 0,
        InvalidArgument = 1,
        InvalidEntry = 2,
        ContextTypeMismatch = 3,
        DuplicateReturn = 4,
        CrossPoolReturn = 5,
        Capacity = 6
    }

    public enum UIPoolEvictionReason
    {
        PerKeyCapacity = 0,
        GlobalCapacity = 1,
        IdleTimeout = 2,
        ScopeReleased = 3,
        LowMemory = 4,
        Invalid = 5,
        Clear = 6,
        PrewarmRollback = 7
    }

    public readonly struct UIPoolEntrySnapshot
    {
        public UIPoolEntrySnapshot(
            Guid entryId,
            Type contextType,
            UIKey key,
            string prefabKey,
            UIPoolScope scope,
            int priority,
            double idleSeconds,
            bool hasResourceLease,
            string leaseSource)
        {
            EntryId = entryId;
            ContextType = contextType;
            Key = key;
            PrefabKey = prefabKey;
            Scope = scope;
            Priority = priority;
            IdleSeconds = idleSeconds;
            HasResourceLease = hasResourceLease;
            LeaseSource = leaseSource ?? string.Empty;
        }

        public Guid EntryId { get; }
        public Type ContextType { get; }
        public UIKey Key { get; }
        public string PrefabKey { get; }
        public UIPoolScope Scope { get; }
        public int Priority { get; }
        public double IdleSeconds { get; }
        public bool HasResourceLease { get; }
        public string LeaseSource { get; }
    }

    public sealed class UIPoolDiagnosticsSnapshot
    {
        public UIPoolDiagnosticsSnapshot(
            IReadOnlyList<UIPoolEntrySnapshot> entries,
            int globalCapacity,
            long hitCount,
            long missCount,
            long evictionCount,
            long prewarmCount,
            long rejectedReturnCount,
            long invalidEntryCount)
        {
            Entries = entries ?? Array.Empty<UIPoolEntrySnapshot>();
            GlobalCapacity = globalCapacity;
            HitCount = hitCount;
            MissCount = missCount;
            EvictionCount = evictionCount;
            PrewarmCount = prewarmCount;
            RejectedReturnCount = rejectedReturnCount;
            InvalidEntryCount = invalidEntryCount;
        }

        public IReadOnlyList<UIPoolEntrySnapshot> Entries { get; }
        public int IdleCount => Entries.Count;
        public int IdleLeasedCount
        {
            get
            {
                var count = 0;
                foreach (var entry in Entries)
                {
                    if (entry.HasResourceLease)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        public int EstimatedInstanceCount => IdleCount;
        public int GlobalCapacity { get; }
        public long HitCount { get; }
        public long MissCount { get; }
        public long EvictionCount { get; }
        public long PrewarmCount { get; }
        public long RejectedReturnCount { get; }
        public long InvalidEntryCount { get; }
    }
}
