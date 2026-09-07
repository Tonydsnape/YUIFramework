using System;
using System.Collections.Generic;

namespace YUIFramework
{
    /// <summary>
    /// 批量加载结果：成功项的所有权明确地交给调用方，失败项聚合保留。
    /// 部分失败不会丢弃已成功获得的租约；调用方可以选择使用它们，或整体 <see cref="Dispose"/>。
    /// </summary>
    public sealed class UIResourceBatchResult : IDisposable
    {
        private readonly List<IUIAssetLease> _leases;
        private readonly List<UIResourceBatchFailure> _failures;
        private bool _disposed;

        public UIResourceBatchResult(
            List<IUIAssetLease> leases,
            List<UIResourceBatchFailure> failures)
        {
            _leases = leases ?? new List<IUIAssetLease>();
            _failures = failures ?? new List<UIResourceBatchFailure>();
        }

        /// <summary>成功加载并已归调用方所有的租约。</summary>
        public IReadOnlyList<IUIAssetLease> Leases => _leases;

        /// <summary>失败项及其原因。</summary>
        public IReadOnlyList<UIResourceBatchFailure> Failures => _failures;

        /// <summary>是否存在失败项。</summary>
        public bool HasFailures => _failures.Count > 0;

        /// <summary>是否全部成功。</summary>
        public bool IsCompleteSuccess => _failures.Count == 0;

        /// <summary>把失败项聚合成一个异常；全部成功时返回 null。</summary>
        public AggregateException ToAggregateException()
        {
            if (_failures.Count == 0)
            {
                return null;
            }

            var inner = new List<Exception>(_failures.Count);
            foreach (var failure in _failures)
            {
                inner.Add(failure.Error);
            }

            return new AggregateException(
                $"Failed to load {_failures.Count} of {_failures.Count + _leases.Count} UI resources.",
                inner);
        }

        /// <summary>释放本结果中全部成功项的租约（幂等）。</summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            foreach (var lease in _leases)
            {
                lease?.Release();
            }

            _leases.Clear();
        }
    }

    /// <summary>批量加载中的单个失败项。</summary>
    public readonly struct UIResourceBatchFailure
    {
        public UIResourceBatchFailure(UIResourceKey key, Exception error)
        {
            Key = key;
            Error = error;
        }

        /// <summary>失败的资源键。</summary>
        public UIResourceKey Key { get; }

        /// <summary>失败原因。</summary>
        public Exception Error { get; }

        public override string ToString() => $"{Key}: {Error?.Message}";
    }
}
