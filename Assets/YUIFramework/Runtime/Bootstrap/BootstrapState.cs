using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace YUIFramework.Bootstrap
{
    public enum BootstrapState
    {
        Idle,
        InitializingPackage,
        RequestingVersion,
        ActivatingManifest,
        CalculatingDownload,
        AwaitingConfirmation,
        CheckingDisk,
        Downloading,
        Verifying,
        ResourcesReady,
        LoadingCodeExtension,
        EnteringGame,
        Completed,
        Canceled,
        Failed,
        Resetting,
        ShuttingDown,
    }

    public static class BootstrapStateGraph
    {
        private static readonly IReadOnlyDictionary<BootstrapState, IReadOnlyCollection<BootstrapState>>
            AllowedTargets = CreateGraph();

        public static bool CanTransition(BootstrapState from, BootstrapState to)
        {
            ValidateState(from, nameof(from));
            ValidateState(to, nameof(to));
            return from == to || AllowedTargets[from].Contains(to);
        }

        public static IReadOnlyCollection<BootstrapState> GetAllowedTargets(BootstrapState from)
        {
            ValidateState(from, nameof(from));
            return AllowedTargets[from];
        }

        public static void EnsureTransition(BootstrapState from, BootstrapState to)
        {
            if (!CanTransition(from, to))
            {
                throw new InvalidOperationException($"Bootstrap state cannot transition from {from} to {to}.");
            }
        }

        private static IReadOnlyDictionary<BootstrapState, IReadOnlyCollection<BootstrapState>> CreateGraph()
        {
            var graph = new Dictionary<BootstrapState, IReadOnlyCollection<BootstrapState>>();
            Add(graph, BootstrapState.Idle, BootstrapState.InitializingPackage, BootstrapState.Resetting, BootstrapState.ShuttingDown);
            AddActive(graph, BootstrapState.InitializingPackage, BootstrapState.RequestingVersion);
            AddActive(graph, BootstrapState.RequestingVersion, BootstrapState.ActivatingManifest);
            AddActive(graph, BootstrapState.ActivatingManifest, BootstrapState.CalculatingDownload);
            AddActive(
                graph,
                BootstrapState.CalculatingDownload,
                BootstrapState.AwaitingConfirmation,
                BootstrapState.CheckingDisk,
                BootstrapState.ResourcesReady);
            AddActive(graph, BootstrapState.AwaitingConfirmation, BootstrapState.CheckingDisk);
            AddActive(graph, BootstrapState.CheckingDisk, BootstrapState.Downloading);
            AddActive(
                graph,
                BootstrapState.Downloading,
                BootstrapState.Verifying,
                BootstrapState.ActivatingManifest);
            AddActive(
                graph,
                BootstrapState.Verifying,
                BootstrapState.ResourcesReady,
                BootstrapState.ActivatingManifest);
            AddActive(graph, BootstrapState.ResourcesReady, BootstrapState.LoadingCodeExtension);
            AddActive(graph, BootstrapState.LoadingCodeExtension, BootstrapState.EnteringGame);
            AddActive(graph, BootstrapState.EnteringGame, BootstrapState.Completed);
            AddTerminal(graph, BootstrapState.Completed);
            AddTerminal(graph, BootstrapState.Canceled);
            AddTerminal(graph, BootstrapState.Failed);
            Add(
                graph,
                BootstrapState.Resetting,
                BootstrapState.Idle,
                BootstrapState.Failed,
                BootstrapState.ShuttingDown);
            Add(graph, BootstrapState.ShuttingDown);
            return new ReadOnlyDictionary<BootstrapState, IReadOnlyCollection<BootstrapState>>(graph);
        }

        private static void AddActive(
            IDictionary<BootstrapState, IReadOnlyCollection<BootstrapState>> graph,
            BootstrapState state,
            params BootstrapState[] next)
        {
            var targets = new List<BootstrapState>(next)
            {
                BootstrapState.Canceled,
                BootstrapState.Failed,
                BootstrapState.ShuttingDown,
            };
            Add(graph, state, targets.ToArray());
        }

        private static void AddTerminal(
            IDictionary<BootstrapState, IReadOnlyCollection<BootstrapState>> graph,
            BootstrapState state)
        {
            Add(graph, state, BootstrapState.Resetting, BootstrapState.ShuttingDown);
        }

        private static void Add(
            IDictionary<BootstrapState, IReadOnlyCollection<BootstrapState>> graph,
            BootstrapState state,
            params BootstrapState[] next)
        {
            graph.Add(state, new ReadOnlyCollection<BootstrapState>(next));
        }

        private static void ValidateState(BootstrapState state, string parameterName)
        {
            if (!Enum.IsDefined(typeof(BootstrapState), state))
            {
                throw new ArgumentOutOfRangeException(parameterName, state, "Unknown bootstrap state.");
            }
        }
    }

    internal static class BootstrapStateCollectionExtensions
    {
        public static bool Contains(this IReadOnlyCollection<BootstrapState> states, BootstrapState value)
        {
            foreach (var state in states)
            {
                if (state == value)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
