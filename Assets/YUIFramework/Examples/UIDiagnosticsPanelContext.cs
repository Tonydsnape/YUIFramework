using System;
using System.Linq;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace YUIFramework
{
    public sealed class UIDiagnosticsPanelModel
    {
        public UIDiagnosticsPanelModel(UIManager ui, UIDiagnostics diagnostics, UIProfilerSampler sampler)
        {
            UI = ui ?? throw new ArgumentNullException(nameof(ui));
            Diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
            Sampler = sampler ?? throw new ArgumentNullException(nameof(sampler));
        }
        public UIManager UI { get; }
        public UIDiagnostics Diagnostics { get; }
        public UIProfilerSampler Sampler { get; }
        public UIPerformanceTarget Target { get; set; } = UIPerformanceTarget.Editor;
        public string Scenario { get; set; } = "Runtime UI inspection (panel included)";
        public string Filter { get; set; } = "";
        public long SelectedInstance { get; set; }
        public UIDiagnosticSnapshot Snapshot { get; private set; }
        public UIPerformanceReport Report { get; private set; }
        public void Refresh()
        {
            Diagnostics.FlushAdapters();
            Sampler.RefreshAvailableCounters();
            var budget = UIPerformanceBudget.For(Target);
            Snapshot = Diagnostics.Capture(UI, budget, includeHierarchy: true);
            Report = Sampler.Report(budget, Scenario, Snapshot);
        }
    }

    public sealed class UIDiagnosticsPanelContext : BaseDialogContext, IUIBackHandler
    {
        private UIDiagnosticsPanelModel _model;
        private Text _body;
        private Button _refresh;
        private bool _closing;
        public override UILayer DefaultLayer => UILayer.Debug;
        public override GameObject DefaultFocus => _refresh?.gameObject;
        public UIDiagnosticsPanelModel Model => _model;
        public string DisplayedText => _body == null ? "" : _body.text;

        protected override void HandleInit()
        {
            var content = SystemUISampleLayout.Panel(this, TrackBinding, .03f, .97f);
            SystemUISampleLayout.Text(content, "Stage 13 - on-demand UI diagnostics", 26);
            _refresh = SystemUISampleLayout.Button(content, "Refresh (no automatic tree scans)", Refresh);
            SystemUISampleLayout.Button(content, "Next page details", NextPage);
            SystemUISampleLayout.Button(content, "Change budget target", () =>
            {
                if (_model == null) return;
                _model.Target = (UIPerformanceTarget)(((int)_model.Target + 1) % 3);
                Refresh();
            });
            SystemUISampleLayout.Button(content, "Close diagnostics", () => TryHandleBack());
            var field = new GameObject("PageFilter", typeof(RectTransform), typeof(Image),
                typeof(InputField), typeof(LayoutElement));
            field.transform.SetParent(content, false);
            field.GetComponent<LayoutElement>().preferredHeight = 48;
            field.GetComponent<Image>().color = new Color(.15f, .2f, .3f);
            var input = field.GetComponent<InputField>();
            var text = SystemUISampleLayout.Text(field.transform, "", 20);
            var rect = (RectTransform)text.transform;
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(12, 4); rect.offsetMax = new Vector2(-12, -4);
            input.textComponent = text;
            input.onEndEdit.AddListener(value => { if (_model != null) { _model.Filter = value; Refresh(); } });
            _body = SystemUISampleLayout.Text(content, "", 18);
            _body.alignment = TextAnchor.UpperLeft;
            _body.supportRichText = false;
        }

        protected override void HandleShow(object args)
        {
            if (!UIProfilerSampler.DevelopmentAllowed)
                throw new InvalidOperationException("Diagnostics panel requires Editor or Development Build.");
            _model = args as UIDiagnosticsPanelModel ??
                throw new ArgumentException("A diagnostic panel model is required.", nameof(args));
            _closing = false;
            TrackDisplayBinding(new BindingToken(() => _model = null));
            Refresh();
        }

        public void Refresh()
        {
            if (_model == null) return;
            _model.Refresh();
            var snapshot = _model.Snapshot;
            var text = new StringBuilder();
            text.AppendLine($"Filter='{_model.Filter}'  focus={snapshot.Focus}");
            text.AppendLine($"Coverage complete={snapshot.RecordingComplete}; overwritten events={snapshot.DroppedEvents}; " +
                $"dropped operations={_model.Diagnostics.DroppedOperations}; adapter faults={_model.Diagnostics.AdapterFaultCount}");
            text.AppendLine("Navigation (bottom -> top): " + string.Join(", ", snapshot.Navigation));
            text.AppendLine("Popup/modal (bottom -> top): " + string.Join(", ", snapshot.Modals));
            text.AppendLine($"Pool idle={snapshot.Pool.IdleCount} hit={snapshot.Pool.HitCount} miss={snapshot.Pool.MissCount}");
            text.AppendLine("Pages / instances:");
            foreach (var context in snapshot.Contexts.Where(Matches)) text.AppendLine(context.ToString());
            text.AppendLine("Selected page details:");
            var selected = snapshot.Contexts.FirstOrDefault(c => c.InstanceId == _model.SelectedInstance) ??
                snapshot.Contexts.FirstOrDefault(Matches);
            if (selected != null)
            {
                _model.SelectedInstance = selected.InstanceId;
                text.AppendLine(selected.ToString());
                text.AppendLine($"lifetimeCanceled={selected.LifetimeCanceled}; instanceLease={selected.HasInstanceLease}");
            }
            text.AppendLine("Scopes:");
            foreach (var scope in snapshot.Scopes)
                text.AppendLine($"{scope.Identity} ended={scope.IsEnded} tasks={scope.OperationCount}");
            text.AppendLine("Resources (unreferenced caches and live idle-pool leases are not leaks):");
            if (snapshot.Resources == null) text.AppendLine("Unavailable: legacy loader has no lease diagnostics.");
            else foreach (var resource in snapshot.Resources.Entries) text.AppendLine(resource.ToString());
            text.AppendLine("Input locks:");
            foreach (var item in snapshot.InputLocks)
                text.AppendLine($"{item.Id} owner={item.OwnerType}#{item.InstanceId} {item.Reason}");
            text.AppendLine("Active animations:");
            foreach (var item in snapshot.Transitions)
                text.AppendLine($"{item.Target} op={item.OperationId} generation={item.Generation} reversing={item.IsReversing}");
            text.AppendLine("Subscriptions:");
            foreach (var item in snapshot.Subscriptions)
                text.AppendLine($"{item.Topic} {item.OwnerType}#{item.InstanceId} {item.Handler} duplicates={item.DuplicateCount}");
            text.AppendLine("Loading / running operations:");
            foreach (var item in snapshot.Operations)
                text.AppendLine($"op={item.OperationId} parent={item.ParentId} {item.Kind}/{item.Phase} {item.Page} " +
                    $"scope={item.Scope} address={item.Resource} {item.ElapsedMs:F2}ms canceled={item.CancellationRequested}");
            text.AppendLine("Findings:");
            foreach (var issue in snapshot.Issues) text.AppendLine(issue.ToString());
            text.AppendLine("Recent events/errors (last 16 matching events; full records available through GetEvents):");
            foreach (var entry in snapshot.Events.Where(e => string.IsNullOrEmpty(_model.Filter) ||
                         e.Page.IndexOf(_model.Filter, StringComparison.OrdinalIgnoreCase) >= 0).Reverse().Take(16).Reverse())
            {
                var line = entry.ToString();
                text.AppendLine(line.Length > 400 ? line.Substring(0, 400) + " [details truncated]" : line);
            }
            text.AppendLine(_model.Report.ToText());
            _body.text = text.Length <= 14000 ? text.ToString() :
                text.ToString(0, 13900) + "\n[Panel text limit reached; narrow the filter or inspect the snapshot API.]";
        }

        private bool Matches(UIContextDiagnosticSnapshot context) =>
            string.IsNullOrEmpty(_model.Filter) ||
            context.Page.IndexOf(_model.Filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
            context.Key.IndexOf(_model.Filter, StringComparison.OrdinalIgnoreCase) >= 0;

        public void NextPage()
        {
            if (_model == null) return;
            var pages = _model.Snapshot.Contexts.Where(Matches).ToArray();
            var index = Array.FindIndex(pages, p => p.InstanceId == _model.SelectedInstance);
            if (pages.Length != 0) _model.SelectedInstance = pages[(index + 1) % pages.Length].InstanceId;
            Refresh();
        }

        public bool TryHandleBack()
        {
            if (_model == null || _closing) return false;
            _closing = true;
            Services.CloseAsync(this).Forget(Debug.LogException);
            return true;
        }
    }
}
