using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace YUIFramework.HotUpdate
{
    /// <summary>
    /// 结构化启动诊断：从启动入口到首个可玩界面，记录带序号与耗时的阶段日志。
    /// 便于在真机/弱网下定位"卡在哪一步"。移植自参考项目并保持原样风格。
    /// </summary>
    public static class StartupFlowTrace
    {
        private const float HeartbeatSeconds = 5f;
        private static int _sequence;
        private static float _startedAt;
        private static bool _started;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            _sequence = 0;
            _startedAt = 0f;
            _started = false;
        }

        public static void Begin(string detail)
        {
            if (!_started)
            {
                _started = true;
                _startedAt = Time.realtimeSinceStartup;
                _sequence = 0;
            }

            Write("BEGIN", "startup", detail, false);
        }

        public static void Step(string stage, string detail = null) => Write("STEP", stage, detail, false);

        public static void Warning(string stage, string detail = null) => Write("WARN", stage, detail, true);

        public static void Error(string stage, string detail = null) => Debug.LogError(Format("ERROR", stage, detail));

        public static void Complete(string detail = null) => Write("COMPLETE", "home-playable", detail, false);

        /// <summary>等待某条件成立，期间按心跳打印进度；超时返回 false。</summary>
        public static async UniTask<bool> WaitUntilAsync(
            Func<bool> condition,
            string stage,
            float timeoutSeconds = 0f,
            Func<string> state = null)
        {
            float waitStartedAt = Time.realtimeSinceStartup;
            float nextHeartbeatAt = waitStartedAt + HeartbeatSeconds;
            Step(stage + ".wait-begin", ReadState(state));

            while (!condition())
            {
                float now = Time.realtimeSinceStartup;
                float elapsed = now - waitStartedAt;
                if (timeoutSeconds > 0f && elapsed >= timeoutSeconds)
                {
                    Warning(
                        stage + ".wait-timeout",
                        $"waited={elapsed:0.0}s timeout={timeoutSeconds:0.0}s {ReadState(state)}");
                    return false;
                }

                if (now >= nextHeartbeatAt)
                {
                    Warning(stage + ".waiting", $"waited={elapsed:0.0}s {ReadState(state)}");
                    nextHeartbeatAt = now + HeartbeatSeconds;
                }

                await UniTask.Delay(100, ignoreTimeScale: true);
            }

            Step(stage + ".wait-end", $"waited={Time.realtimeSinceStartup - waitStartedAt:0.0}s {ReadState(state)}");
            return true;
        }

        private static string ReadState(Func<string> state)
        {
            if (state == null)
            {
                return string.Empty;
            }

            try
            {
                return state.Invoke() ?? string.Empty;
            }
            catch (Exception e)
            {
                return "state-error=" + e.GetType().Name;
            }
        }

        private static void Write(string kind, string stage, string detail, bool warning)
        {
            string message = Format(kind, stage, detail);
            if (warning)
            {
                Debug.LogWarning(message);
            }
            else
            {
                Debug.Log(message);
            }
        }

        private static string Format(string kind, string stage, string detail)
        {
            if (!_started)
            {
                _started = true;
                _startedAt = Time.realtimeSinceStartup;
            }

            int sequence = ++_sequence;
            float elapsed = Time.realtimeSinceStartup - _startedAt;
            string suffix = string.IsNullOrWhiteSpace(detail) ? string.Empty : " | " + detail.Trim();
            return $"[StartupFlow][{kind}] #{sequence:000} +{elapsed:0.000}s stage={stage}{suffix}";
        }
    }
}
