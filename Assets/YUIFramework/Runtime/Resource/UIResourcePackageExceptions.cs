using System;

namespace YUIFramework
{
    /// <summary>请求了未注册 package 时抛出。</summary>
    public sealed class UIResourcePackageNotFoundException : InvalidOperationException
    {
        public UIResourcePackageNotFoundException(string packageName, string knownPackages)
            : base(BuildMessage(packageName, knownPackages))
        {
            PackageName = packageName;
        }

        public string PackageName { get; }

        private static string BuildMessage(string packageName, string knownPackages)
        {
            var requested = string.IsNullOrEmpty(packageName) ? "<default>" : packageName;
            return string.IsNullOrEmpty(knownPackages)
                ? $"UI resource package \"{requested}\" is not registered, and no package has been registered at all."
                : $"UI resource package \"{requested}\" is not registered. Registered packages: {knownPackages}.";
        }
    }

    /// <summary>重复注册同名 package 时抛出。</summary>
    public sealed class UIResourcePackageAlreadyRegisteredException : InvalidOperationException
    {
        public UIResourcePackageAlreadyRegisteredException(string packageName)
            : base($"UI resource package \"{packageName}\" is already registered. Unregister it before registering a new provider.")
        {
            PackageName = packageName;
        }

        public string PackageName { get; }
    }
}
