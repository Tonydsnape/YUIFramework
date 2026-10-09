using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace YUIFramework.Configuration
{
    public static class ConfigUIStartup
    {
        public static async UniTask EnterAsync(ConfigService configs, UIManager ui,
            Func<ConfigSnapshot, IReadOnlyList<UIConfigRegistration>> map,
            Func<CancellationToken, UniTask> businessEntry, CancellationToken cancellationToken = default)
        {
            if (configs == null || ui == null || map == null || businessEntry == null)
                throw new ArgumentNullException("Config startup dependencies must be provided.");
            var snapshot = await configs.InitializeAsync(cancellationToken);
            if (snapshot.OptionalFailures.Count != 0)
                throw new AggregateException("UI startup requires all requested configuration tables.",
                    GetErrors(snapshot));
            var registrations = map(snapshot);
            cancellationToken.ThrowIfCancellationRequested();
            ui.RegisterBatch(registrations);
            cancellationToken.ThrowIfCancellationRequested();
            await businessEntry(cancellationToken);
        }

        private static IEnumerable<Exception> GetErrors(ConfigSnapshot snapshot)
        {
            foreach (var failure in snapshot.OptionalFailures) yield return failure.Error;
        }
    }
}
