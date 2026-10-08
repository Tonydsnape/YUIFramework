using System;
using System.Collections.Generic;

namespace YUIFramework
{
    public sealed class UIObjectPool : IUIObjectPool
    {
        private readonly Dictionary<Type, List<UIPooledObject>> _buckets =
            new Dictionary<Type, List<UIPooledObject>>();
        private readonly IUIPoolClock _clock;
        private readonly Guid _poolId = Guid.NewGuid();
        private readonly int _globalCapacity;
        private long _hitCount;
        private long _missCount;
        private long _evictionCount;
        private long _prewarmCount;
        private long _rejectedReturnCount;
        private long _invalidEntryCount;
        private int _count;

        public UIObjectPool(int globalCapacity = 128, IUIPoolClock clock = null)
        {
            if (globalCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(globalCapacity),
                    "Global pool capacity must be positive.");
            }

            _globalCapacity = globalCapacity;
            _clock = clock ?? new StopwatchUIPoolClock();
            if (_clock.Frequency <= 0)
            {
                throw new ArgumentException("Pool clock frequency must be positive.", nameof(clock));
            }
        }

        public bool TryGet(Type contextType, out UIPooledObject pooledObject)
        {
            return TryGet(contextType, UIPoolScope.Global, null, out pooledObject);
        }

        public bool TryGet(
            Type contextType,
            UIPoolScope scope,
            Action<UIPooledObject> invalidAction,
            out UIPooledObject pooledObject)
        {
            pooledObject = null;
            if (contextType == null || !scope.IsValid)
            {
                _missCount++;
                return false;
            }

            EvictExpired(invalidAction);
            if (!_buckets.TryGetValue(contextType, out var bucket))
            {
                _missCount++;
                return false;
            }

            for (var index = bucket.Count - 1; index >= 0; index--)
            {
                var candidate = bucket[index];
                if (candidate == null || !candidate.IsValid)
                {
                    RemoveAt(bucket, index, candidate);
                    _invalidEntryCount++;
                    _evictionCount++;
                    invalidAction?.Invoke(candidate);
                    continue;
                }

                if (candidate.Scope != scope)
                {
                    continue;
                }

                RemoveAt(bucket, index, candidate, preserveOwnership: true);
                candidate.IsIdle = false;
                pooledObject = candidate;
                break;
            }

            RemoveEmptyBucket(contextType, bucket);
            if (pooledObject == null)
            {
                _missCount++;
                return false;
            }

            _hitCount++;
            return true;
        }

        public bool TryRelease(
            Type contextType,
            UIPooledObject pooledObject,
            UIPoolPolicy policy,
            out UIPooledObject overflowObject)
        {
            if (policy != null &&
                (Count(contextType) >= policy.MaxPoolSize || _count >= _globalCapacity))
            {
                overflowObject = pooledObject;
                _rejectedReturnCount++;
                return false;
            }

            return TryRelease(
                contextType,
                pooledObject,
                policy,
                UIPoolScope.Global,
                "Open",
                false,
                out overflowObject,
                out _);
        }

        public bool TryRelease(
            Type contextType,
            UIPooledObject pooledObject,
            UIPoolPolicy policy,
            UIPoolScope scope,
            string leaseSource,
            bool hasResourceLease,
            out UIPooledObject overflowObject,
            out UIPoolReturnRejection rejection)
        {
            overflowObject = null;
            rejection = ValidateReturn(contextType, pooledObject, policy, scope);
            if (rejection != UIPoolReturnRejection.None)
            {
                _rejectedReturnCount++;
                if (rejection == UIPoolReturnRejection.InvalidEntry)
                {
                    _invalidEntryCount++;
                }

                overflowObject = pooledObject;
                return false;
            }

            var previousOwner = pooledObject.OwnerPoolId;
            pooledObject.Scope = scope;
            pooledObject.Priority = policy.Priority;
            pooledObject.IdleTimeoutSeconds = policy.IdleTimeoutSeconds;
            pooledObject.CachedTimestamp = _clock.Timestamp;
            pooledObject.LeaseSource = string.IsNullOrWhiteSpace(leaseSource)
                ? "Unknown"
                : leaseSource.Trim();
            pooledObject.HasResourceLease = hasResourceLease;
            pooledObject.IsIdle = true;

            if (!_buckets.TryGetValue(contextType, out var bucket))
            {
                bucket = new List<UIPooledObject>(Math.Max(1, policy.MaxPoolSize));
                _buckets.Add(contextType, bucket);
            }

            if (bucket.Count >= policy.MaxPoolSize)
            {
                var victimIndex = FindEvictionCandidate(bucket);
                if (victimIndex < 0 ||
                    CompareEvictionOrder(pooledObject, bucket[victimIndex]) <= 0)
                {
                    pooledObject.IsIdle = false;
                    pooledObject.OwnerPoolId = previousOwner;
                    overflowObject = pooledObject;
                    rejection = UIPoolReturnRejection.Capacity;
                    _rejectedReturnCount++;
                    return false;
                }

                overflowObject = bucket[victimIndex];
                RemoveAt(bucket, victimIndex, overflowObject);
                _evictionCount++;
            }

            if (_count >= _globalCapacity)
            {
                var globalVictim = FindGlobalEvictionCandidate();
                if (globalVictim == null ||
                    CompareEvictionOrder(pooledObject, globalVictim) <= 0)
                {
                    pooledObject.IsIdle = false;
                    pooledObject.OwnerPoolId = previousOwner;
                    if (overflowObject != null)
                    {
                        AddExisting(bucket, overflowObject);
                        overflowObject = null;
                        _evictionCount--;
                    }

                    overflowObject = pooledObject;
                    rejection = UIPoolReturnRejection.Capacity;
                    _rejectedReturnCount++;
                    return false;
                }

                RemoveExisting(globalVictim);
                if (overflowObject == null)
                {
                    overflowObject = globalVictim;
                }
                else
                {
                    AddExisting(bucket, overflowObject);
                    overflowObject = globalVictim;
                }

                _evictionCount++;
            }

            pooledObject.OwnerPoolId = _poolId;
            bucket.Add(pooledObject);
            _count++;
            if (string.Equals(pooledObject.LeaseSource, "Prewarm", StringComparison.Ordinal))
            {
                _prewarmCount++;
            }

            return true;
        }

