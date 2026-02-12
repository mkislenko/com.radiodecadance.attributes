using System;
using System.Collections.Generic;
using RadioDecadance.GameplayTags;

namespace RadioDecadance.Attributes
{
    /// <summary>
    /// Float attribute that inherits ModifiableValue and augments itself with global
    /// modifiers from AttributeSystem based on its GameplayTag.
    /// </summary>
    [Serializable]
    public class AttributeFloat : ModifiableValue, UnityEngine.ISerializationCallbackReceiver
    {
        public GameplayTag Tag;
        
        // Track ids of injected global modifiers so we can refresh cleanly
        private List<int> _injectedIds = new();
        private AttributeSystem _system;

        public AttributeFloat(GameplayTag tag, float baseValue = 0f, AttributeSystem system = null) : base(baseValue)
        {
            Tag = tag;
            if (system != null) Register(system);
        }

        void UnityEngine.ISerializationCallbackReceiver.OnBeforeSerialize() { base.OnBeforeSerialize(); }
        void UnityEngine.ISerializationCallbackReceiver.OnAfterDeserialize()
        {
            // Unity won't run constructors/field initializers on prefab deserialization.
            // Ensure our internal collections are valid.
            if (_injectedIds == null) _injectedIds = new List<int>();
            // Also run base deserialization logic to recalc value/cache
            base.OnAfterDeserialize();
        }

        /// <summary>
        /// Register this attribute into provided AttributeSystem and pull matching modifiers.
        /// Safe to call multiple times; duplicates are avoided by first deregistering injected modifiers.
        /// </summary>
        public void Register(AttributeSystem system)
        {
            // Unsubscribe from previous system if any
            Deregister();
            _system = system;
            if (_system != null)
                _system.OnModifiersChanged += HandleModifiersChanged;
            RefreshGlobalModifiers();
        }
        
        private void HandleModifiersChanged(GameplayTag changedTag)
        {
            if (!Tag.IsValid) return;
            if (Tag.MatchesOrChildOf(changedTag))
                RefreshGlobalModifiers();
        }

        private void RefreshGlobalModifiers()
        {
            // remove previously injected
            if (_injectedIds == null) _injectedIds = new List<int>();
            for (int i = 0; i < _injectedIds.Count; i++)
                RemoveModifierById(_injectedIds[i]);
            _injectedIds.Clear();

            var sys = _system;
            if (sys == null || !Tag.IsValid) return;
            var mods = sys.GetMatchingModifiers(Tag);
            for (int i = 0; i < mods.Count; i++)
            {
                var id = AddModifier(mods[i]);
                _injectedIds.Add(id);
            }
        }

        public void Deregister()
        {
            if (_injectedIds == null) _injectedIds = new List<int>();
            if (_system != null)
                _system.OnModifiersChanged -= HandleModifiersChanged;
            // cleanup injected modifiers
            for (int i = 0; i < _injectedIds.Count; i++)
                RemoveModifierById(_injectedIds[i]);
            _injectedIds.Clear();
            _system = null;
        }
    }

    /// <summary>
    /// Int attribute backed by ModifiableInt and augmented with global modifiers
    /// from AttributeSystem based on its GameplayTag.
    /// </summary>
    [Serializable]
    public class AttributeInt : ModifiableInt, UnityEngine.ISerializationCallbackReceiver
    {
        public GameplayTag Tag;
        
        private List<int> _injectedIds = new();
        private AttributeSystem _system;

        public AttributeInt(GameplayTag tag, int baseValue = 0, AttributeSystem system = null) : base(baseValue)
        {
            Tag = tag;
            if (system != null) Register(system);
        }

        void UnityEngine.ISerializationCallbackReceiver.OnBeforeSerialize() { }
        void UnityEngine.ISerializationCallbackReceiver.OnAfterDeserialize()
        {
            if (_injectedIds == null) _injectedIds = new List<int>();
        }

        /// <summary>
        /// Register this attribute into provided AttributeSystem and pull matching modifiers.
        /// Safe to call multiple times; duplicates are avoided by first clearing injected modifiers.
        /// </summary>
        public void Register(AttributeSystem system)
        {
            Deregister();
            _system = system;
            if (_system != null)
                _system.OnModifiersChanged += HandleModifiersChanged;
            RefreshGlobalModifiers();
        }
        
        // Backward compatibility no-op
        public void Register() { /* no-op */ }
        
        private void HandleModifiersChanged(GameplayTag changedTag)
        {
            if (!Tag.IsValid) return;
            if (Tag.MatchesOrChildOf(changedTag))
                RefreshGlobalModifiers();
        }

        private void RefreshGlobalModifiers()
        {
            if (_injectedIds == null) _injectedIds = new List<int>();
            for (int i = 0; i < _injectedIds.Count; i++)
                RemoveModifierById(_injectedIds[i]);
            _injectedIds.Clear();

            var sys = _system;
            if (sys == null || !Tag.IsValid) return;
            var mods = sys.GetMatchingModifiers(Tag);
            for (int i = 0; i < mods.Count; i++)
            {
                var id = AddModifier(mods[i]);
                _injectedIds.Add(id);
            }
        }

        public void Deregister()
        {
            if (_injectedIds == null) _injectedIds = new List<int>();
            if (_system != null)
                _system.OnModifiersChanged -= HandleModifiersChanged;
            for (int i = 0; i < _injectedIds.Count; i++)
                RemoveModifierById(_injectedIds[i]);
            _injectedIds.Clear();
            _system = null;
        }
    }
}
