using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace YUIFramework.Configuration
{
    public enum ConfigFormat { Json, MessagePack }

    public interface IConfigCodec
    {
        JToken Decode(byte[] bytes, ConfigFormat format);
    }

    public interface IConfigAsset : IDisposable
    {
        byte[] Bytes { get; }
    }

    public interface IConfigSource
    {
        UniTask<IConfigAsset> LoadAsync(string table, CancellationToken cancellationToken);
    }

    public abstract class ConfigTable
    {
        protected ConfigTable(string name, bool required)
        {
            if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(new[] { '/', '\\', ':', '.' }) >= 0)
                throw new ArgumentException("A simple table name is required.", nameof(name));
            Name = name;
            Required = required;
        }
        public string Name { get; }
        public bool Required { get; }
        internal abstract object Parse(JToken root);
    }

    public sealed class ConfigTable<T> : ConfigTable where T : class
    {
        private readonly Func<JToken, T> _parse;
        public ConfigTable(string name, bool required, Func<JToken, T> parse) : base(name, required)
        { _parse = parse ?? throw new ArgumentNullException(nameof(parse)); }
        internal override object Parse(JToken root) =>
            _parse(root) ?? throw new ConfigDataException($"Parser returned null for {Name}.");
    }

    public sealed class ConfigSnapshot
    {
        private readonly Dictionary<ConfigTable, object> _tables;
        internal ConfigSnapshot(long generation, Dictionary<ConfigTable, object> tables, List<ConfigTableFailure> failures)
        { Generation = generation; _tables = tables; OptionalFailures = failures.AsReadOnly(); }
        public long Generation { get; }
        public IReadOnlyList<ConfigTableFailure> OptionalFailures { get; }
        public T Get<T>(ConfigTable<T> table) where T : class =>
            TryGet(table, out var value) ? value : throw new ConfigNotLoadedException(table.Name);
        public bool TryGet<T>(ConfigTable<T> table, out T value) where T : class
        {
            if (table == null) throw new ArgumentNullException(nameof(table));
            if (_tables.TryGetValue(table, out var found)) { value = (T)found; return true; }
            value = null;
            return false;
        }
    }

    public sealed class ConfigTableFailure
    {
        internal ConfigTableFailure(string name, Exception error) { Name = name; Error = error; }
        public string Name { get; }
        public Exception Error { get; }
    }
    public sealed class ConfigDataException : Exception
    {
        public ConfigDataException(string message) : base(message) { }
        public ConfigDataException(string message, Exception error) : base(message, error) { }
    }
    public sealed class ConfigNotLoadedException : InvalidOperationException
    {
        public ConfigNotLoadedException(string name) : base($"Configuration {name} has not been loaded.") { }
    }
    public readonly struct ConfigRowNode
    {
        public ConfigRowNode(JToken[] keys, JObject row) { Keys = keys; Row = row; }
        public JToken[] Keys { get; }
        public JObject Row { get; }
    }
}
