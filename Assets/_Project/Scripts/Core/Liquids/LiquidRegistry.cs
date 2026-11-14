using System;
using System.Collections.Generic;

namespace SpaceSim.Liquids
{
    /// <summary>
    /// Library of liquid definitions, reaction rules and miscibility exceptions. The physics code only ever reads data
    /// from here, so adding a liquid or a reaction never requires a code change: build a <see cref="LiquidDefinition"/>
    /// / <see cref="LiquidReaction"/> and register it. <see cref="LiquidLibrary.CreateDefaultRegistry"/> returns the
    /// starter set.
    /// </summary>
    public sealed class LiquidRegistry
    {
        readonly List<LiquidDefinition> liquids = new List<LiquidDefinition>();
        readonly Dictionary<string, LiquidDefinition> byId = new Dictionary<string, LiquidDefinition>(StringComparer.Ordinal);
        readonly List<LiquidReaction> reactions = new List<LiquidReaction>();
        readonly Dictionary<string, LiquidReaction> reactionById = new Dictionary<string, LiquidReaction>(StringComparer.Ordinal);
        readonly Dictionary<string, bool> miscibilityOverrides = new Dictionary<string, bool>(StringComparer.Ordinal);

        /// <summary>
        /// Two non-metallic liquids are miscible when their Hansen distance Ra is below this value [MPa^½]. 25 separates
        /// water–ethanol (24.0), water–ammonia (24.8) and water–glycerol (14.0) from water–hydrocarbons (≈ 45).
        /// </summary>
        public double MiscibilityThreshold = 25.0;

        public IReadOnlyList<LiquidDefinition> Liquids => liquids;
        public IReadOnlyList<LiquidReaction> Reactions => reactions;
        public int Count => liquids.Count;

        /// <summary>Registers a copy of the definition. Throws when the data is invalid or the id is taken.</summary>
        public LiquidDefinition Register(LiquidDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            List<string> issues = definition.Validate();
            if (issues.Count > 0) throw new ArgumentException($"Liquid '{definition.id}' is invalid: {string.Join(" ", issues)}", nameof(definition));
            if (byId.ContainsKey(definition.id)) throw new ArgumentException($"Liquid '{definition.id}' is already registered.", nameof(definition));
            LiquidDefinition copy = definition.Clone();
            liquids.Add(copy);
            byId.Add(copy.id, copy);
            return copy;
        }

        public bool Contains(string id) => id != null && byId.ContainsKey(id);

        public bool TryGet(string id, out LiquidDefinition definition)
        {
            if (id == null) { definition = null; return false; }
            return byId.TryGetValue(id, out definition);
        }

        public LiquidDefinition Get(string id)
        {
            if (!TryGet(id, out LiquidDefinition d)) throw new KeyNotFoundException($"Liquid '{id}' is not registered.");
            return d;
        }

        /// <summary>Registers a copy of a reaction rule. All referenced liquids must already be registered.</summary>
        public LiquidReaction RegisterReaction(LiquidReaction reaction)
        {
            if (reaction == null) throw new ArgumentNullException(nameof(reaction));
            List<string> issues = reaction.Validate();
            for (int i = 0; reaction.reactants != null && i < reaction.reactants.Length; i++)
                if (!Contains(reaction.reactants[i].liquidId)) issues.Add($"Unknown reactant '{reaction.reactants[i].liquidId}'.");
            for (int i = 0; reaction.products != null && i < reaction.products.Length; i++)
                if (!Contains(reaction.products[i].liquidId)) issues.Add($"Unknown product '{reaction.products[i].liquidId}'.");
            if (issues.Count > 0) throw new ArgumentException($"Reaction '{reaction.id}' is invalid: {string.Join(" ", issues)}", nameof(reaction));
            if (reactionById.ContainsKey(reaction.id)) throw new ArgumentException($"Reaction '{reaction.id}' is already registered.", nameof(reaction));
            LiquidReaction copy = reaction.Clone();
            reactions.Add(copy);
            reactionById.Add(copy.id, copy);
            return copy;
        }

        public bool TryGetReaction(string id, out LiquidReaction reaction)
        {
            if (id == null) { reaction = null; return false; }
            return reactionById.TryGetValue(id, out reaction);
        }

        /// <summary>Forces two liquids to be treated as miscible or immiscible regardless of the Hansen rule.</summary>
        public void SetMiscibility(string a, string b, bool miscible)
        {
            if (!Contains(a)) throw new KeyNotFoundException($"Liquid '{a}' is not registered.");
            if (!Contains(b)) throw new KeyNotFoundException($"Liquid '{b}' is not registered.");
            miscibilityOverrides[PairKey(a, b)] = miscible;
        }

        /// <summary>
        /// Miscibility rule: explicit override, else liquid metals mix only with liquid metals, else Hansen distance
        /// Ra &lt; <see cref="MiscibilityThreshold"/>. A liquid is always miscible with itself.
        /// </summary>
        public bool AreMiscible(string a, string b)
        {
            if (string.Equals(a, b, StringComparison.Ordinal)) return true;
            if (miscibilityOverrides.TryGetValue(PairKey(a, b), out bool forced)) return forced;
            LiquidDefinition da = Get(a), db = Get(b);
            if (da.isMetallic || db.isMetallic) return da.isMetallic && db.isMetallic;
            return LiquidChemistry.HansenDistance(da, db) < MiscibilityThreshold;
        }

        static string PairKey(string a, string b) =>
            string.CompareOrdinal(a, b) <= 0 ? a + "\u0001" + b : b + "\u0001" + a;
    }
}