        public void Clear(Action<UIPooledObject> destroyAction = null)
        {
            var entries = CollectEntries(_ => true);
            InvokeAll(entries, destroyAction);
        }

        public void Clear(Type contextType, Action<UIPooledObject> destroyAction = null)
        {
            if (contextType == null)
            {
                return;
            }

            var entries = CollectEntries(entry => entry.ContextType == contextType);
            InvokeAll(entries, destroyAction);
        }

        public int ClearScope(UIPoolScope scope, Action<UIPooledObject> destroyAction = null)
        {
            if (!scope.IsValid || scope.Kind == UIPoolScopeKind.Global)
            {
                return 0;
            }

            var entries = CollectEntries(entry => entry.Scope == scope);
            _evictionCount += entries.Count;
            InvokeAll(entries, destroyAction);
            return entries.Count;
        }

        public int EvictExpired(Action<UIPooledObject> destroyAction = null)
        {
            var now = _clock.Timestamp;
            var entries = CollectEntries(entry =>
                entry.IdleTimeoutSeconds > 0 &&
                ToSeconds(now - entry.CachedTimestamp) >= entry.IdleTimeoutSeconds);
            _evictionCount += entries.Count;
            InvokeAll(entries, destroyAction);
            return entries.Count;
        }

        public int EvictIdle(Action<UIPooledObject> destroyAction = null)
        {
            var entries = CollectEntries(_ => true);
            entries.Sort(CompareEvictionOrder);
            _evictionCount += entries.Count;
            InvokeAll(entries, destroyAction);
            return entries.Count;
        }

        public bool Remove(UIPooledObject pooledObject)
        {
            return RemoveExisting(pooledObject);
        }

        public int RemoveInvalid(Action<UIPooledObject> removeAction = null)
        {
            var entries = CollectEntries(entry => entry == null || !entry.IsValid);
            _invalidEntryCount += entries.Count;
            _evictionCount += entries.Count;
            InvokeAll(entries, removeAction);
            return entries.Count;
        }

        public int Count(Type contextType)
        {
            return contextType != null && _buckets.TryGetValue(contextType, out var bucket)
                ? bucket.Count
                : 0;
        }

        public int Count(Type contextType, UIPoolScope scope)
        {
            if (contextType == null || !_buckets.TryGetValue(contextType, out var bucket))
            {
                return 0;
            }

            var count = 0;
            foreach (var entry in bucket)
            {
                if (entry != null && entry.Scope == scope)
                {
                    count++;
                }
            }

            return count;
        }

        public UIPoolDiagnosticsSnapshot GetDiagnostics()
        {
            var now = _clock.Timestamp;
            var entries = new List<UIPoolEntrySnapshot>(_count);
            foreach (var bucket in _buckets.Values)
            {
                foreach (var entry in bucket)
                {
                    if (entry == null)
                    {
                        continue;
                    }

                    entries.Add(new UIPoolEntrySnapshot(
                        entry.EntryId,
                        entry.ContextType,
                        entry.Context == null || string.IsNullOrWhiteSpace(entry.Context.Id)
                            ? default
                            : new UIKey(entry.Context.Id),
                        entry.PrefabKey,
                        entry.Scope,
                        entry.Priority,
                        Math.Max(0, ToSeconds(now - entry.CachedTimestamp)),
                        entry.HasResourceLease,
                        entry.LeaseSource));
                }
            }

            return new UIPoolDiagnosticsSnapshot(
                entries.AsReadOnly(),
                _globalCapacity,
                _hitCount,
                _missCount,
                _evictionCount,
                _prewarmCount,
                _rejectedReturnCount,
                _invalidEntryCount);
        }

