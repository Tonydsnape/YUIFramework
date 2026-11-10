using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.Scripting;

namespace YUIFramework.CodeLoading
{
    // Concrete AOT definitions, not an assertion that arbitrary Hotfix value-type
    // instantiations or future HybridCLR metadata have been generated.
    [Preserve]
    public static class HotfixAotWitness
    {
        [Preserve]
        public static async UniTask<int> ExerciseAsync(CancellationToken token = default)
        {
            var number = new ObservableProperty<int>(0);
            var text = new ObservableProperty<string>("");
            var items = new ObservableCollection<int>();
            var names = new ObservableCollection<string>();
            var notifications = 0;
            using var subscription = number.Subscribe(_ => notifications++, false);
            using var collection = items.Subscribe(_ => notifications++);
            using var command = new UIAsyncCommand(async cancellation =>
            {
                await UniTask.Yield(PlayerLoopTiming.Update, cancellation);
                number.Value = 3; text.Value = "AOT"; items.Add(number.Value); names.Add(text.Value);
            });
            await command.ExecuteAsync(token);
            return notifications + items[0] + names[0].Length;
        }
    }
}
