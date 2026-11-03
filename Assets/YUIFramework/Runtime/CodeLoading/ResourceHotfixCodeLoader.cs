using System;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using YUIFramework.Bootstrap;

namespace YUIFramework.CodeLoading
{
    public sealed class ResourceHotfixCodeLoader : IBootstrapCodeLoader
    {
        private readonly HotfixAssemblySpec _spec;
        private readonly Func<BootstrapReadyContext, IUIResourceService> _resources;
        private readonly IHotfixAssemblyRuntime _runtime;
        private bool _loading;
        private Exception _irreversibleFailure;
        public Assembly LoadedAssembly { get; private set; }
        public IHotfixEntry Entry { get; private set; }
        public Exception LastFailure { get; private set; }

        public ResourceHotfixCodeLoader(HotfixAssemblySpec spec,
            Func<BootstrapReadyContext, IUIResourceService> resources, IHotfixAssemblyRuntime runtime)
        {
            _spec = spec ?? throw new ArgumentNullException(nameof(spec));
            _resources = resources ?? throw new ArgumentNullException(nameof(resources));
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        }
        public async UniTask LoadAsync(BootstrapReadyContext context, CancellationToken cancellationToken)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            cancellationToken.ThrowIfCancellationRequested();
            if (_loading) throw new InvalidOperationException("The same code-loader instance cannot be run concurrently.");
            if (_irreversibleFailure != null)
                throw new InvalidOperationException("Assembly loading/entry construction previously failed after an irreversible attempt; restart this loader/application.", _irreversibleFailure);
            var found = false;
            foreach (var package in context.Packages)
            {
                if (!package.IsManifestVerified) throw new InvalidOperationException("Code loading requires verified ready packages.");
                if (package.PackageName == _spec.Package) found = true;
            }
            if (!found) throw new InvalidOperationException("Code package is not part of this verified ready context: " + _spec.Package);
            if (Entry != null) return;
            _loading = true; LastFailure = null;
            try
            {
                if (LoadedAssembly == null)
                {
                    var service = _resources(context) ?? throw new InvalidOperationException("Ready resource composition is missing.");
                    byte[] bytes;
                    using (var lease = await service.LoadAssetAsync<TextAsset>(_spec.Address, _spec.Package, cancellationToken))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        bytes = lease.Asset.bytes;
                        using var sha = SHA256.Create();
                        var actual = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
                        if (actual != _spec.Sha256) throw new InvalidOperationException("Hotfix DLL content hash does not match the trusted manifest.");
                    }
                    cancellationToken.ThrowIfCancellationRequested();
                    // Release the borrowed resource lease BEFORE executing arbitrary code.
                    // Assembly.Load cannot be canceled/unloaded; never pretend to roll it back.
                    try
                    {
                        LoadedAssembly = _runtime.Load(bytes, _spec.AssemblyFullName) ??
                            throw new InvalidOperationException("Assembly runtime returned null.");
                        if (LoadedAssembly.FullName != _spec.AssemblyFullName)
                            throw new InvalidOperationException("Loaded assembly identity differs from the trusted manifest: " + LoadedAssembly.FullName);
                    }
                    catch (Exception error) { _irreversibleFailure = error; throw; }
                }
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var type = LoadedAssembly.GetType(_spec.EntryType, true, false);
                    if (!type.IsClass || type.IsAbstract || !typeof(IHotfixEntry).IsAssignableFrom(type) ||
                        type.GetConstructor(Type.EmptyTypes) == null)
                        throw new InvalidOperationException("Hotfix entry must be a concrete class implementing IHotfixEntry with a public parameterless constructor.");
                    Entry = (IHotfixEntry)Activator.CreateInstance(type);
                    // Constructors are synchronous and cannot be rolled back. Retain
                    // a completed entry even if cancellation arrived inside its ctor.
                    cancellationToken.ThrowIfCancellationRequested();
                }
                catch (Exception error) when (!(error is OperationCanceledException))
                { _irreversibleFailure = error; throw; }
            }
            catch (Exception error) { LastFailure = error; throw; }
            finally { _loading = false; }
        }
    }
}
