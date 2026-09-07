using System;
using System.Collections.Generic;

namespace YUIFramework
{
    /// <summary>
    /// 默认 UI 对象池实现。
    /// </summary>
    public sealed class UIObjectPool : IUIObjectPool
    {
        private readonly Dictionary<Type, Stack<UIPooledObject>> _buckets = new Dictionary<Type, Stack<UIPooledObject>>();

        public bool TryGet(Type contextType, out UIPooledObject pooledObject)
        {
            pooledObject = null;
            if (contextType == null || !_buckets.TryGetValue(contextType, out var bucket))
            {
                return false;
            }

            while (bucket.Count > 0)
            {
                var candidate = bucket.Pop();
                if (candidate != null && candidate.IsValid)
                {
                    pooledObject = candidate;
                    break;
                }
            }

            if (bucket.Count == 0)
            {
                _buckets.Remove(contextType);
            }

            return pooledObject != null;
        }

        public bool TryRelease(Type contextType, UIPooledObject pooledObject, UIPoolPolicy policy, out UIPooledObject overflowObject)
        {
            overflowObject = null;
            if (contextType == null || pooledObject == null || !pooledObject.IsValid || policy == null)
            {
                return false;
            }

            if (!policy.CacheOnClose || policy.MaxPoolSize <= 0)
            {
                overflowObject = pooledObject;
                return false;
            }

            if (!_buckets.TryGetValue(contextType, out var bucket))
            {
                bucket = new Stack<UIPooledObject>(policy.MaxPoolSize);
                _buckets[contextType] = bucket;
            }

            if (bucket.Count >= policy.MaxPoolSize)
            {
                overflowObject = pooledObject;
                return false;
            }

            bucket.Push(pooledObject);
            return true;
        }

        public void Clear(Action<UIPooledObject> destroyAction = null)
        {
            foreach (var pair in _buckets)
            {
                ClearBucket(pair.Value, destroyAction);
            }

            _buckets.Clear();
        }

        public void Clear(Type contextType, Action<UIPooledObject> destroyAction = null)
        {
            if (contextType == null || !_buckets.TryGetValue(contextType, out var bucket))
            {
                return;
            }

            ClearBucket(bucket, destroyAction);
            _buckets.Remove(contextType);
        }

        public int RemoveInvalid(Action<UIPooledObject> removeAction = null)
        {
            var removed = 0;
            var emptyBuckets = new List<Type>();
            var removedObjects = new List<UIPooledObject>();
            foreach (var pair in _buckets)
            {
                var retained = new Stack<UIPooledObject>(pair.Value.Count);
                while (pair.Value.Count > 0)
                {
                    var pooled = pair.Value.Pop();
                    if (pooled != null && pooled.IsValid)
                    {
                        retained.Push(pooled);
                    }
                    else
                    {
                        removed++;
                        if (pooled != null)
                        {
                            removedObjects.Add(pooled);
                        }
                    }
                }

                while (retained.Count > 0)
                {
                    pair.Value.Push(retained.Pop());
                }

                if (pair.Value.Count == 0)
                {
                    emptyBuckets.Add(pair.Key);
                }
            }

            foreach (var contextType in emptyBuckets)
            {
                _buckets.Remove(contextType);
            }

            foreach (var pooled in removedObjects)
            {
                removeAction?.Invoke(pooled);
            }

            return removed;
        }

        public int Count(Type contextType)
        {
            return contextType != null && _buckets.TryGetValue(contextType, out var bucket) ? bucket.Count : 0;
        }

        private static void ClearBucket(Stack<UIPooledObject> bucket, Action<UIPooledObject> destroyAction)
        {
            if (bucket == null)
            {
                return;
            }

            while (bucket.Count > 0)
            {
                var pooled = bucket.Pop();
                if (pooled != null)
                {
                    destroyAction?.Invoke(pooled);
                }
            }
        }
    }
}
