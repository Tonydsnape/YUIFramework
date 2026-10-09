'use strict';

const CS_TYPES = {
  int: 'int',
  long: 'long',
  float: 'float',
  double: 'double',
  string: 'string',
  'string?': 'string',
  bool: 'bool',
  json: 'JToken'
};

const CS_READERS = {
  int: 'ReadInt32',
  long: 'ReadInt64',
  float: 'ReadSingle',
  double: 'ReadDouble',
  string: 'ReadString',
  'string?': 'ReadOptionalString',
  bool: 'ReadBoolean',
  json: 'ReadJson'
};

const RESERVED = new Set(('abstract as base bool break byte case catch char checked class const continue decimal default delegate do double else enum event explicit extern false finally fixed float for foreach goto if implicit in int interface internal is lock long namespace new null object operator out override params private protected public readonly ref return sbyte sealed short sizeof stackalloc static string struct switch this throw true try typeof uint ulong unchecked unsafe ushort using virtual void volatile while').split(' '));

function identifier(value, context) {
  if (!/^[A-Za-z_][A-Za-z0-9_]*$/.test(value)) throw new Error(`${context} "${value}" 不是合法 C# 标识符`);
  return RESERVED.has(value) ? `@${value}` : value;
}

function q(value) {
  return JSON.stringify(String(value));
}

function clientFields(config) {
  return config.type === 'base'
    ? config.entries.filter((entry) => entry.target === 'c' || entry.target === 'sc')
    : config.fields.filter((field) => field.target === 'c' || field.target === 'sc');
}

function readExpression(type, objectName, jsonName, configName) {
  return `ConfigValue.${CS_READERS[type]}(${objectName}, ${q(jsonName)}, ${q(configName)})`;
}

function propertyDeclaration(field) {
  const name = identifier(field.name, '字段名');
  if (field.type === 'json')
    return `        private readonly JToken _${field.name};\n        public JToken ${name} => _${field.name}.DeepClone();`;
  return `        public ${CS_TYPES[field.type]} ${name} { get; }`;
}

function assignment(field) {
  const name = identifier(field.name, '字段名');
  return field.type === 'json'
    ? `            this._${field.name} = ${name}.DeepClone();`
    : `            this.${name} = ${name};`;
}

function generateBase(config) {
  const className = identifier(config.name, '配置名');
  const fields = clientFields(config);
  const ctorParams = fields.map((field) => `${CS_TYPES[field.type]} ${identifier(field.name, '字段名')}`).join(', ');
  const assignments = fields.map(assignment).join('\n');
  const properties = fields.map(propertyDeclaration).join('\n');
  const reads = fields.map((field) => `                ${readExpression(field.type, 'obj', field.name, config.name)}`).join(',\n');

  return `
    public sealed class ${className}
    {
${properties}

        internal ${className}(${ctorParams})
        {
${assignments}
        }
        public static ConfigTable<${className}> Table { get; } =
            new ConfigTable<${className}>(${q(config.name)}, ${config.required !== false}, Parse);

        private static ${className} Parse(JToken root)
        {
            JObject obj = ConfigValue.RequireObject(root, ${q(config.name)});
            return new ${className}(
${reads});
        }

    }
`;
}

