using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ChitakTech
{
    // Read definitions, never instantiate PartModules or run converters in the background.
    internal sealed class SurvivalProfile
    {
        internal static readonly HashSet<string> ModuleNames = new HashSet<string>
        { "ChitakTechISRU", "ChitakTechGenerator", "ChitakTechStorage", "ChitakTechMaterialStorage", "ChitakTechDestructivePart" };
        private static readonly HashSet<string> Fields = new HashSet<string>
        { "name", "situationDestructive", "minAltitude", "maxAltitude", "minG", "maxG",
            "minTemperature", "maxTemperature", "minPressure", "maxPressure" };
        private readonly ConfigNode template;
        private readonly Dictionary<string, double> settings;
        private readonly string context;
        private ChitakTechISRURecipe parsed;
        private string location;

        private SurvivalProfile(ConfigNode source, ConfigNode saved, string context)
        {
            this.context = context;
            template = Project(source);
            parsed = new ChitakTechISRURecipe(template, context);
            settings = new Dictionary<string, double>();
            var savedRecipe = saved?.GetNode("RECIPE_SETTING_VALUES")?.GetNodes("RECIPE")
                .FirstOrDefault(n => n.GetValue("name")?.Trim() == source.GetValue("name")?.Trim());
            foreach (var setting in parsed._settings)
            {
                var entry = savedRecipe?.GetNodes("SETTING").FirstOrDefault(n => n.GetValue("var") == setting.variable);
                double value = setting.defaultValue;
                if (entry != null && double.TryParse(entry.GetValue("value"), NumberStyles.Float,
                        CultureInfo.InvariantCulture, out double number)) value = setting.Clamp(number);
                if (setting.IsChoice && entry?.GetValue("option") != null)
                    value = setting.Clamp(setting.options.IndexOf(entry.GetValue("option")));
                settings[setting.variable] = value;
            }
            parsed = parsed.WithSettingValues(settings);
        }

        internal ChitakTechISRUBiomeConfig At(ChitakTechRuntimeData data)
        {
            if (parsed.HasEnvironmentVariables && location != data.planet + "/" + data.biome)
            {
                location = data.planet + "/" + data.biome;
                parsed = new ChitakTechISRURecipe(template, context, settings,
                    new Dictionary<string, string> { { "currentBody", data.planet }, { "currentBiome", data.biome } });
            }
            return parsed.IsValid ? parsed.GetBiomeConfig(data.planet, data.biome) : null;
        }

        internal static SurvivalProfile Create(ConfigNode module, ConfigNode saved,
            IEnumerable<ChitakTechISRURecipe> globalRecipes, string context)
        {
            string type = module.GetValue("name");
            ConfigNode source = module;
            if (type == "ChitakTechISRU")
            {
                // An inventory processor on a physical part is inactive, just as in flight.
                if (ChitakTechISRU.IsPartEVAConfig(module)) return null;
                var candidates = module.GetNodes("RECIPE").ToList();
                var globals = globalRecipes.ToList();
                foreach (string name in module.GetNode("RECIPE_NAMES")?.GetValues("name") ?? new string[0])
                {
                    var recipe = globals.FirstOrDefault(r => r._name == name.Trim());
                    if (recipe != null) candidates.Add(recipe.mainNode);
                }
                string recipeType = module.GetValue("recipeType")
                    ?? (ChitakTechISRU.IsKerbalEVAConfig(module) ? "kerbalEVA" : null);
                string subtype = module.GetValue("recipeSubtype"), subsubtype = module.GetValue("recipeSubsubtype");
                if (recipeType != null || subtype != null || subsubtype != null)
                    candidates.AddRange(globals.Where(r => r.MatchesTypes(recipeType, subtype, subsubtype)).Select(r => r.mainNode));
                string selected = saved?.GetValue("activeRecipeName");
                var valid = candidates.Select(n => ChitakTechISRURecipe.ApplyModuleOverrides(module, n))
                    .Where(n => new ChitakTechISRURecipe(n, context).IsValid).ToList();
                var chosen = valid.FirstOrDefault(n => n.GetValue("name")?.Trim() == selected) ?? valid.FirstOrDefault();
                if (chosen == null) return null;
                source = chosen;
            }
            else if (type == "ChitakTechGenerator")
            {
                // Background survival only processes rules that can destroy the vessel.
                if (!string.Equals(module.GetValue("powerfail")?.Trim(), "EXPLODE",
                    StringComparison.OrdinalIgnoreCase)) return null;
                source = new ConfigNode(); module.CopyTo(source);
                string displayName = module.GetValue("displayName")?.Trim();
                source.SetValue("name", string.IsNullOrEmpty(displayName) ? "Unknown Generator" : displayName, true);
            }
            if (!source.HasNode("PLANET_CONFIG")) return null;
            // Solar flux is not part of the unloaded thermal model. Never substitute a
            // guessed value into a destruction threshold and destroy a vessel on that basis.
            var projected = Project(source);
            if (ContainsSolar(projected))
            {
                CTShared.LogWarning(context + ": background survival skipped: solarMultiplier in survival rules is unsupported.", "BackgroundSurvival/config");
                return null;
            }
            return new SurvivalProfile(source, saved, context);
        }

        private static bool ContainsSolar(ConfigNode node)
            => node.values.Cast<ConfigNode.Value>().Any(v => (v.value ?? "").Contains("solarMultiplier"))
                || node.nodes.Cast<ConfigNode>().Any(ContainsSolar);

        private static ConfigNode Project(ConfigNode source)
        {
            // Reuse the real settings, expression, temperature-unit, and biome parsers,
            // excluding output production, charging and operating-only restrictions.
            var node = new ConfigNode("RECIPE");
            node.AddValue("name", source.GetValue("name") ?? "Background survival"); node.AddValue("recipeTime", 1);
            var output = node.AddNode("OUTPUT_RESOURCE"); output.AddValue("name", "ElectricCharge"); output.AddValue("amount", 1);
            foreach (ConfigNode setting in source.GetNodes("SETTING"))
            { var copy = node.AddNode("SETTING"); setting.CopyTo(copy); }
            foreach (ConfigNode planet in source.GetNodes("PLANET_CONFIG"))
            {
                var p = node.AddNode("PLANET_CONFIG"); p.AddValue("name", planet.GetValue("name") ?? "ALL");
                foreach (ConfigNode biome in planet.GetNodes("BIOME_CONFIG"))
                {
                    var b = p.AddNode("BIOME_CONFIG");
                    foreach (ConfigNode.Value value in biome.values)
                        if (Fields.Contains(value.name)) b.AddValue(value.name, value.value);
                }
            }
            return node;
        }
    }
}
