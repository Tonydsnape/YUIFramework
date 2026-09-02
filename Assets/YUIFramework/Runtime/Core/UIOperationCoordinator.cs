using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;

namespace YUIFramework
{
    /// <summary>
    /// 每个 key 一条 FIFO 队列的调度器。不同 key 的命令可以在 PlayerLoop 上并发推进，
    /// 同一个 key 的命令严格按入队顺序串行执行。
    /// 仅用于承载"排队/合并/关闭期收尾"这三件事，不感知 UI 生命周期语义——具体的
    /// 打开/关闭/隐藏/显示行为由调用方通过委托传入。
    /// </summary>
    /// <remarks>
    /// 设计要点：
    /// - 每个 key 惰性创建一条 lane，lane 内部只有一个 worker 循环，worker 退休（队列清空）
    ///   与新命令入队共用同一把锁，避免"worker 判空要退休"和"新命令认为 worker 还在跑"
    ///   之间的竞态。
    /// - Worker 内部把异常/取消都收敛进节点自身的 TaskCompletionSource，worker 循环体
    ///   本身永远不会向外抛异常，也不会产生未观察的 Task。
    /// - 使用 System.Threading.Tasks.Task 作为节点的共享结果载体，允许多个调用方安全地
    ///   多次 await 同一个已完成/进行中的结果（裸的 UniTask 不支持多次 await）。
    /// - 合并 Open 的每个调用方都独立取消自己的等待；只有全部等待者都取消时才取消共享
    ///   执行。服务生命周期 token 始终可以取消共享执行。
    /// - 生命周期回调与守卫的同步调用窗口使用显式 thread-local key 作用域检测重入，
    ///   从而显式失败而不是死锁，同时不污染其他 PlayerLoop continuation。
    /// - 不使用 Task.Run/ConfigureAwait(false)：所有继续都落回 Unity 主线程的同步上下文。
    /// </remarks>
    internal sealed class UIOperationCoordinator
    {
        private readonly object _gate = new object();
        private readonly Dictionary<object, Lane> _lanes = new Dictionary<object, Lane>();
        private bool _stopped;

        /// <summary>
        /// 停止接受新命令。已经入队/正在执行的命令继续正常排干，不会被强行取消——
        /// 它们通过各自携带的 CancellationToken（通常已链接了服务生命周期 token）
        /// 自然地观察到取消。
        /// </summary>
        public void Stop()
        {
            lock (_gate)
            {
                _stopped = true;
            }
        }

        public bool IsBusy(object key)
        {
            lock (_gate)
            {
                return _lanes.TryGetValue(key, out var lane) && (lane.Running != null || lane.Pending.Count > 0);
            }
        }

        /// <summary>
        /// 等待所有 lane 排空（运行中 + 排队中都清零）。不会阻塞主线程，逐帧轮询。
        /// </summary>
        public async UniTask DrainAsync()
        {
            while (true)
            {
                lock (_gate)
                {
                    if (_lanes.Count == 0)
                    {
                        return;
                    }
                }

                await UniTask.Yield(PlayerLoopTiming.Update);
            }
        }

        /// <summary>
        /// 排队一个返回值命令，不参与合并。
        /// </summary>
        public UniTask<T> EnqueueAsync<T>(
            object key,
            string operationName,
            Func<CancellationToken, UniTask<T>> work,
            CancellationToken callerToken)
        {
            ThrowIfReentrant(key, operationName);
            var node = new Node(key, callerToken);
            node.Invoke = ct => InvokeBoxedAsync(work, ct);
            EnqueueNode(node, operationName);
            return AwaitOwnAsync<T>(node);
        }

        /// <summary>
        /// 排队一个无返回值命令，不参与合并。
        /// </summary>
        public async UniTask EnqueueAsync(
            object key,
            string operationName,
            Func<CancellationToken, UniTask> work,
            CancellationToken callerToken)
        {
            await EnqueueAsync<object>(
                key,
                operationName,
                async ct =>
                {
                    await work(ct);
                    return null;
                },
                callerToken);
        }

