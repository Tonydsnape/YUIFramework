using System;
using UnityEngine;

namespace YUIFramework.Localization
{
    [Serializable]
    public struct LocalizedTextKey
    {
        [SerializeField] private string key;
        public LocalizedTextKey(string value) { key = value; }
        public string Key => key;
    }
}
