using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace YUIFramework
{
    public sealed class UIHandle<T> where T : BaseContext
    {
        private readonly IUIService _service;

        internal UIHandle(IUIService service, UIKey key, T context)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            Key = key;
            Context = context ?? throw new ArgumentNullException(nameof(context));
        }

        public UIKey Key { get; }
        public T Context { get; }
        public bool IsOpen => ReferenceEquals(_service.Get<T>(), Context) && _service.IsOpen<T>();

        public UniTask CloseAsync(CancellationToken cancellationToken = default)
        {
            return _service.CloseAsync(Context, cancellationToken);
        }
    }
}
