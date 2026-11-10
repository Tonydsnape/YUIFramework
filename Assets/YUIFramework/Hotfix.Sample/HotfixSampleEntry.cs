using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Scripting;
using UnityEngine.UI;
using YUIFramework.CodeLoading;

namespace YUIFramework.Hotfix.Sample
{
    [Preserve]
    public sealed class HotfixSampleEntry : IHotfixEntry
    {
        [Preserve] public HotfixSampleEntry() { }
        public async UniTask<BaseContext> EnterAsync(IUIService ui, UIConfig page, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ui.Register<HotfixSamplePage>(page);
            return await ui.OpenAsync<HotfixSamplePage>(cancellationToken: cancellationToken);
        }
    }

    [Preserve]
    public sealed class HotfixSamplePage : BasePageContext
    {
        private readonly ObservableProperty<int> _count = new ObservableProperty<int>(0);
        private readonly ObservableCollection<int> _history = new ObservableCollection<int>();
        private UIAsyncCommand _increment;
        private Image _panel;
        private Button _button;
        private UnityEngine.Events.UnityAction _click;
        public int Count => _count.Value;
        [Preserve] public HotfixSamplePage() { }
        protected override void HandleInit()
        {
            _panel = ViewObject.GetComponent<Image>();
            if (!_panel) _panel = ViewObject.AddComponent<Image>();
            _panel.color = new Color(.15f, .35f, .25f, .95f);
            var target = new GameObject("Hotfix increment", typeof(RectTransform), typeof(Image), typeof(Button));
            target.transform.SetParent(View.RectTransform, false);
            var rect = (RectTransform)target.transform;
            rect.anchorMin = new Vector2(.3f, .35f); rect.anchorMax = new Vector2(.7f, .65f);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            _button = target.GetComponent<Button>();
            _increment = new UIAsyncCommand(async token =>
            {
                await UniTask.Yield(PlayerLoopTiming.Update, token);
                _count.Value++; _history.Add(_count.Value);
            });
            _click = () => _increment.ExecuteAsync(DisplayToken).Forget(error =>
            {
                if (!(error is System.OperationCanceledException)) Debug.LogException(error);
            });
            _button.onClick.AddListener(_click);
        }
        protected override void HandleShow(object args)
        {
            TrackDisplayBinding(_count.Subscribe(value =>
            {
                ViewObject.name = "Hotfix count " + value;
                _panel.color = Color.Lerp(Color.green, Color.blue, (value % 10) / 10f);
            }));
        }
        public override GameObject DefaultFocus => _button ? _button.gameObject : null;
        protected override void HandleDestroy()
        {
            if (_button && _click != null) _button.onClick.RemoveListener(_click);
            _increment?.Dispose();
        }
    }
}
