using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using ChitakTech;

internal static class WashingRecipeTests
{
    internal static int Run()
    {
        int count = 0;
        void Check(bool ok, string error) { count++; if (!ok) throw new Exception(error); }
        var node = CreateRecipe();
        CTShared.Errors.Clear();
        var recipe = new ChitakTechISRURecipe(node, "Washing Basin");
        Check(recipe.IsValid, string.Join("; ", CTShared.Errors));
        var definition = new ChitakTechMaterial
        {
            name = "Water", shapes = new List<string> { "Fluid" },
            parameters = new Dictionary<string, string>
            {
                { "m3", "DER[amount*volume]" }, { "pH", "7" },
                { "tts", "0" }, { "turbidity", "0" }
            }
        };
        Check(recipe.ValidateReferences(new[] { definition }, "test"), string.Join("; ", CTShared.Errors));
        var input = recipe._inputMaterials.Single();
        var output = recipe._outputMaterials.Single();
        Check(input.amount == 50000 && output.amount == 50000, "Expected 50,000 cubic-centimeter units");
        Check(CTMathExpr.TryInterpolateNumber(output.outVolume, out double unitVolume, out _), "Per-unit volume parses");
        Check(Math.Abs(output.amount * unitVolume - 0.05) < 1e-12, "Output must total 50 liters");
        var water = definition.parameters.Where(p => !p.Value.StartsWith("DER")).ToDictionary(p => p.Key, p => p.Value);
        var variables = new Dictionary<string, string> { ["amount"] = "50000", ["volume"] = "0.000001" };
        Check(CTMathExpr.TryInterpolateNumber(definition.parameters["m3"].Substring(3), out double m3, out _, variables), "Water m3 derives");
        water["m3"] = m3.ToString("R", CultureInfo.InvariantCulture);
        foreach (var condition in input.parameters)
            Check(MaterialParameterCondition.TryResolve(condition.Value, null, out var resolved, out _)
                && CTShared.EvaluateParamComparison(water[condition.Key], resolved), "Clean 50-liter batch passes " + condition.Key);
        var produced = new Dictionary<string, string>();
        foreach (var assignment in output.parameterAssignments)
        {
            string expression = Regex.Replace(assignment.Value, @"\(INMAT:water:([^)]*)\)", m => water[m.Groups[1].Value]);
            Check(CTMathExpr.TryInterpolate(expression, out string value, out string error, produced), assignment.Key + ": " + error);
            produced[assignment.Key] = value;
        }
        Check(double.Parse(produced["tts"], CultureInfo.InvariantCulture) == 19000, "950g/0.05m3 must yield 19,000 mg/L");
        double turbidity = double.Parse(produced["turbidity"], CultureInfo.InvariantCulture);
        Check(turbidity >= 19000.0/60 && turbidity <= 19000.0/25,
            "later parameters see the updated suspended-solids value");
        return count;
    }

    private static ConfigNode CreateRecipe()
    {
        // Only the behavior under test: whole cm3 units, repeated bounds, INMAT,
        // random expressions, and an ordered parameter depending on an earlier one.
        var recipe = new ConfigNode("CHITAKTECHISRU_RECIPE");
        recipe.AddValue("name", "Test water treatment"); recipe.AddValue("recipeTime", 600);
        var input = recipe.AddNode("INPUT_MATERIAL");
        input.AddValue("id", "water"); input.AddValue("name", "Water");
        input.AddValue("shape", "Fluid"); input.AddValue("size", "none"); input.AddValue("amount", 50000);
        var conditions = input.AddNode("PARAMS");
        conditions.AddValue("m3", "EM0.049"); conditions.AddValue("m3", "EL0.051");
        conditions.AddValue("pH", "EM5.5"); conditions.AddValue("pH", "EL9.0");
        conditions.AddValue("tts", "EL2000"); conditions.AddValue("turbidity", "EL500");
        var output = recipe.AddNode("OUTPUT_MATERIAL");
        output.AddValue("id", "washwater"); output.AddValue("name", "Water");
        output.AddValue("shape", "Fluid"); output.AddValue("size", "none"); output.AddValue("amount", 50000);
        output.AddValue("outVolume", "0.000001");
        var parameters = output.AddNode("PARAMS");
        parameters.AddValue("pH", "[Min(9.2,Max(5.0,(INMAT:water:pH)+randf(-0.3,0.2)))]");
        parameters.AddValue("tts", "[(INMAT:water:tts)+(950/(INMAT:water:m3))]");
        parameters.AddValue("turbidity", "[tts/randf(25,60)]");
        return recipe;
    }
}