function generateNormal(config) {
  const className = identifier(config.name, '配置名');
  const rowName = `${className}Row`;
  const fields = clientFields(config);
  const keyFields = config.fields.slice(0, config.keyCount);
  const ctorParams = fields.map((field) => `${CS_TYPES[field.type]} ${identifier(field.name, '字段名')}`).join(', ');
  const assignments = fields.map(assignment).join('\n');
  const properties = fields.map(propertyDeclaration).join('\n');
  const methodParams = keyFields.map((field, index) => `${CS_TYPES[field.type]} key${index}`).join(', ');
  const methodArgs = keyFields.map((field, index) => `key${index}`).join(', ');
  const rowReads = fields.map((field) => `                    ${readExpression(field.type, 'row', field.name, config.name)}`).join(',\n');
  const keyReads = keyFields.map((field, index) => `ConfigValue.${CS_READERS[field.type]}(keys[${index}], ${q(config.name + '.' + field.name)})`).join(', ');

  return `
    public sealed class ${rowName}
    {
${properties}

        internal ${rowName}(${ctorParams})
        {
${assignments}
        }
    }

    public sealed class ${className}
    {
        private readonly Dictionary<string, ${rowName}> _rows;
        private ${className}(Dictionary<string, ${rowName}> rows) => _rows = rows;
        public int Count => _rows.Count;
        public IReadOnlyCollection<${rowName}> Values => _rows.Values;
        public static ConfigTable<${className}> Table { get; } =
            new ConfigTable<${className}>(${q(config.name)}, ${config.required !== false}, Parse);

        public bool TryGet(${methodParams}, out ${rowName} row)
        {
            return _rows.TryGetValue(ConfigKey.Compose(${methodArgs}), out row);
        }

        public ${rowName} Get(${methodParams})
        {
            if (_rows.TryGetValue(ConfigKey.Compose(${methodArgs}), out ${rowName} row)) return row;
            throw new KeyNotFoundException($"配置 ${config.name} 不存在 Key: {ConfigKey.Display(${methodArgs})}");
        }

        private static ${className} Parse(JToken root)
        {
            var rows = new Dictionary<string, ${rowName}>(StringComparer.Ordinal);
            foreach (ConfigRowNode node in ConfigValue.EnumerateRows(root, ${config.keyCount}, ${q(config.name)}))
            {
                JToken[] keys = node.Keys;
                JObject row = node.Row;
                string key = ConfigKey.Compose(${keyReads});
                var value = new ${rowName}(
${rowReads});
                if (key != ConfigKey.Compose(${keyFields.map(field => `value.${identifier(field.name, 'Key')}`).join(', ')}))
                    throw new ConfigDataException("Row key fields do not match their nested path.");
                if (!rows.TryAdd(key, value)) throw new ConfigDataException($"配置 ${config.name} 包含重复 Key: {key}");
            }
${config.required !== false ? '            if (rows.Count == 0) throw new ConfigDataException("Required table has no rows.");' : ''}
            return new ${className}(rows);
        }
    }
`;
}

function generateCSharp(configurations, namespace = 'YUIFramework.ConfigGenerated') {
  namespace = namespace.split('.').map(part => identifier(part, 'Namespace')).join('.');
  const symbols = new Set(['GeneratedConfigCatalog', 'ConfigTable', 'ConfigValue', 'ConfigKey',
    'ConfigRowNode', 'ConfigDataException', 'JObject', 'JToken', 'Array', 'Dictionary',
    'StringComparer', 'IReadOnlyList', 'IReadOnlyCollection', 'KeyNotFoundException',
    'Table', 'Parse', 'Count', 'Values', 'Get', 'TryGet', '_rows']);
  const infrastructure = new Set(symbols);
  for (const config of configurations) {
    for (const name of [config.name, ...(config.type === 'normal' ? [config.name + 'Row'] : [])]) {
      identifier(name, 'Table');
      if (symbols.has(name)) throw new Error(`Generated type collision: ${name}`);
      symbols.add(name);
    }
    for (const field of clientFields(config)) {
      if (infrastructure.has(field.name) || [config.name, config.name + 'Row'].includes(field.name))
        throw new Error(`Field collides with generated member: ${field.name}`);
      if (clientFields(config).some(other => other.type === 'json' && field.name === '_' + other.name))
        throw new Error(`Field collides with JSON backing member: ${field.name}`);
    }
  }
  const bodies = configurations.map((config) => config.type === 'base'
    ? generateBase(config)
    : generateNormal(config)).join('\n');

  const descriptors = configurations.map((config) => {
    const className = identifier(config.name, '配置名');
    return `            ${className}.Table`;
  }).join(',\n');
  const names = configurations.map((config) => `                ${q(config.name)}`).join(',\n');

  return `// <auto-generated />
using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using YUIFramework.Configuration;

namespace ${namespace}
{
${bodies}
    public static class GeneratedConfigCatalog
    {
        public static IReadOnlyList<string> Names { get; } = Array.AsReadOnly(new[]
        {
${names}
        });

        public static IReadOnlyList<ConfigTable> Tables { get; } = Array.AsReadOnly(new ConfigTable[]
        {
${descriptors}
        });
    }
}
`;
}

module.exports = { generateCSharp };
