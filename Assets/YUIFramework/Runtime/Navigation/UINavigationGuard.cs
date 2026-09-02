using System.Threading;
using Cysharp.Threading.Tasks;

namespace YUIFramework
{
    /// <summary>
    /// 异步导航守卫扩展点。返回 <c>true</c> 放行，<c>false</c> 拒绝。
    /// 守卫抛出的异常与取消会原样传播给发起该导航命令的调用方；无论放行还是拒绝，
    /// 守卫求值都发生在命令真正执行（出队）时的栈快照之上，被拒绝或抛异常时命令
    /// 不会对导航栈或任何 Context 产生任何副作用。
    /// 一个执行缓慢的守卫会天然地把同一 <see cref="UINavigator"/> 上后续排队的命令
    /// 阻塞在其后面——导航命令始终严格 FIFO。
    /// </summary>
    public delegate UniTask<bool> UINavigationGuard(UINavigationRequest request, CancellationToken cancellationToken);
}
