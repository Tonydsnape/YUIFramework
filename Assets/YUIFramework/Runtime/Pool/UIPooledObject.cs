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

        public UIPooledObject(Type contextType, string prefabKey, BaseContext context, GameObject viewObject)
        {
            ContextType = contextType ?? throw new ArgumentNullException(nameof(contextType));
            PrefabKey = prefabKey ?? string.Empty;
            Context = context;
            ViewObject = viewObject;
            CachedAt = DateTime.UtcNow;

            if (ViewObject != null)
            {
                ViewObject.SetActive(false);
            }
        }
    }
}
