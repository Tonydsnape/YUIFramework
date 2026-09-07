using System.Collections.Generic;

namespace YUIFramework
{
    /// <summary>单个资源条目的诊断快照。</summary>
    public readonly struct UIResourceEntrySnapshot
    {
        public UIResourceEntrySnapshot(
            UIResourceKey key,
            int leaseCount,
            int waiterCount,
            bool isLoaded,
            bool isLoading)
        {
            Key = key;
            LeaseCount = leaseCount;
            WaiterCount = waiterCount;
            IsLoaded = isLoaded;
            IsLoading = isLoading;
        }

        /// <summary>资源键。</summary>
        public UIResourceKey Key { get; }

        /// <summary>当前仍未释放的租约数量。</summary>
        public int LeaseCount { get; }

        /// <summary>当前正在等待该资源加载完成的调用方数量。</summary>
        public int WaiterCount { get; }

        /// <summary>底层句柄是否已加载完成。</summary>
        public bool IsLoaded { get; }

        /// <summary>底层加载是否仍在进行中。</summary>
        public bool IsLoading { get; }

        /// <summary>是否为“已加载但无人引用”的缓存条目。</summary>
        public bool IsUnreferenced => IsLoaded && LeaseCount == 0 && WaiterCount == 0;

        public override string ToString()
        {
            return $"{Key} leases={LeaseCount} waiters={WaiterCount} loaded={IsLoaded} loading={IsLoading}";
        }
    }

    /// <summary>资源服务的整体诊断快照，用于泄漏检测与运行时观察。</summary>
    public sealed class UIResourceDiagnosticsSnapshot
    {
        public UIResourceDiagnosticsSnapshot(
            IReadOnlyList<UIResourceEntrySnapshot> entries,
            long nativeLoadCount,
            long nativeReleaseCount,
            bool isShutDown)
        {
            Entries = entries ?? new List<UIResourceEntrySnapshot>();
            NativeLoadCount = nativeLoadCount;
            NativeReleaseCount = nativeReleaseCount;
            IsShutDown = isShutDown;

            var leased = new List<UIResourceEntrySnapshot>();
            var unreferenced = new List<UIResourceEntrySnapshot>();
            foreach (var entry in Entries)
            {
                TotalLeases += entry.LeaseCount;
                TotalWaiters += entry.WaiterCount;
                if (entry.IsLoading)
                {
                    LoadingEntryCount++;
                }

                if (entry.IsLoaded)
                {
                    LoadedEntryCount++;
                }

                if (entry.LeaseCount > 0)
                {
                    leased.Add(entry);
                }
                else if (entry.IsUnreferenced)
                {
                    unreferenced.Add(entry);
                }
            }

            LeasedEntries = leased;
            UnreferencedEntries = unreferenced;
        }

        /// <summary>全部资源条目。</summary>
        public IReadOnlyList<UIResourceEntrySnapshot> Entries { get; }

        /// <summary>仍持有租约的条目（泄漏排查的主要依据）。</summary>
        public IReadOnlyList<UIResourceEntrySnapshot> LeasedEntries { get; }

        /// <summary>已加载但无人引用的缓存条目（低内存时可清理）。</summary>
        public IReadOnlyList<UIResourceEntrySnapshot> UnreferencedEntries { get; }

        /// <summary>累计发起的底层加载次数。</summary>
        public long NativeLoadCount { get; }

        /// <summary>累计释放的底层句柄次数。</summary>
        public long NativeReleaseCount { get; }

        /// <summary>服务是否已关闭。</summary>
        public bool IsShutDown { get; }

        /// <summary>条目总数。</summary>
        public int EntryCount => Entries.Count;

        /// <summary>未释放租约总数。</summary>
        public int TotalLeases { get; }

        /// <summary>等待者总数。</summary>
        public int TotalWaiters { get; }

        /// <summary>已加载条目数。</summary>
        public int LoadedEntryCount { get; }

        /// <summary>加载中条目数。</summary>
        public int LoadingEntryCount { get; }

        /// <summary>当前是否存在疑似泄漏（仍有未释放租约）。</summary>
        public bool HasOutstandingLeases => TotalLeases > 0;

        /// <summary>底层句柄是否已全部归零（加载次数 == 释放次数）。</summary>
        public bool IsNativeBalanced => NativeLoadCount == NativeReleaseCount;

        public override string ToString()
        {
            return
                $"entries={EntryCount} leases={TotalLeases} waiters={TotalWaiters} " +
                $"loaded={LoadedEntryCount} loading={LoadingEntryCount} " +
                $"nativeLoads={NativeLoadCount} nativeReleases={NativeReleaseCount} shutdown={IsShutDown}";
        }
    }
}
