using System;
using UnityEditor;
using UnityEngine;
using YUIFramework.Bootstrap;

namespace YUIFramework.Editor
{
    public sealed class HotUpdateEditorWindow : EditorWindow
    {
        private const string Prefix = "YUIFramework.Bootstrap.";

        private BootstrapMode _mode;
        private string _packageName;
        private string _applicationId;
        private string _channel;
        private string _applicationVersion;
        private string _primaryCdn;
        private string _fallbackCdn;
        private float _timeoutSeconds;
        private int _maximumAttempts;
        private int _downloadConcurrency;
        private string _validationMessage;

        [MenuItem("Tools/YUIFramework/Bootstrap Profile")]
        private static void Open()
        {
            var window = GetWindow<HotUpdateEditorWindow>("Bootstrap");
            window.minSize = new Vector2(420f, 420f);
            window.Show();
        }

        private void OnEnable()
        {
            _mode = (BootstrapMode)EditorPrefs.GetInt(
                Prefix + "Mode",
                (int)BootstrapMode.EditorSimulate);
            _packageName = EditorPrefs.GetString(Prefix + "Package", "DefaultPackage");
            _applicationId = EditorPrefs.GetString(
                Prefix + "ApplicationId",
                string.IsNullOrWhiteSpace(Application.identifier)
                    ? "YUIFramework.Application"
                    : Application.identifier);
            _channel = EditorPrefs.GetString(Prefix + "Channel", "default");
            _applicationVersion = EditorPrefs.GetString(
                Prefix + "ApplicationVersion",
                string.IsNullOrWhiteSpace(Application.version) ? "0" : Application.version);
            _primaryCdn = EditorPrefs.GetString(
                Prefix + "PrimaryCdn",
                "http://127.0.0.1:8080");
            _fallbackCdn = EditorPrefs.GetString(Prefix + "FallbackCdn", string.Empty);
            _timeoutSeconds = EditorPrefs.GetFloat(Prefix + "Timeout", 30f);
            _maximumAttempts = EditorPrefs.GetInt(Prefix + "Attempts", 3);
            _downloadConcurrency = EditorPrefs.GetInt(Prefix + "Concurrency", 8);
            Validate();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Immutable BootstrapProfile defaults", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "These values are editor preferences for authoring and validation only. " +
                "Runtime startup code must construct and inject its own immutable BootstrapProfile.",
                MessageType.Info);

            EditorGUI.BeginChangeCheck();
            _mode = (BootstrapMode)EditorGUILayout.EnumPopup("Mode", _mode);
            _packageName = EditorGUILayout.TextField("Package", _packageName);
            _applicationId = EditorGUILayout.TextField("Application ID", _applicationId);
            _channel = EditorGUILayout.TextField("Channel", _channel);
            _applicationVersion = EditorGUILayout.TextField("Application Version", _applicationVersion);
            _primaryCdn = EditorGUILayout.TextField("Primary CDN", _primaryCdn);
            _fallbackCdn = EditorGUILayout.TextField("Fallback CDN", _fallbackCdn);
            _timeoutSeconds = EditorGUILayout.FloatField("Timeout (seconds)", _timeoutSeconds);
            _maximumAttempts = EditorGUILayout.IntField("Maximum Attempts", _maximumAttempts);
            _downloadConcurrency = EditorGUILayout.IntField("Download Concurrency", _downloadConcurrency);
            if (EditorGUI.EndChangeCheck())
            {
                Save();
                Validate();
            }

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                string.IsNullOrEmpty(_validationMessage)
                    ? "Profile values are valid."
                    : _validationMessage,
                string.IsNullOrEmpty(_validationMessage)
                    ? MessageType.Info
                    : MessageType.Error);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("YooAsset", EditorStyles.boldLabel);
            if (GUILayout.Button("Open AssetBundle Collector"))
            {
                ExecuteYooAssetMenu("YooAsset/AssetBundle Collector");
            }

            if (GUILayout.Button("Open AssetBundle Builder"))
            {
                ExecuteYooAssetMenu("YooAsset/AssetBundle Builder");
            }
        }

        private void Save()
        {
            EditorPrefs.SetInt(Prefix + "Mode", (int)_mode);
            EditorPrefs.SetString(Prefix + "Package", _packageName);
            EditorPrefs.SetString(Prefix + "ApplicationId", _applicationId);
            EditorPrefs.SetString(Prefix + "Channel", _channel);
            EditorPrefs.SetString(Prefix + "ApplicationVersion", _applicationVersion);
            EditorPrefs.SetString(Prefix + "PrimaryCdn", _primaryCdn);
            EditorPrefs.SetString(Prefix + "FallbackCdn", _fallbackCdn);
            EditorPrefs.SetFloat(Prefix + "Timeout", _timeoutSeconds);
            EditorPrefs.SetInt(Prefix + "Attempts", _maximumAttempts);
            EditorPrefs.SetInt(Prefix + "Concurrency", _downloadConcurrency);
        }

        private void Validate()
        {
            try
            {
                var primary = string.IsNullOrWhiteSpace(_primaryCdn)
                    ? null
                    : new Uri(_primaryCdn.Trim().TrimEnd('/') + "/", UriKind.Absolute);
                var fallback = string.IsNullOrWhiteSpace(_fallbackCdn)
                    ? null
                    : new Uri(_fallbackCdn.Trim().TrimEnd('/') + "/", UriKind.Absolute);
                _ = new BootstrapProfile(
                    _mode,
                    new[] { new BootstrapPackageProfile(_packageName) },
                    _applicationId,
                    _channel,
                    _applicationVersion,
                    primary,
                    fallback,
                    TimeSpan.FromSeconds(_timeoutSeconds),
                    _maximumAttempts,
                    downloadConcurrency: _downloadConcurrency);
                _validationMessage = string.Empty;
            }
            catch (Exception exception)
            {
                _validationMessage = exception.Message;
            }
        }

        private static void ExecuteYooAssetMenu(string menuPath)
        {
            if (!EditorApplication.ExecuteMenuItem(menuPath))
            {
                Debug.LogWarning($"[Bootstrap] YooAsset menu is unavailable: {menuPath}");
            }
        }
    }
}