        private UIPoolReturnRejection ValidateReturn(
            Type contextType,
            UIPooledObject pooledObject,
            UIPoolPolicy policy,
            UIPoolScope scope)
        {
            if (contextType == null || pooledObject == null || policy == null || !scope.IsValid)
            {
                return UIPoolReturnRejection.InvalidArgument;
            }

            if (!pooledObject.IsValid)
            {
                return UIPoolReturnRejection.InvalidEntry;
            }

            if (pooledObject.ContextType != contextType ||
                pooledObject.Context.GetType() != contextType)
            {
                return UIPoolReturnRejection.ContextTypeMismatch;
            }

            if (pooledObject.OwnerPoolId != Guid.Empty && pooledObject.OwnerPoolId != _poolId)
            {
                return UIPoolReturnRejection.CrossPoolReturn;
            }

            if (pooledObject.OwnerPoolId == _poolId && pooledObject.IsIdle)
            {
                return UIPoolReturnRejection.DuplicateReturn;
            }

            if (!policy.CacheOnClose || policy.MaxPoolSize <= 0)
            {
                return UIPoolReturnRejection.Capacity;
            }

            return UIPoolReturnRejection.None;
        }

        private List<UIPooledObject> CollectEntries(Predicate<UIPooledObject> predicate)
        {
            var collected = new List<UIPooledObject>();
            var emptyTypes = new List<Type>();
            foreach (var pair in _buckets)
            {
                var bucket = pair.Value;
                for (var index = bucket.Count - 1; index >= 0; index--)
                {
                    var entry = bucket[index];
                    if (!predicate(entry))
                    {
                        continue;
                    }

                    RemoveAt(bucket, index, entry);
                    collected.Add(entry);
                }

                if (bucket.Count == 0)
                {
                    emptyTypes.Add(pair.Key);
                }
            }

            foreach (var type in emptyTypes)
            {
                _buckets.Remove(type);
            }

            return collected;
        }

        private UIPooledObject FindGlobalEvictionCandidate()
        {
            UIPooledObject candidate = null;
            foreach (var bucket in _buckets.Values)
            {
                foreach (var entry in bucket)
                {
                    if (entry != null &&
                        (candidate == null || CompareEvictionOrder(entry, candidate) < 0))
                    {
                        candidate = entry;
                    }
                }
            }

            return candidate;
        }

        private static int FindEvictionCandidate(List<UIPooledObject> bucket)
        {
            var candidate = -1;
            for (var index = 0; index < bucket.Count; index++)
            {
                if (candidate < 0 ||
                    CompareEvictionOrder(bucket[index], bucket[candidate]) < 0)
                {
                    candidate = index;
                }
            }

            return candidate;
        }

        private static int CompareEvictionOrder(UIPooledObject left, UIPooledObject right)
        {
            if (ReferenceEquals(left, right))
            {
                return 0;
            }

            if (left == null)
            {
                return -1;
            }

            if (right == null)
            {
                return 1;
            }

            var priority = left.Priority.CompareTo(right.Priority);
            if (priority != 0)
            {
                return priority;
            }

            var time = left.CachedTimestamp.CompareTo(right.CachedTimestamp);
            return time != 0 ? time : left.EntryId.CompareTo(right.EntryId);
        }

        private bool RemoveExisting(UIPooledObject entry)
        {
            if (entry == null || !_buckets.TryGetValue(entry.ContextType, out var bucket))
            {
                return false;
            }

            var index = bucket.IndexOf(entry);
            if (index < 0)
            {
                return false;
            }

            RemoveAt(bucket, index, entry);
            RemoveEmptyBucket(entry.ContextType, bucket);
            return true;
        }

        private void AddExisting(List<UIPooledObject> bucket, UIPooledObject entry)
        {
            entry.IsIdle = true;
            bucket.Add(entry);
            _count++;
        }

        private void RemoveAt(
            List<UIPooledObject> bucket,
            int index,
            UIPooledObject entry,
            bool preserveOwnership = false)
        {
            bucket.RemoveAt(index);
            _count--;
            if (_count < 0)
            {
                throw new InvalidOperationException("UI pool count became negative.");
            }

            if (entry != null)
            {
                entry.IsIdle = false;
                if (!preserveOwnership)
                {
                    entry.OwnerPoolId = Guid.Empty;
                }
            }
        }

        private void RemoveEmptyBucket(Type contextType, List<UIPooledObject> bucket)
        {
            if (bucket.Count == 0)
            {
                _buckets.Remove(contextType);
            }
        }

        private double ToSeconds(long ticks)
        {
            return (double)ticks / _clock.Frequency;
        }

        private static void InvokeAll(
            IReadOnlyList<UIPooledObject> entries,
            Action<UIPooledObject> action)
        {
            if (action == null)
            {
                return;
            }

            var errors = new List<Exception>();
            foreach (var entry in entries)
            {
                try
                {
                    action(entry);
                }
                catch (Exception exception)
                {
                    errors.Add(exception);
                }
            }

            if (errors.Count > 0)
            {
                throw new AggregateException("One or more pooled entries failed cleanup.", errors);
            }
        }
    }
}
