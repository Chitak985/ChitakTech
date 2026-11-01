using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace ChitakTech
{
    internal static class RecipeEnvironment
    {
        private static readonly Regex Tokens = new Regex(@"\[\s*(currentBiome|currentBody)\s*\]|\bsolarMultiplier\b");
        internal static bool Contains(ConfigNode node) => node != null &&
            (node.values.Cast<ConfigNode.Value>().Any(v => Tokens.IsMatch(v.value ?? ""))
             || node.nodes.Cast<ConfigNode>().Any(Contains));

        internal static bool Resolve(ConfigNode node, IDictionary<string, string> variables, out string error, bool root = true)
        {
            error = null;
            foreach (ConfigNode.Value value in node.values)
            {
                string text = value.value ?? "";
                if (!Tokens.IsMatch(text)) continue;
                if (node.name == "SETTING" || node.name == "OPTIONS" || node.name == "OPTIONS_DISPLAY"
                    || node.name == "SETTING_CONDITIONS" || value.name == "id"
                    || root && (value.name == "name" || value.name.StartsWith("recipe") && value.name != "recipeTime"))
                { error = "Environment variables cannot change recipe identity, ids or setting definitions: " + value.name; return false; }
                bool numeric = node.name != "PARAMS" && node.name != "PARAM_REQUIREMENTS"
                    && (value.name == "amount" || value.name == "recipeTime" || value.name == "outVolume"
                        || value.name.EndsWith("Mul") || value.name.StartsWith("min") || value.name.StartsWith("max"));
                if (numeric && Tokens.Matches(text).Cast<Match>().Any(m => m.Groups[1].Success))
                { error = value.name + " requires a number; currentBody/currentBiome are strings."; return false; }
                text = Tokens.Replace(text, match => {
                    string key = match.Value == "solarMultiplier" ? match.Value : match.Groups[1].Value;
                    return variables != null && variables.TryGetValue(key, out string result) ? result
                        : key == "solarMultiplier" ? "1" : "__" + key + "__";
                });
                if (numeric && value.name != "recipeTime" && value.name != "outVolume"
                    && !text.Contains("(SETTING:") && !text.Contains("(OUTMAT:") && !text.Contains("(INMAT:"))
                {
                    if (!CTMathExpr.TryEvaluateNumericExpression(text, out double number, out error)) return false;
                    text = number.ToString("R", CultureInfo.InvariantCulture);
                }
                value.value = text;
            }
            foreach (ConfigNode child in node.nodes)
                if (!Resolve(child, variables, out error, false)) return false;
            return true;
        }
    }

    public partial class ChitakTechISRURecipe
    {
        internal bool HasEnvironmentVariables;
        internal IDictionary<string, string> _environmentValues;
    }

}
