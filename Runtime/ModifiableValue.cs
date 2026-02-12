using System;
using System.Collections.Generic;
using System.Linq;
using RadioDecadance.GameplayTags;
using UnityEngine;

namespace RadioDecadance.Attributes
{
    /// <summary>
    ///     Operation types supported by <see cref="ValueModifier" />.
    /// </summary>
    public enum ModifierOperation
    {
        Add = 0,
        Subtract = 1,
        Multiply = 2,
        Divide = 3,
        Override = 4
    }

    /// <summary>
    ///     Represents a single modifier that can be applied to a numeric value.
    /// </summary>
    [Serializable]
    public sealed class ValueModifier
    {
        private static int _nextId = 1;

        [SerializeField] private ModifierOperation operation;
        [SerializeField] private float amount;
        [SerializeField] private int order;
        [SerializeField] private GameplayTag sourceTag;

        public int Id { get; }
        public ModifierOperation Operation => operation;
        public float Amount => amount;

        /// <summary>
        ///     Order in which this modifier is applied. Lower values are applied first.
        ///     If you don't care about order, keep it 0.
        /// </summary>
        public int Order => order;

        /// <summary>
        ///     Optional source tag to help manage/removal in gameplay code.
        /// </summary>
        public GameplayTag SourceTag => sourceTag;

        public ValueModifier(ModifierOperation operation, float amount, int order = 0, GameplayTag sourceTag = default)
        {
            if (operation == ModifierOperation.Divide)
            {
                if (Mathf.Approximately(amount, 0f))
                {
                    throw new ArgumentException("Division modifier amount cannot be 0.", nameof(amount));
                }
            }

            Id = _nextId++;
            this.operation = operation;
            this.amount = amount;
            this.order = order;
            this.sourceTag = sourceTag;
        }

        public override string ToString()
        {
            return $"[#{Id}] {operation} {amount} (order {order}) tag='{sourceTag}'";
        }
    }

    /// <summary>
    ///     A float-based value that can be modified by a set of <see cref="ValueModifier" />s.
    ///     Useful for things like movement speed, damage, cooldowns, etc.
    /// </summary>
    [Serializable]
    public class ModifiableValue : ISerializationCallbackReceiver
    {
        /// <summary>Invoked when the effective value changes after recalculation.</summary>
        public event Action<float> OnValueChanged;

        [SerializeField] private float baseValue;
        [SerializeField] private float minValue = float.NegativeInfinity;
        [SerializeField] private float maxValue = float.PositiveInfinity;

        private List<ValueModifier> _modifiers = new();

        private float _cachedValue;
        private bool _dirty = true;

        /// <summary>The unmodified base value.</summary>
        public float BaseValue
        {
            get => baseValue;
            set
            {
                if (!Mathf.Approximately(baseValue, value))
                {
                    baseValue = value;
                    _dirty = true;
                    RecalculateIfDirty();
                }
            }
        }

        /// <summary>Minimum allowed value for the calculated result (inclusive).</summary>
        public float Min
        {
            get => minValue;
            set
            {
                if (!Mathf.Approximately(minValue, value))
                {
                    minValue = value;

                    // Ensure min <= max
                    if (minValue > maxValue)
                    {
                        maxValue = minValue;
                    }

                    _dirty = true;
                    RecalculateIfDirty();
                }
            }
        }

        /// <summary>Maximum allowed value for the calculated result (inclusive).</summary>
        public float Max
        {
            get => maxValue;
            set
            {
                if (!Mathf.Approximately(maxValue, value))
                {
                    maxValue = value;

                    // Ensure min <= max
                    if (maxValue < minValue)
                    {
                        minValue = maxValue;
                    }

                    _dirty = true;
                    RecalculateIfDirty();
                }
            }
        }

        /// <summary>The list of current modifiers (read-only snapshot).</summary>
        public IReadOnlyList<ValueModifier> Modifiers => _modifiers;

        /// <summary>The effective value after applying all modifiers.</summary>
        public float Value
        {
            get
            {
                RecalculateIfDirty();

                return _cachedValue;
            }
        }

        public ModifiableValue(float baseValue = 0f)
        {
            this.baseValue = baseValue;
            _cachedValue = baseValue;
            _dirty = true;
        }

        /// <summary>
        ///     Adds a modifier and returns its unique id.
        /// </summary>
        public int AddModifier(ValueModifier modifier)
        {
            if (modifier == null)
            {
                throw new ArgumentNullException(nameof(modifier));
            }

            int positionToInsert = 0;

            for (; positionToInsert < _modifiers.Count; positionToInsert++)
            {
                if (_modifiers[positionToInsert].Order < modifier.Order)
                {
                    continue;
                }

                if (_modifiers[positionToInsert].Order == modifier.Order &&
                    _modifiers[positionToInsert].Id < modifier.Id)
                {
                    continue;
                }

                break;
            }

            _modifiers.Insert(positionToInsert, modifier);

            _dirty = true;
            RecalculateIfDirty();

            return modifier.Id;
        }

