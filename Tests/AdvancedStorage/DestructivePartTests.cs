using ChitakTech;

static partial class Program
{
    static void DestructivePartTests()
    {
        ChitakTechDestructivePart Create(ConfigNode config, PartModule conflict = null)
        {
            UnityEngine.Time.fixedTime++;
            var part = new Part { vessel = new Vessel(), moduleConfig = config };
            var module = new ChitakTechDestructivePart { part = part };
            part.Modules.Add(module);
            if (conflict != null) part.Modules.Add(conflict);
            module.OnStart(PartModule.StartState.None);
            return module;
        }
        var rule = EnvironmentConfig("ALL", "maxTemperature", "300");
        var module = Create(rule);
        module.part.vessel.externalTemperature = 300;
        module.FixedUpdate();
        Equal(0, module.part.explosions, "survival boundary inclusive");
        UnityEngine.Time.fixedTime++;
        module.part.vessel.externalTemperature = 301;
        module.FixedUpdate(); module.FixedUpdate();
        Equal(1, module.part.explosions, "destruction happens once");

        foreach (PartModule conflict in new PartModule[] { new ChitakTechISRU(), new ChitakTechStorage(),
            new ChitakTechMaterialStorage(), new ChitakTechDestructivePart() })
        {
            module = Create(EnvironmentConfig("ALL", "maxTemperature", "1"), conflict);
            module.FixedUpdate();
            Equal(0, module.enabled || module.isEnabled ? 1 : 0, "conflict disables destructive module");
            Equal(0, module.part.explosions, "conflict prevents destruction");
            Equal(1, conflict.enabled ? 1 : 0, "other module untouched");
        }
        module = Create(rule);
        module.part.Modules.Add(new ChitakTechISRU());
        module.FixedUpdate();
        Equal(0, module.enabled ? 1 : 0, "late-added conflict detected");

        foreach (string field in new[] { "volumeMul", "speedMul", "disabled", "minOperatingG", "depositCondition" })
            Equal(0, ChitakTechDestructivePart.ValidateConfig(EnvironmentConfig("ALL", field, "1")) ? 1 : 0,
                "unsupported biome field rejected");
        var invalid = EnvironmentConfig("ALL");
        invalid.GetNode("PLANET_CONFIG").GetNode("BIOME_CONFIG").AddNode("INPUT_RESOURCE");
        Equal(0, ChitakTechDestructivePart.ValidateConfig(invalid) ? 1 : 0, "resource nodes rejected");

        module = Create(EnvironmentConfig("Deserts", "maxTemperature", "1"));
        module.FixedUpdate();
        Equal(0, module.part.explosions, "unmatched biome safe");
        module = Create(EnvironmentConfig("ALL", "maxTemperature", "1"));
        HighLogic.LoadedSceneIsFlight = false;
        module.FixedUpdate();
        Equal(0, module.part.explosions, "editor safe");
        HighLogic.LoadedSceneIsFlight = true;
        module.FixedUpdate();
        Equal(1, module.part.explosions, "flight enforces rules");
    }
}
