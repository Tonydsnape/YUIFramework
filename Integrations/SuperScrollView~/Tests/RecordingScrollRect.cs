using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace YUIFramework.Integrations.Tests
{
    public sealed class RecordingScrollRect : ScrollRect
    {
        public int Begins;
        public int Drags;
        public int Ends;
        public override void OnBeginDrag(PointerEventData eventData) { Begins++; }
        public override void OnDrag(PointerEventData eventData) { Drags++; }
        public override void OnEndDrag(PointerEventData eventData) { Ends++; }
    }
}