        /// <summary>
        ///     Convenience: add an additive modifier (Add or Subtract by sign) with optional order and tag.
        /// </summary>
        public int AddAdditive(float amount, int order = 0, GameplayTag sourceTag = default)
        {
            ModifierOperation op = amount >= 0 ? ModifierOperation.Add : ModifierOperation.Subtract;

            return AddModifier(new ValueModifier(op, Mathf.Abs(amount), order, sourceTag));
        }

        /// <summary>
        ///     Convenience: add a multiplicative modifier (Multiply or Divide by sign) with optional order and tag.
        ///     For divide, pass values &lt; 1 to reduce (or use Divide explicitly via ValueModifier).
        /// </summary>
        public int AddMultiplier(float factor, int order = 0, GameplayTag sourceTag = default)
        {
            if (Mathf.Approximately(factor, 0f))
            {
                throw new ArgumentException("Multiplier factor cannot be 0. Use a very small number or reconsider.",
                    nameof(factor));
            }

            if (factor > 0f)
            {
                return AddModifier(new ValueModifier(ModifierOperation.Multiply, factor, order, sourceTag));
            }

            // Negative factor multiplies by a negative number – keep as multiply.
            return AddModifier(new ValueModifier(ModifierOperation.Multiply, factor, order, sourceTag));
        }

        /// <summary>
        ///     Checks if the provided modifier could be applied such that the resulting value (before clamping)
        ///     would lie within [Min, Max]. Does not mutate internal state.
        ///     Returns true if within range; otherwise false.
        /// </summary>
        public bool CanAddModifier(ValueModifier modifier)
        {
            if (modifier == null)
            {
                throw new ArgumentNullException(nameof(modifier));
            }

            // Simulate the calculation with the new modifier included
            float val = baseValue;

            // Build a temporary sorted list including the new modifier respecting order then id
            var temp = new List<ValueModifier>(_modifiers.Count + 1);
            temp.AddRange(_modifiers);
            temp.Add(modifier);
            temp = temp.OrderBy(m => m.Order).ThenBy(m => m.Id).ToList();

            // If any Override modifiers exist, choose the highest-priority one (lower Order wins, then lower Id)
            ValueModifier firstOverride = temp.FirstOrDefault(m => m.Operation == ModifierOperation.Override);
            if (firstOverride != null)
            {
                val = firstOverride.Amount;
            }
            else
            {
                foreach (ValueModifier mod in temp)
                {
                    switch (mod.Operation)
                    {
                        case ModifierOperation.Add:
                            val += mod.Amount;

                            break;
                        case ModifierOperation.Subtract:
                            val -= mod.Amount;

                            break;
                        case ModifierOperation.Multiply:
                            val *= mod.Amount;

                            break;
                        case ModifierOperation.Divide:
                            if (!Mathf.Approximately(mod.Amount, 0f))
                            {
                                val /= mod.Amount;
                            }

                            break;
                    }
                }
            }

            // Check range BEFORE clamping; only allow if within [Min, Max]
            return !(val < minValue || val > maxValue);
        }

        [Obsolete("Use GameplayTag-based overloads")]
        public int AddAdditive(float amount, int order, string sourceTag)
        {
            return AddAdditive(amount, order, GameplayTag.FromString(sourceTag));
        }

        [Obsolete("Use GameplayTag-based overloads")]
        public int AddMultiplier(float factor, int order, string sourceTag)
        {
            return AddMultiplier(factor, order, GameplayTag.FromString(sourceTag));
        }

        public bool RemoveModifierById(int id)
        {
            int idx = _modifiers.FindIndex(m => m.Id == id);

            if (idx >= 0)
            {
                _modifiers.RemoveAt(idx);
                _dirty = true;
                RecalculateIfDirty();

                return true;
            }

            return false;
        }

        public int RemoveModifiersByTag(GameplayTag tagOrParent)
        {
            if (!tagOrParent.IsValid)
            {
                return 0;
            }

            int removed = _modifiers.RemoveAll(m =>
                !m.SourceTag.IsNone && (m.SourceTag == tagOrParent || m.SourceTag.MatchesOrChildOf(tagOrParent)));

            if (removed > 0)
            {
                _dirty = true;
                RecalculateIfDirty();
            }

            return removed;
        }

        public void ClearModifiers()
        {
            if (_modifiers.Count == 0)
            {
                return;
            }

            _modifiers.Clear();
            _dirty = true;
            RecalculateIfDirty();
        }

        public void OnBeforeSerialize()
        {
            // Nothing to do before serialize
        }

        public void OnAfterDeserialize()
        {
            // Unity does not run constructors/field initializers on deserialized objects
            // Ensure the value is recalculated on load
            _cachedValue = baseValue;
            _dirty = true;
            RecalculateIfDirty();
        }

        public override string ToString()
        {
            return $"Base={baseValue}, Value={Value}, Modifiers={_modifiers.Count}";
        }

