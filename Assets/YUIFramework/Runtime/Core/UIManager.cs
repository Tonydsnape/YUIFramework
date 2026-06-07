using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace YUIFramework
{
    /// <summary>
    /// UI 核心调度器，负责注册、打开、关闭与生命周期驱动。
    /// </summary>
    public class UIManager
    {
        private static readonly Lazy<UIManager> LazyInstance = new Lazy<UIManager>(() => new UIManager());

        private readonly Dictionary<Type, UIConfig> _configRegistry = new Dictionary<Type, UIConfig>();
        private readonly Dictionary<Type, BaseContext> _activeContexts = new Dictionary<Type, BaseContext>();
        private readonly Dictionary<BaseContext, string> _contextPrefabKeys = new Dictionary<BaseContext, string>();

        private IResourceLoader _resourceLoader;
        private IUIObjectPool _objectPool = new UIObjectPool();
        private UILayerManager _layerManager;
        private bool _initialized;

        public static UIManager Instance => LazyInstance.Value;
        public UINavigator Navigator { get; private set; }

        public void Init(IResourceLoader loader, IUIObjectPool pool = null)
        {
            _resourceLoader = loader ?? throw new ArgumentNullException(nameof(loader));
            if (pool != null)
            {
                if (_objectPool != null && !ReferenceEquals(_objectPool, pool))
                {
                    _objectPool.Clear(DestroyPooledObject);
                }

                _objectPool = pool;
            }
            else
            {
                _objectPool ??= new UIObjectPool();
            }

            _layerManager = new UILayerManager(UIRoot.Instance);
            Navigator ??= new UINavigator(this);
            _initialized = true;
        }

        public void Register<T>(UIConfig config) where T : BaseContext
        {
            EnsureInitialized();

            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            if (string.IsNullOrWhiteSpace(config.Id))
            {
                throw new ArgumentException("UIConfig.Id 不能为空。", nameof(config));
            }

            if (string.IsNullOrWhiteSpace(config.PrefabKey))
            {
                throw new ArgumentException("UIConfig.PrefabKey 不能为空。", nameof(config));
            }

            _configRegistry[typeof(T)] = config;
        }

        public async Task<T> OpenAsync<T>(object args = null) where T : BaseContext
        {
            EnsureInitialized();

            var contextType = typeof(T);
            if (!_configRegistry.TryGetValue(contextType, out var config))
            {
                throw new KeyNotFoundException($"未注册 UI Context: {contextType.Name}");
            }

            if (_activeContexts.TryGetValue(contextType, out var cachedContext))
            {
                if (cachedContext.View == null || cachedContext.ViewObject == null)
                {
                    throw new InvalidOperationException($"Context {contextType.Name} 运行时绑定丢失。请重新打开并检查生命周期。");
                }

                _layerManager.AddToLayer(cachedContext.Layer, cachedContext.View.RectTransform);
                cachedContext.ViewObject.SetActive(true);
                cachedContext.OnShow(args);
                return (T)cachedContext;
            }

            if (_objectPool.TryGet(contextType, out var pooled))
            {
                var pooledContext = pooled.Context;
                var pooledViewObject = pooled.ViewObject;
                if (pooledContext != null && pooledViewObject != null)
                {
                    var pooledView = pooledContext.View ?? pooledViewObject.GetComponent<UIView>() ?? pooledViewObject.AddComponent<UIView>();
                    pooledContext.BindRuntime(config.Id, config.Layer, pooledView, pooledViewObject);
                    pooledView.Context = pooledContext;
                    _layerManager.AddToLayer(config.Layer, pooledView.RectTransform);
                    pooledViewObject.SetActive(true);
                    pooledContext.OnShow(args);

                    _activeContexts[contextType] = pooledContext;
                    _contextPrefabKeys[pooledContext] = pooled.PrefabKey;
                    return (T)pooledContext;
                }
            }

            var newContext = Activator.CreateInstance<T>();
            newContext.State = UIContextState.Loading;

            GameObject prefab;
            var loaderType = _resourceLoader?.GetType().Name ?? "UnknownLoader";
            try
            {
                prefab = await _resourceLoader.LoadPrefabAsync(config.PrefabKey);
            }
            catch (ResourceLoadException ex)
            {
                throw new InvalidOperationException(
                    BuildPrefabLoadErrorMessage(contextType, config, ex.LoaderType, ex.DetailMessage), ex);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    BuildPrefabLoadErrorMessage(contextType, config, loaderType, ex.Message), ex);
            }

            if (prefab == null)
            {
                throw new InvalidOperationException(
                    BuildPrefabLoadErrorMessage(contextType, config, loaderType, "Loader 返回了 null Prefab。"));
            }

            var instance = UnityEngine.Object.Instantiate(prefab);
            var view = instance.GetComponent<UIView>() ?? instance.AddComponent<UIView>();

            var layer = config.Layer;
            newContext.BindRuntime(config.Id, layer, view, instance);
            view.Context = newContext;

            _layerManager.AddToLayer(layer, view.RectTransform);
            newContext.OnInit();
            instance.SetActive(true);
            newContext.OnShow(args);

            _activeContexts[contextType] = newContext;
            _contextPrefabKeys[newContext] = config.PrefabKey;

            // TODO(P2): 接入栈式导航（Push/Pop/Replace）并结合 FullScreen 决策遮挡策略。
            // TODO(P5): 接入消息中心，支持 UI 与系统模块解耦通信。
            return newContext;
        }

        public Task CloseAsync<T>() where T : BaseContext
        {
            return _activeContexts.TryGetValue(typeof(T), out var context)
                ? CloseAsync(context)
                : Task.CompletedTask;
        }

        public Task CloseAsync(BaseContext ctx)
        {
            EnsureInitialized();

            if (ctx == null)
            {
                return Task.CompletedTask;
            }

            var contextType = ctx.GetType();
            if (!_activeContexts.ContainsKey(contextType))
            {
                return Task.CompletedTask;
            }

            ctx.OnHide();
            ctx.OnClose();
            _activeContexts.Remove(contextType);

            _configRegistry.TryGetValue(contextType, out var config);
            var prefabKey = ResolvePrefabKey(ctx, config);

            if (config != null && _objectPool != null)
            {
                var policy = UIPoolPolicy.FromConfig(config);
                var pooledObject = new UIPooledObject(contextType, prefabKey, ctx, ctx.ViewObject);
                if (_objectPool.TryRelease(contextType, pooledObject, policy, out var overflow))
                {
                    return Task.CompletedTask;
                }

                if (overflow != null)
                {
                    DestroyContextInternal(overflow.Context, overflow.PrefabKey);
                    return Task.CompletedTask;
                }
            }

            DestroyContextInternal(ctx, prefabKey);

            return Task.CompletedTask;
        }

        public T Get<T>() where T : BaseContext
        {
            return _activeContexts.TryGetValue(typeof(T), out var context) ? (T)context : null;
        }

        public bool IsOpen<T>() where T : BaseContext
        {
            if (!_activeContexts.TryGetValue(typeof(T), out var context) || context.ViewObject == null)
            {
                return false;
            }

            return context.ViewObject.activeInHierarchy;
        }

        public void ClearPool<T>() where T : BaseContext
        {
            EnsureInitialized();
            _objectPool?.Clear(typeof(T), DestroyPooledObject);
        }

        public void ClearAllPools()
        {
            EnsureInitialized();
            _objectPool?.Clear(DestroyPooledObject);
        }

        internal void HideWithoutClose(BaseContext ctx)
        {
            EnsureInitialized();

            if (ctx == null)
            {
                return;
            }

            ctx.OnHide();
            if (ctx.ViewObject != null)
            {
                ctx.ViewObject.SetActive(false);
            }
        }

        internal void ShowWithoutOpen(BaseContext ctx, object args = null)
        {
            EnsureInitialized();

            if (ctx == null)
            {
                return;
            }

            if (ctx.ViewObject != null)
            {
                ctx.ViewObject.SetActive(true);
            }

            if (ctx.View != null && ctx.View.RectTransform != null)
            {
                _layerManager.AddToLayer(ctx.Layer, ctx.View.RectTransform);
            }
            ctx.OnShow(args);
        }

        private void EnsureInitialized()
        {
            if (!_initialized)
            {
                throw new InvalidOperationException("UIManager 尚未初始化。请先调用 Init(IResourceLoader)。");
            }
        }

        private string ResolvePrefabKey(BaseContext context, UIConfig config)
        {
            if (context != null && _contextPrefabKeys.TryGetValue(context, out var storedKey))
            {
                return storedKey;
            }

            return config?.PrefabKey ?? string.Empty;
        }

        private void DestroyPooledObject(UIPooledObject pooledObject)
        {
            if (pooledObject == null)
            {
                return;
            }

            DestroyContextInternal(pooledObject.Context, pooledObject.PrefabKey);
        }

        private void DestroyContextInternal(BaseContext context, string prefabKey)
        {
            if (context == null)
            {
                return;
            }

            context.OnDestroy();
            if (context.ViewObject != null)
            {
                _resourceLoader.Release(prefabKey, context.ViewObject);
            }

            _contextPrefabKeys.Remove(context);
        }

        private static string BuildPrefabLoadErrorMessage(Type contextType, UIConfig config, string loaderType, string detail)
        {
            var message =
                $"加载 UI Prefab 失败: type={contextType.Name}, id={config.Id}, key={config.PrefabKey}, loader={loaderType}。{detail}";

            if (string.Equals(loaderType, nameof(ResourcesLoader), StringComparison.Ordinal))
            {
                var normalizedKey = ResourcePathUtility.NormalizeResourcesKey(config.PrefabKey);
                message +=
                    $" 如果使用 ResourcesLoader，请确认文件位于 Assets/Resources/{normalizedKey}.prefab，且 PrefabKey 不包含 Assets/Resources/ 和 .prefab。";
            }

            return message;
        }
    }
}
