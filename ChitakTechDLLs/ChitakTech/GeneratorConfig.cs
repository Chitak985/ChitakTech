using System;
using System.Linq;

namespace ChitakTech
{
    internal static class GeneratorConfig
    {
        internal static ConfigNode Build(ConfigNode source, string name, string interval)
        {
            var module = new ConfigNode("MODULE");
            module.AddValue("name", "ChitakTechGenerator");
            module.AddValue("ConverterName", name);
            module.AddValue("StartActionName", "Start generator");
            module.AddValue("StopActionName", "Stop generator");
            var recipe = module.AddNode("RECIPE");
            recipe.AddValue("name", name);
            recipe.AddValue("recipeType", "ChitakTechGenerator");
            recipe.AddValue("isGenerator", true);
            recipe.AddValue("recipeTime", interval);
            foreach (ConfigNode child in source.nodes)
            {
                if (!new[] { "OUTPUT_RESOURCE", "OUTPUT_MATERIAL", "SETTING", "PLANET_CONFIG", "MAINTENANCE" }.Contains(child.name))
                    throw new FormatException("Unsupported generator node " + child.name);
                var copy = new ConfigNode(child.name); child.CopyTo(copy); TranslateMultipliers(copy); recipe.AddNode(copy);
            }
            return module;
        }
        private static void TranslateMultipliers(ConfigNode node)
        {
            if (node.name == "BIOME_CONFIG" && node.nodes.Cast<ConfigNode>().Any(n => n.name != "OUTPUT_RESOURCE"))
                throw new FormatException("Generator BIOME_CONFIG only supports OUTPUT_RESOURCE child nodes.");
            // The shared engine has separate input/output factors. Generators have no
            // inputs, so inMul is the internal slot used solely for material output.
            if (node.name == "BIOME_CONFIG" || node.name == "STAGE")
                foreach (ConfigNode.Value value in node.values)
                {
                    if (!value.name.EndsWith("Mul", StringComparison.Ordinal)) continue;
                    switch (value.name)
                    {
                        case "intervalMul": value.name = "speedMul"; break;
                        case "outResourcesMul": value.name = "outMul"; break;
                        case "outMaterialMul": value.name = "inMul"; break;
                        default: throw new FormatException("Unsupported generator multiplier " + value.name);
                    }
                }
            foreach (ConfigNode child in node.nodes) TranslateMultipliers(child);
        }
    }
}
