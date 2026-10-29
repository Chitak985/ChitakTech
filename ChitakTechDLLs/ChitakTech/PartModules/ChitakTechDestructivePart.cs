using System.Collections.Generic;

namespace ChitakTech
{
    /// <summary>Biome-dependent survival limits for otherwise ordinary parts.</summary>
    public class ChitakTechDestructivePart : PartModule
    {
        private readonly StorageEnvironment environment = new StorageEnvironment();
        private bool ready;

        private static readonly HashSet<string> BiomeFields = new HashSet<string>
        {
            "name", "situationDestructive", "minAltitude", "maxAltitude",
            "minG", "maxG", "minTemperature", "maxTemperature", "minPressure", "maxPressure"
        };

        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            ready = false;
            if (HasConflict()) return;
            // Read the part definition, not the save's MODULE snapshot: rules are configuration.
            ConfigNode config = CTShared.FindModuleConfigNode(this, nameof(ChitakTechDestructivePart));
            if (config == null)
            {
                DisableWithError("Could not find MODULE in the part definition.");
                return;
            }
            if (!ValidateConfig(config) || !environment.Load(config, false))
            {
                DisableWithError("Invalid destructive configuration.");
                return;
            }
            ready = true;
        }

        internal static bool ValidateConfig(ConfigNode config)
        {
            foreach (ConfigNode planet in config.GetNodes())
            {
                if (planet.name != "PLANET_CONFIG") return Invalid("Only PLANET_CONFIG child nodes are supported.");
                foreach (ConfigNode.Value value in planet.values)
                    if (value.name != "name") return Invalid("Unsupported PLANET_CONFIG value: " + value.name);
                foreach (ConfigNode biome in planet.GetNodes())
                {
                    if (biome.name != "BIOME_CONFIG") return Invalid("Only BIOME_CONFIG is supported inside PLANET_CONFIG.");
                    if (biome.GetNodes().Length != 0) return Invalid("BIOME_CONFIG does not support child nodes.");
                    foreach (ConfigNode.Value value in biome.values)
                        if (!BiomeFields.Contains(value.name)) return Invalid("Unsupported BIOME_CONFIG value: " + value.name);
                }
            }
            return true;
        }

        private static bool Invalid(string message)
        {
            CTShared.LogError(message, "ChitakTechDestructivePart/Load");
            return false;
        }

        private bool HasConflict()
        {
            if (part == null) return true;
            foreach (PartModule module in part.Modules)
            {
                if (ReferenceEquals(module, this)) continue;
                if (module is ChitakTechISRU || module is ChitakTechStorage
                    || module is ChitakTechMaterialStorage || module is ChitakTechDestructivePart)
                {
                    DisableWithError("Conflicting PLANET_CONFIG-capable module " + module.GetType().Name
                        + " is present on the same part.");
                    return true;
                }
            }
            return false;
        }

        private void DisableWithError(string reason)
        {
            CTShared.LogError("Part \"" + (part?.name ?? "unknown") + "\": " + reason
                + " ChitakTechDestructivePart disabled.", "ChitakTechDestructivePart");
            ready = false;
            enabled = false;
            isEnabled = false;
        }

        public void FixedUpdate()
        {
            if (!ready || !HighLogic.LoadedSceneIsFlight || part?.vessel == null) return;
            // Also catch modules added dynamically after OnStart.
            if (HasConflict()) return;
            environment.Update(part);
        }
    }
}
