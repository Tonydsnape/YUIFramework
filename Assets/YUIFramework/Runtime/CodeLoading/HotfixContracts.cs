using System;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace YUIFramework.CodeLoading
{
    public interface IHotfixEntry
    {
        UniTask<BaseContext> EnterAsync(IUIService ui, UIConfig page, CancellationToken cancellationToken);
    }

    public interface IHotfixAssemblyRuntime
    {
        Assembly Load(byte[] verifiedBytes, string expectedAssemblyName);
    }

    public sealed class PreloadedHotfixAssemblyRuntime : IHotfixAssemblyRuntime
    {
        private readonly Assembly _assembly;
        public PreloadedHotfixAssemblyRuntime(Assembly assembly) =>
            _assembly = assembly ?? throw new ArgumentNullException(nameof(assembly));
        public Assembly Load(byte[] verifiedBytes, string expectedAssemblyName) => _assembly;
    }

    // This proves the extension on Mono, not dynamic-code support on stock IL2CPP.
    public sealed class MonoHotfixAssemblyRuntime : IHotfixAssemblyRuntime
    {
        public Assembly Load(byte[] verifiedBytes, string expectedAssemblyName)
        {
#if ENABLE_IL2CPP
            throw new PlatformNotSupportedException("Stock IL2CPP cannot load managed executable DLLs. Inject a separately installed HybridCLR adapter.");
#else
            return Assembly.Load(verifiedBytes);
#endif
        }
    }

    public sealed class HotfixAssemblySpec
    {
        public HotfixAssemblySpec(string package, string address, string assemblyFullName, string entryType, string sha256)
        {
            if (string.IsNullOrWhiteSpace(package) || string.IsNullOrWhiteSpace(address) || string.IsNullOrWhiteSpace(entryType))
                throw new ArgumentException("Explicit package, resource address and entry type are required.");
            var identity = new AssemblyName(assemblyFullName);
            if (identity.Version == null || identity.FullName != assemblyFullName)
                throw new ArgumentException("Use an exact Assembly.FullName, including version/culture/public key token.");
            if (sha256 == null || sha256.Length != 64 || !System.Text.RegularExpressions.Regex.IsMatch(sha256, "^[0-9a-fA-F]{64}$"))
                throw new ArgumentException("An expected SHA256 from the trusted application manifest is required.");
            Package = package; Address = address; AssemblyFullName = assemblyFullName;
            EntryType = entryType; Sha256 = sha256.ToLowerInvariant();
        }
        public string Package { get; }
        public string Address { get; }
        public string AssemblyFullName { get; }
        public string EntryType { get; }
        public string Sha256 { get; }
    }
}
