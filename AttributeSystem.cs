using System;
using System.Collections.Generic;
using System.Linq;
using RadioDecadance.GameplayTags;

namespace RadioDecadance.Attributes
{
    /// <summary>
    /// Central runtime system for attribute modifiers addressed by GameplayTag.
    /// Holds a list of modifiers per tag. A modifier applies to an attribute if
    /// the attribute tag MatchesOrChildOf(modifierTag).
    /// </summary>
    public class AttributeSystem
    {
        public static AttributeSystem Instance { get; private set; }

        public event Action<GameplayTag> OnModifiersChanged;
        
        private readonly Dictionary<GameplayTag, List<ValueModifier>> _modifiers = new();

        public AttributeSystem()
        {
            Instance = this;
        }
        
        /// <summary>
        /// Adds a modifier under a tag. Any attribute whose tag MatchesOrChildOf(modTag) will be affected.
        /// Returns the unique id of the added modifier, or 0 if not added (invalid input).
        /// </summary>
        public int AddModifier(GameplayTag modTag, ValueModifier modifier)
        {
            if (!modTag.IsValid || modifier == null) return 0;
            if (!_modifiers.TryGetValue(modTag, out var list))
            {
                list = new ListValueModifiers();
                _modifiers.Add(modTag, list);
            }
            list.Add(modifier);
            OnModifiersChanged?.Invoke(modTag);
            return modifier.Id;
        }

        /// <summary>
        /// Removes a modifier with the specified id under the given tag.
        /// Returns true if a modifier was found and removed.
        /// </summary>
        public bool RemoveModifierById(GameplayTag modTag, int id)
        {
            if (!modTag.IsValid) return false;
            if (_modifiers.TryGetValue(modTag, out var list))
            {
                int idx = list.FindIndex(m => m != null && m.Id == id);
                if (idx >= 0)
                {
                    list.RemoveAt(idx);
                    if (list.Count == 0)
                        _modifiers.Remove(modTag);
                    OnModifiersChanged?.Invoke(modTag);
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Returns a new list of all modifiers that affect the provided attributeTag.
        /// </summary>
        public List<ValueModifier> GetMatchingModifiers(GameplayTag attributeTag)
        {
            var result = new List<ValueModifier>();
            foreach (var kv in _modifiers)
            {
                if (!attributeTag.MatchesOrChildOf(kv.Key)) continue;
                var list = kv.Value;
                for (int i = 0; i < list.Count; i++)
                {
                    var m = list[i];
                    if (m != null) result.Add(m);
                }
            }
            // order not guaranteed here; callers may sort by m.Order
            return result;
        }

        /// <summary>
        /// Computes modified float value by applying all matching modifiers to the base value
        /// in ascending Order using their operations.
        /// </summary>
        public float EvaluateFloat(GameplayTag attributeTag, float baseValue)
        {
            float value = baseValue;
            var mods = GetMatchingModifiers(attributeTag);
            if (mods.Count == 0) return value;
            foreach (var m in mods.OrderBy(m => m.Order))
            {
                switch (m.Operation)
                {
                    case ModifierOperation.Add:
                        value += m.Amount;
                        break;
                    case ModifierOperation.Subtract:
                        value -= m.Amount;
                        break;
                    case ModifierOperation.Multiply:
                        value *= m.Amount;
                        break;
                    case ModifierOperation.Divide:
                        value /= m.Amount;
                        break;
                }
            }
            return value;
        }

        /// <summary>
        /// Computes modified int value by applying all matching modifiers to the base value as float,
        /// then floors the result.
        /// </summary>
        public int EvaluateInt(GameplayTag attributeTag, int baseValue)
        {
            float f = EvaluateFloat(attributeTag, baseValue);
            return (int)Math.Floor(f);
        }

        private sealed class ListValueModifiers : List<ValueModifier> { }
    }
}
