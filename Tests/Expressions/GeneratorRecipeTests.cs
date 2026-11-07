using System;
using System.Collections.Generic;
using ChitakTech;

internal static class GeneratorRecipeTests
{
    internal static int Run()
    {
        int count = 0;
        void Check(bool ok, string why) { count++; if (!ok) throw new Exception(why + ": " + string.Join("; ", CTShared.Errors)); }
        var module = new ConfigNode("MODULE");
        Check(GeneratorConfig.ReadPowerfail(module) == ChitakTechISRURecipe.PowerfailResult.Void,
            "missing generator powerfail defaults to VOID");
        foreach (var mode in new[] { "PAUSE", "VOID", "MAINT", "EXPLODE" })
        {
            module.SetValue("powerfail", mode, true);
            Check(GeneratorConfig.ReadPowerfail(module).ToString().ToUpperInvariant() == mode,
                "generator accepts " + mode);
        }
        var output = new ConfigNode("OUTPUT_RESOURCE"); module.AddNode(output);
        output.AddValue("name", "Water"); output.AddValue("amount", "2 * solarMultiplier * (SETTING:rate)");
        var setting = new ConfigNode("SETTING"); module.AddNode(setting);
        setting.AddValue("name", "Rate"); setting.AddValue("var", "rate"); setting.AddValue("min", 1); setting.AddValue("default", 2);
        var recipeNode = GeneratorConfig.Build(module, "Generator", "3*(SETTING:rate)").GetNode("RECIPE");
        var recipe = new ChitakTechISRURecipe(recipeNode, "Generator");
        Check(recipe.IsValid && recipe._recipeTime == 6 && recipe._outputs[0].amount == 4, "generator settings and interval parse");
        var env = new Dictionary<string,string> { { "currentBody", "Duna" }, { "currentBiome", "Midlands" }, { "solarMultiplier", "0.5" } };
        recipe = new ChitakTechISRURecipe(recipeNode, "Generator", null, env);
        Check(recipe.IsValid && recipe._outputs[0].amount == 2, "solar flux scales resource output");
        env["solarMultiplier"] = "0";
        recipe = new ChitakTechISRURecipe(recipeNode, "Generator", null, env);
        Check(recipe.IsValid && recipe._outputs[0].amount == 0, "darkness permits an empty batch");
        output.SetValue("name", "Water_[currentBody]_[currentBiome]");
        recipeNode = GeneratorConfig.Build(module, "Generator", "1").GetNode("RECIPE");
        recipe = new ChitakTechISRURecipe(recipeNode, "Generator", null, env);
        Check(recipe.IsValid && recipe._outputs[0].resourceName == "Water_Duna_Midlands", "runtime body and biome interpolate into names");
        Check(recipe.mainNode.GetNode("OUTPUT_RESOURCE").GetValue("name").Contains("[currentBody]"), "original environment template retained");
        env["currentBody"] = "Kerbin";
        Check(new ChitakTechISRURecipe(recipe.mainNode, "Generator", null, env)._outputs[0].resourceName == "Water_Kerbin_Midlands", "scene/body changes re-resolve template");
        Check(CTMathExpr.TryInterpolate("Origin [currentBody]", out string label, out _, env) && label == "Origin Kerbin", "CTMathExpr supports string variable insertion");
        Check(!CTMathExpr.TryInterpolateNumber("[currentBody]", out _, out _, env), "string values cannot masquerade as numbers");
        output.SetValue("amount", "[currentBody]");
        recipeNode = GeneratorConfig.Build(module, "Generator", "1").GetNode("RECIPE");
        Check(!new ChitakTechISRURecipe(recipeNode, "Generator", null, env).IsValid, "numeric use of body name rejected");
        recipeNode.SetValue("recipeType", "[currentBody]");
        Check(!new ChitakTechISRURecipe(recipeNode, "Generator", null, env).IsValid, "environment cannot alter recipe selection");
        var biome = new ConfigNode("BIOME_CONFIG"); var planet = new ConfigNode("PLANET_CONFIG"); module.AddNode(planet); planet.AddNode(biome);
        biome.AddValue("intervalMul", 2); biome.AddValue("outResourcesMul", 3); biome.AddValue("outMaterialMul", 4);
        var translated = GeneratorConfig.Build(module, "Generator", "1").GetNode("RECIPE").GetNode("PLANET_CONFIG").GetNode("BIOME_CONFIG");
        Check(translated.GetValue("speedMul") == "2" && translated.GetValue("outMul") == "3" && translated.GetValue("inMul") == "4", "independent custom multipliers map to shared engine");
        biome.AddValue("chargeRateMul", 2);
        try { GeneratorConfig.Build(module, "Generator", "1"); Check(false, "unrelated multipliers must not be silently accepted"); }
        catch (FormatException) { Check(true, "unsupported multiplier rejected"); }

        var materialModule = new ConfigNode("MODULE");
        var choice = materialModule.AddNode("SETTING");
        choice.AddValue("name", "Shape"); choice.AddValue("var", "shape");
        var options = choice.AddNode("OPTIONS"); options.AddValue("option", "Log"); options.AddValue("option", "HalfLog");
        var material = materialModule.AddNode("OUTPUT_MATERIAL");
        material.AddValue("id", "log"); material.AddValue("name", "Wood");
        material.AddValue("shape", "(SETTING:shape)"); material.AddValue("size", "1");
        material.AddValue("amount", "[2*solarMultiplier]"); material.AddValue("outVolume", "[PI*Pow(radius,2)*height]");
        var parameters = material.AddNode("PARAMS"); parameters.AddValue("radius", "0.1"); parameters.AddValue("height", "1");
        parameters.AddValue("sourceScatter", "[currentBody]/[currentBiome]");
        recipeNode = GeneratorConfig.Build(materialModule, "Material generator", "2*(OUTMAT:log:amount)").GetNode("RECIPE");
        env["solarMultiplier"] = "1";
        recipe = new ChitakTechISRURecipe(recipeNode, "Material generator", null, env);
        Check(recipe.IsValid && recipe._outputMaterials[0].shape == "Log"
            && recipe._outputMaterials[0].parameters["sourceScatter"] == "Kerbin/Midlands",
            "material generator supports dropdowns, OUTMAT interval and environment parameters");
        recipe = recipe.WithSettingValues(new Dictionary<string,double> { { "shape", 1 } });
        Check(recipe.IsValid && recipe._outputMaterials[0].shape == "HalfLog"
            && recipe._outputMaterials[0].parameters["sourceScatter"] == "Kerbin/Midlands", "changing settings preserves the environment");
        env["solarMultiplier"] = "0";
        recipe = new ChitakTechISRURecipe(recipeNode, "Material generator", null, env);
        Check(recipe.IsValid && recipe._outputMaterials[0].amount == 0, "material generation can scale to zero in darkness");
        material.SetValue("amount", "[ currentBody ]");
        recipeNode = GeneratorConfig.Build(materialModule, "Material generator", "1").GetNode("RECIPE");
        Check(!new ChitakTechISRURecipe(recipeNode, "Material generator", null, env).IsValid, "spaced string imports cannot be numeric");
        return count;
    }
}