        /// <summary>
        /// 排队一个 Open 命令。当且仅当同一 key 当前正在运行的命令满足以下全部条件时，
        /// 才会与其共享同一次执行而不新建节点：
        /// 1) 该命令仍在运行（尚未完成）；
        /// 2) 它在开始执行后已经通过 <c>markFirstCreation</c> 回调标记为“首次创建”
        ///    （即执行伊始既没有活动实例也没有可复用的池化实例）；
        /// 3) 请求参数与其 Equals 相等（均为 null 也视为相等）；
        /// 4) 该 lane 当前没有排在它后面、尚未执行的命令（不允许插队式合并）。
        /// 任何 Close 之后的 Open、任何已完成/已出错的节点、任何“刷新”或“池化复用”都
        /// 不会被合并，调用方各自独立排队执行。
        /// </summary>
        public UniTask<T> EnqueueOpenAsync<T>(
            object key,
            object args,
            bool mayCreateNew,
            Func<Action, CancellationToken, UniTask<T>> work,
            CancellationToken callerToken,
            CancellationToken serviceToken)
        {
            const string operationName = "Open";
            ThrowIfReentrant(key, operationName);

            Node attach = null;
            Node node = null;
            Lane lane = null;
            var startWorker = false;
            lock (_gate)
            {
                ThrowIfStoppedLocked(operationName);
                if (_lanes.TryGetValue(key, out var existingLane) &&
                    TryGetMergeCandidate(existingLane, args, out attach))
                {
                    attach.WaiterCount++;
                }
                else
                {
                    node = new Node(
                        key,
                        CancellationToken.None,
                        CancellationTokenSource.CreateLinkedTokenSource(serviceToken))
                    {
                        Args = args,
                        MergeEligible = mayCreateNew,
                        WaiterCount = 1
                    };
                    node.Invoke = ct => InvokeOpenBoxedAsync(work, node, ct);
                    lane = GetOrCreateLaneLocked(key);
                    lane.Pending.Enqueue(node);
                    if (!lane.WorkerRunning)
                    {
                        lane.WorkerRunning = true;
                        startWorker = true;
                    }
                }
            }

            if (attach != null)
            {
                return AwaitSharedAsync<T>(attach, callerToken);
            }

            if (startWorker)
            {
                RunLaneWorkerAsync(key, lane).Forget(HandleUnexpectedWorkerFault);
            }

            return AwaitSharedAsync<T>(node, callerToken);
        }

        private void ThrowIfStoppedLocked(string operationName)
        {
            if (_stopped)
            {
                throw new InvalidOperationException(
                    $"UIManager is shutting down and cannot accept new '{operationName}' operations.");
            }
        }

        private void ThrowIfReentrant(object key, string operationName)
        {
            if (UIOperationReentrancyScope.Contains(key))
            {
                throw new UIOperationReentrancyException(key, operationName);
            }
        }

        private void EnqueueNode(Node node, string operationName)
        {
            Lane lane;
            var startWorker = false;
            lock (_gate)
            {
                ThrowIfStoppedLocked(operationName);
                lane = GetOrCreateLaneLocked(node.Key);
                lane.Pending.Enqueue(node);
                if (!lane.WorkerRunning)
                {
                    lane.WorkerRunning = true;
                    startWorker = true;
                }
            }

            if (startWorker)
            {
                RunLaneWorkerAsync(node.Key, lane).Forget(HandleUnexpectedWorkerFault);
            }
        }

        private Lane GetOrCreateLaneLocked(object key)
        {
            if (!_lanes.TryGetValue(key, out var lane))
            {
                lane = new Lane();
                _lanes[key] = lane;
            }

            return lane;
        }

        private static void HandleUnexpectedWorkerFault(Exception exception)
        {
            // ExecuteNodeAsync captures every exception into the node's own completion,
            // so reaching here means a defect in the coordinator itself. Surface it
            // instead of letting it vanish as an unobserved task fault.
            UnityEngine.Debug.LogException(
                new InvalidOperationException(
                    "UIOperationCoordinator lane worker faulted unexpectedly.",
                    exception));
        }

        private async UniTask RunLaneWorkerAsync(object key, Lane lane)
        {
            // Defer execution so an immediately adjacent equivalent first-creation
            // Open can attach before the first node starts.
            await UniTask.Yield(PlayerLoopTiming.Update);

            while (true)
            {
                Node node;
                lock (_gate)
                {
                    if (lane.Pending.Count == 0)
                    {
                        lane.WorkerRunning = false;
                        lane.Running = null;
                        if (_lanes.TryGetValue(key, out var current) && ReferenceEquals(current, lane))
                        {
                            _lanes.Remove(key);
                        }

                        return;
                    }

                    node = lane.Pending.Dequeue();
                    lane.Running = node;
                }

                await ExecuteNodeAsync(node);

                lock (_gate)
                {
                    if (ReferenceEquals(lane.Running, node))
                    {
                        lane.Running = null;
                    }
                }
            }
        }