        private void RecalculateIfDirty()
        {
            if (!_dirty)
            {
                return;
            }

            float val = baseValue;

            // If any Override modifiers exist, use the highest-priority one (lower Order, then lower Id) and ignore others
            var overrideMod = _modifiers.FirstOrDefault(m => m.Operation == ModifierOperation.Override);
            if (overrideMod != null)
            {
                val = overrideMod.Amount;
            }
            else
            {
                // Apply in the stored order (Order then Id). This gives full control to the caller.
                foreach (ValueModifier mod in _modifiers)
                {
                    switch (mod.Operation)
                    {
                        case ModifierOperation.Add:
                            val += mod.Amount;

                            break;
                        case ModifierOperation.Subtract:
                            val -= mod.Amount;

                            break;
                        case ModifierOperation.Multiply:
                            val *= mod.Amount;

                            break;
                        case ModifierOperation.Divide:
                            if (Mathf.Approximately(mod.Amount, 0f))
                            {
                                Debug.LogWarning("Attempted to divide by 0 in ValueModifier. Ignoring this modifier.");
                            }
                            else
                            {
                                val /= mod.Amount;
                            }

                            break;
                    }
                }
            }

            // Clamp final value to [Min, Max]
            val = Mathf.Clamp(val, minValue, maxValue);

            if (!Mathf.Approximately(_cachedValue, val))
            {
                _cachedValue = val;
                OnValueChanged?.Invoke(_cachedValue);
            }

            _dirty = false;
        }
    }

    /// <summary>
    ///     Rounding modes for converting the float-based <see cref="ModifiableValue" /> to an int.
    /// </summary>
    public enum IntRoundingMode
    {
        Round = 0,
        Floor = 1,
        Ceil = 2,
        Truncate = 3
    }

    /// <summary>
    ///     A convenience wrapper that exposes an integer value using a float-based ModifiableValue underneath.
    ///     This lets you work with ints while still stacking add/mul modifiers in a predictable way.
    /// </summary>
    [Serializable]
    public class ModifiableInt
    {
        [SerializeField] private ModifiableValue raw;
        [SerializeField] private IntRoundingMode roundingMode = IntRoundingMode.Round;

        public ModifiableValue Raw => raw;

        public IntRoundingMode RoundingMode
        {
            get => roundingMode;
            set => roundingMode = value;
        }

        public int Value
        {
            get
            {
                float v = raw.Value;

                switch (roundingMode)
                {
                    case IntRoundingMode.Floor: return Mathf.FloorToInt(v);
                    case IntRoundingMode.Ceil: return Mathf.CeilToInt(v);
                    case IntRoundingMode.Truncate: return (int)v;
                    default: return Mathf.RoundToInt(v);
                }
            }
        }

        public int BaseValue
        {
            get => Mathf.RoundToInt(raw.BaseValue);
            set => raw.BaseValue = value; // implicit conversion to float
        }

        /// <summary>
        ///     Minimum allowed integer value (mapped to underlying float Min). Uses RoundToInt on read.
        /// </summary>
        public int Min
        {
            get => Mathf.RoundToInt(raw.Min);
            set => raw.Min = value;
        }

        /// <summary>
        ///     Maximum allowed integer value (mapped to underlying float Max). Uses RoundToInt on read.
        /// </summary>
        public int Max
        {
            get => Mathf.RoundToInt(raw.Max);
            set => raw.Max = value;
        }

        public ModifiableInt(int baseValue = 0, IntRoundingMode rounding = IntRoundingMode.Round)
        {
            raw = new ModifiableValue(baseValue);
            roundingMode = rounding;
        }

        public int AddModifier(ValueModifier modifier)
        {
            return raw.AddModifier(modifier);
        }

        public int AddAdditive(int amount, int order = 0, GameplayTag sourceTag = default)
        {
            return raw.AddAdditive(amount, order, sourceTag);
        }

        public int AddMultiplier(float factor, int order = 0, GameplayTag sourceTag = default)
        {
            return raw.AddMultiplier(factor, order, sourceTag);
        }

        public bool CanAddModifier(ValueModifier modifier)
        {
            return raw.CanAddModifier(modifier);
        }

        public bool RemoveModifierById(int id)
        {
            return raw.RemoveModifierById(id);
        }

        public int RemoveModifiersByTag(GameplayTag tag)
        {
            return raw.RemoveModifiersByTag(tag);
        }

        public void ClearModifiers()
        {
            raw.ClearModifiers();
        }

        [Obsolete("Use GameplayTag-based overloads")]
        public int AddAdditive(int amount, int order, string sourceTag)
        {
            return raw.AddAdditive(amount, order, GameplayTag.FromString(sourceTag));
        }

        [Obsolete("Use GameplayTag-based overloads")]
        public int AddMultiplier(float factor, int order, string sourceTag)
        {
            return raw.AddMultiplier(factor, order, GameplayTag.FromString(sourceTag));
        }

        [Obsolete("Use GameplayTag-based overloads")]
        public int RemoveModifiersByTag(string tag)
        {
            return raw.RemoveModifiersByTag(GameplayTag.FromString(tag));
        }

        public override string ToString()
        {
            return $"Base={BaseValue}, Value={Value}, Rounding={roundingMode}, Mods={raw.Modifiers.Count}";
        }
    }
}