using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace YUIFramework.HotUpdate
{
    [Obsolete("Inject IBootstrapTelemetrySink into BootstrapRunner.")]
    public static class StartupFlowTrace
    {
        public static void Begin(string detail) => Write("BEGIN", "bootstrap", detail);

        public static void Step(string stage, string detail = null) => Write("STEP", stage, detail);

        public static void Warning(string stage, string detail = null) => Write("WARN", stage, detail);

        public static void Error(string stage, string detail = null)
        {
            Debug.LogError(Format("ERROR", stage, detail));
        }

        public static void Complete(string detail = null) => Write("COMPLETE", "game-entered", detail);

        public static async UniTask<bool> WaitUntilAsync(
            Func<bool> condition,
            string stage,
            float timeoutSeconds = 0f,
            Func<string> state = null)
        {
            if (condition == null)
            {
                throw new ArgumentNullException(nameof(condition));
            }

            var startedAt = Time.realtimeSinceStartup;
            while (!condition())
            {
                if (timeoutSeconds > 0f &&
                    Time.realtimeSinceStartup - startedAt >= timeoutSeconds)
                {
                    return false;
                }

                await UniTask.Yield(PlayerLoopTiming.Update);
            }

            return true;
        }

        private static void Write(string kind, string stage, string detail)
        {
            Debug.Log(Format(kind, stage, detail));
        }

        private static string Format(string kind, string stage, string detail)
        {
            var suffix = string.IsNullOrWhiteSpace(detail) ? string.Empty : " | " + detail.Trim();
            return $"[Bootstrap][{kind}] state={stage}{suffix}";
        }
    }
}
