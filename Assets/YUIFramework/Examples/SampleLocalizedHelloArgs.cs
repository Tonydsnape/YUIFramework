using System;
using YUIFramework.Localization;

namespace YUIFramework
{
    public sealed class SampleLocalizedHelloArgs
    {
        public SampleLocalizedHelloArgs(TextLocalizationService localization, string playerName, UIPresentationService presentation = null)
        { Localization = localization ?? throw new ArgumentNullException(nameof(localization)); PlayerName = playerName; Presentation = presentation; }
        public TextLocalizationService Localization { get; }
        public string PlayerName { get; }
        public UIPresentationService Presentation { get; }
    }
}
