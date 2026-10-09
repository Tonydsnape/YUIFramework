namespace YUIFramework.Integrations.Tests
{
    public sealed class LegacyListItemProbe : UIVirtualListItem
    {
        public int Unbinds;
        protected override void OnUnbindIndex() { Unbinds++; }
    }
}
