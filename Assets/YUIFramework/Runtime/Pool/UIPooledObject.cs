using System;
using UnityEngine;

namespace YUIFramework
{
    /// <summary>
    /// UI 池对象条目。
    /// </summary>
    public sealed class UIPooledObject
    {
        public Type ContextType { get; }
        public string PrefabKey { get; }
        public BaseContext Context { get; }
        public GameObject ViewObject { get; }
        public DateTime CachedAt { get; }
        public bool IsValid => Context != null && ViewObject != null;
        public Guid EntryId { get; } = Guid.NewGuid();
        public UIPoolScope Scope { get; internal set; }
        public int Priority { get; internal set; }
        public double IdleTimeoutSeconds { get; internal set; }
        public long CachedTimestamp { get; internal set; }
        public string LeaseSource { get; internal set; }
        public bool HasResourceLease { get; internal set; }

        internal Guid OwnerPoolId { get; set; }
        internal bool IsIdle { get; set; }

        public UIPooledObject(Type contextType, string prefabKey, BaseContext context, GameObject viewObject)
        {
            ContextType = contextType ?? throw new ArgumentNullException(nameof(contextType));
            PrefabKey = prefabKey ?? string.Empty;
            Context = context;
            ViewObject = viewObject;
            CachedAt = DateTime.UtcNow;
            Scope = UIPoolScope.Global;
            LeaseSource = "Open";

            if (ViewObject != null)
            {
                ViewObject.SetActive(false);
            }
        }
    }
}
