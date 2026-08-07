using UnityEditor;
using UnityEngine;
using YUIFramework.HotUpdate;

namespace YUIFramework.Editor
{
    /// <summary>
    /// 热更调试工具（概念级）。用于在编辑器里切换运行模式、设置本地 CDN 地址，
    /// 并快捷打开 YooAsset 自带的收集器 / 构建窗口。
    ///
    /// 说明：本工具只做"运行模式 + CDN 切换"，资源收集与构建仍走 YooAsset 官方窗口，
    /// 不重造轮子。设置持久化在 EditorPrefs，编辑器加载时自动写回 <see cref="HotUpdateConfig"/>。
    /// 注意：进入 Play 时若场景里的 GameLauncher 勾选了自身的运行模式，会以其序列化值为准。
    /// </summary>
    public sealed class HotUpdateEditorWindow : EditorWindow
    {
        private const string PrefPlayMode = "YUIFramework.HotUpdate.PlayMode";
        private const string PrefUseYoo = "YUIFramework.HotUpdate.UseYooAsset";
        private const string PrefHost = "YUIFramework.HotUpdate.HostURL";

        [MenuItem("Tools/YUIFramework/HotUpdate 设置")]
        private static void Open()
        {
            var window = GetWindow<HotUpdateEditorWindow>("HotUpdate");
            window.minSize = new Vector2(360f, 260f);
            window.Show();
        }

        [InitializeOnLoadMethod]
        private static void ApplyPersistedOnLoad()
        {
            HotUpdateConfig.PlayMode = (HotUpdatePlayMode)EditorPrefs.GetInt(PrefPlayMode, (int)HotUpdatePlayMode.EditorSimulate);
            HotUpdateConfig.UseYooAsset = EditorPrefs.GetBool(PrefUseYoo, true);
            var host = EditorPrefs.GetString(PrefHost, HotUpdateConfig.HostServerURL);
            if (!string.IsNullOrWhiteSpace(host))
            {
                HotUpdateConfig.ConfigureHost(host);
            }
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("运行模式与 CDN", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "EditorSimulate：资源来自工程，不走 StreamingAssets/CDN，仅开发用。\n" +
                "Offline：仅读包内内置资源。\n" +
                "Host：包内命中则用包内，否则从 CDN 下载。",
                MessageType.Info);

            EditorGUI.BeginChangeCheck();
            var playMode = (HotUpdatePlayMode)EditorGUILayout.EnumPopup("运行模式", HotUpdateConfig.PlayMode);
            var useYoo = EditorGUILayout.Toggle("启用 YooAsset", HotUpdateConfig.UseYooAsset);
            var host = EditorGUILayout.TextField("CDN 主地址", HotUpdateConfig.HostServerURL);
            if (EditorGUI.EndChangeCheck())
            {
                HotUpdateConfig.PlayMode = playMode;
                HotUpdateConfig.UseYooAsset = useYoo;
                if (!string.IsNullOrWhiteSpace(host))
                {
                    HotUpdateConfig.ConfigureHost(host);
                }

                EditorPrefs.SetInt(PrefPlayMode, (int)playMode);
                EditorPrefs.SetBool(PrefUseYoo, useYoo);
                EditorPrefs.SetString(PrefHost, host);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("YooAsset 官方窗口", EditorStyles.boldLabel);
            if (GUILayout.Button("打开 AssetBundle Collector"))
            {
                TryExecute("YooAsset/AssetBundle Collector");
            }

            if (GUILayout.Button("打开 AssetBundle Builder"))
            {
                TryExecute("YooAsset/AssetBundle Builder");
            }

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "本地 CDN 联调：用 YooAsset Builder 构建后，把输出目录用任意静态服务器（如 " +
                "`python -m http.server 8080`）托管，CDN 主地址填 http://127.0.0.1:8080 即可。",
                MessageType.None);
        }

        private static void TryExecute(string menuPath)
        {
            if (!EditorApplication.ExecuteMenuItem(menuPath))
            {
                Debug.LogWarning($"[HotUpdate] 未找到菜单：{menuPath}，请确认 YooAsset 已正确安装。");
            }
        }
    }
}
