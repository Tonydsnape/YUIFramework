namespace YUIFramework
{
    /// <summary>
    /// MVVM 示例 ViewModel。
    /// </summary>
    public sealed class MvvmSampleViewModel : ViewModelBase
    {
        public ObservableProperty<string> Title { get; } = new ObservableProperty<string>("MVVM Sample");
        public ObservableProperty<int> ClickCount { get; } = new ObservableProperty<int>(0);
        public ObservableProperty<bool> Enabled { get; } = new ObservableProperty<bool>(true);
        public ObservableProperty<float> Progress { get; } = new ObservableProperty<float>(0.5f);
        public UICommand IncrementCommand { get; }

        public MvvmSampleViewModel()
        {
            IncrementCommand = new UICommand(Increment, () => Enabled.Value);
            TrackDisposable(IncrementCommand);
            TrackDisposable(
                Enabled.Subscribe(
                    _ => IncrementCommand.NotifyCanExecuteChanged(),
                    false));
        }

        public void Increment()
        {
            ClickCount.Value++;
            Title.Value = $"Clicked {ClickCount.Value} times";
        }
    }
}