        private async UniTask ExecuteNodeAsync(Node node)
        {
            try
            {
                var executionToken = node.SharedCancellation?.Token ?? node.CallerToken;
                var result = await node.Invoke(executionToken);
                node.Tcs.TrySetResult(result);
            }
            catch (OperationCanceledException cancellation)
            {
                node.Tcs.TrySetCanceled(
                    cancellation.CancellationToken.CanBeCanceled
                        ? cancellation.CancellationToken
                        : node.CallerToken);
            }
            catch (Exception exception)
            {
                node.Tcs.TrySetException(exception);
            }
            finally
            {
                node.SharedCancellation?.Dispose();
            }
        }

        private static async UniTask<object> InvokeBoxedAsync<T>(Func<CancellationToken, UniTask<T>> work, CancellationToken token)
        {
            var result = await work(token);
            return result;
        }

        private static async UniTask<object> InvokeOpenBoxedAsync<T>(
            Func<Action, CancellationToken, UniTask<T>> work,
            Node node,
            CancellationToken token)
        {
            var result = await work(() => node.MergeEligible = true, token);
            return result;
        }

        private static async UniTask<T> AwaitOwnAsync<T>(Node node)
        {
            var result = await node.Tcs.Task;
            return (T)result;
        }

        private async UniTask<T> AwaitSharedAsync<T>(Node node, CancellationToken callerToken)
        {
            CancellationTokenSource cancellation = null;
            try
            {
                var result = await AwaitCancelableAsync(node.Tcs.Task, callerToken);
                return (T)result;
            }
            finally
            {
                lock (_gate)
                {
                    node.WaiterCount--;
                    if (node.WaiterCount == 0 &&
                        !node.Tcs.Task.IsCompleted &&
                        node.SharedCancellation != null &&
                        !node.SharedCancellation.IsCancellationRequested)
                    {
                        cancellation = node.SharedCancellation;
                    }
                }

                cancellation?.Cancel();
            }
        }

        private static async UniTask<object> AwaitCancelableAsync(Task<object> sharedTask, CancellationToken callerToken)
        {
            if (sharedTask.IsCompleted || !callerToken.CanBeCanceled)
            {
                return await sharedTask;
            }

            callerToken.ThrowIfCancellationRequested();
            var cancellationSignal = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (callerToken.Register(state => ((TaskCompletionSource<object>)state).TrySetCanceled(callerToken), cancellationSignal))
            {
                var completed = await Task.WhenAny(sharedTask, cancellationSignal.Task);
                if (ReferenceEquals(completed, cancellationSignal.Task))
                {
                    // Only this caller's wait is abandoned; the shared task keeps running
                    // for the original submitter and any other attached waiters.
                    await cancellationSignal.Task;
                }

                return await sharedTask;
            }
        }

        private static bool ArgsEqual(object left, object right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }

            if (left == null || right == null)
            {
                return false;
            }

            return left.Equals(right);
        }

        private static bool TryGetMergeCandidate(Lane lane, object args, out Node candidate)
        {
            candidate = lane.Running;
            if (candidate == null && lane.Pending.Count == 1)
            {
                candidate = lane.Pending.Peek();
            }

            if (candidate == null ||
                !candidate.MergeEligible ||
                candidate.SharedCancellation == null ||
                candidate.SharedCancellation.IsCancellationRequested ||
                lane.Pending.Count > (ReferenceEquals(candidate, lane.Running) ? 0 : 1) ||
                candidate.Tcs.Task.IsCompleted ||
                !ArgsEqual(candidate.Args, args))
            {
                candidate = null;
                return false;
            }

            return true;
        }

        private sealed class Lane
        {
            public readonly Queue<Node> Pending = new Queue<Node>();
            public Node Running;
            public bool WorkerRunning;
        }

        private sealed class Node
        {
            public Node(
                object key,
                CancellationToken callerToken,
                CancellationTokenSource sharedCancellation = null)
            {
                Key = key;
                CallerToken = callerToken;
                SharedCancellation = sharedCancellation;
                Tcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            public object Key { get; }
            public CancellationToken CallerToken { get; }
            public object Args { get; set; }
            public bool MergeEligible { get; set; }
            public int WaiterCount { get; set; }
            public CancellationTokenSource SharedCancellation { get; }
            public TaskCompletionSource<object> Tcs { get; }
            public Func<CancellationToken, UniTask<object>> Invoke { get; set; }
        }

    }
}
