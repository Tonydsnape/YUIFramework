using System;
using System.Collections.Generic;

namespace YUIFramework
{
    /// <summary>
    /// <see cref="IUIResourcePackageRegistry"/> 的默认线程安全实现。
    /// 第一个注册的 package 会自动成为默认 package，显式指定的默认 package 优先。
    /// </summary>
    public sealed class UIResourcePackageRegistry : IUIResourcePackageRegistry
    {
        private readonly object _gate = new object();

        private readonly Dictionary<string, IUIResourceProvider> _providers =
            new Dictionary<string, IUIResourceProvider>(StringComparer.Ordinal);

        private string _defaultPackageName;
        private bool _defaultWasExplicit;
        private bool _frozen;

        public string DefaultPackageName
        {
            get
            {
                lock (_gate)
                {
                    return _defaultPackageName;
                }
            }
        }

        public IReadOnlyList<string> PackageNames
        {
            get
            {
                lock (_gate)
                {
                    return new List<string>(_providers.Keys);
                }
            }
        }

        public bool IsFrozen
        {
            get
            {
                lock (_gate)
                {
                    return _frozen;
                }
            }
        }

        public void Register(IUIResourceProvider provider, bool isDefault = false)
        {
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }

            var packageName = provider.PackageName;
            if (string.IsNullOrWhiteSpace(packageName))
            {
                throw new ArgumentException(
                    "IUIResourceProvider.PackageName must not be null or whitespace.",
                    nameof(provider));
            }

            lock (_gate)
            {
                ThrowIfFrozen();
                if (_providers.ContainsKey(packageName))
                {
                    throw new UIResourcePackageAlreadyRegisteredException(packageName);
                }

                _providers.Add(packageName, provider);

                if (isDefault)
                {
                    _defaultPackageName = packageName;
                    _defaultWasExplicit = true;
                }
                else if (_defaultPackageName == null && !_defaultWasExplicit)
                {
                    _defaultPackageName = packageName;
                }
            }
        }

        public bool Unregister(string packageName)
        {
            if (string.IsNullOrWhiteSpace(packageName))
            {
                return false;
            }

            lock (_gate)
            {
                ThrowIfFrozen();
                if (!_providers.Remove(packageName))
                {
                    return false;
                }

                if (!string.Equals(_defaultPackageName, packageName, StringComparison.Ordinal))
                {
                    return true;
                }

                _defaultWasExplicit = false;
                _defaultPackageName = null;
                foreach (var remaining in _providers.Keys)
                {
                    _defaultPackageName = remaining;
                    break;
                }

                return true;
            }
        }

        public IUIResourceProvider Resolve(string packageName)
        {
            if (TryResolve(packageName, out var provider))
            {
                return provider;
            }

            string known;
            lock (_gate)
            {
                known = string.Join(", ", new List<string>(_providers.Keys));
            }

            throw new UIResourcePackageNotFoundException(packageName, known);
        }

        public bool TryResolve(string packageName, out IUIResourceProvider provider)
        {
            lock (_gate)
            {
                var resolved = string.IsNullOrWhiteSpace(packageName) ? _defaultPackageName : packageName;
                if (resolved == null)
                {
                    provider = null;
                    return false;
                }

                return _providers.TryGetValue(resolved, out provider);
            }
        }

        public IReadOnlyList<IUIResourceProvider> GetProviders()
        {
            lock (_gate)
            {
                return new List<IUIResourceProvider>(_providers.Values);
            }
        }

        public void Freeze()
        {
            lock (_gate)
            {
                _frozen = true;
            }
        }

        private void ThrowIfFrozen()
        {
            if (_frozen)
            {
                throw new InvalidOperationException(
                    "The resource package registry is frozen after its service starts loading.");
            }
        }
    }
}
