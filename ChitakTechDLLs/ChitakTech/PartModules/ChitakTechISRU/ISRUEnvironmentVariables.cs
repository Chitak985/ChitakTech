using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ChitakTech
{
    public partial class ChitakTechISRU
    {
        private ConfigNode environmentTemplate;
        private string environmentKey;
        private bool environmentValid;
        private static Dictionary<string, string> EnvironmentValues(Vessel vessel)
        {
            var runtime = new ChitakTechRuntimeData(vessel);
            double reference = PhysicsGlobals.SolarLuminosityAtHome;
            double solar = vessel != null && vessel.directSunlight && reference > 0
                ? vessel.solarFlux / reference : 0;
            if (!CTShared.IsFinite(solar) || solar < 0) solar = 0;
            return new Dictionary<string, string> { { "currentBody", runtime.planet },
                { "currentBiome", runtime.biome }, { "solarMultiplier", solar.ToString("R", CultureInfo.InvariantCulture) } };
        }

        protected virtual ConfigNode GetConverterConfig()
            => CTShared.FindModuleConfigNode(part, ConverterName, "ChitakTechISRU");

        protected bool RefreshEnvironmentRecipe()
        {
            if (_activeRecipe == null || !_activeRecipe.HasEnvironmentVariables) return true;
            var environment = EnvironmentValues(GetProcessingVessel());
            var settings = EnsureRecipeSettingValues(_activeRecipe);
            string key = string.Join("|", environment.OrderBy(p => p.Key).Select(p => p.Value))
                + string.Join("|", settings.OrderBy(p => p.Key).Select(p => p.Key + "=" + p.Value.ToString("R", CultureInfo.InvariantCulture)));
            if (ReferenceEquals(environmentTemplate, _activeRecipe.mainNode) && key == environmentKey) return environmentValid;
            environmentTemplate = _activeRecipe.mainNode;
            environmentKey = key;
            environmentValid = false;
            var resolved = new ChitakTechISRURecipe(_activeRecipe.mainNode, ConverterName,
                settings, environment)
                .ScaledCopy(_activeRecipe._settingRecipeScale);
            if (!resolved.IsValid || !resolved.ValidateReferences(CTShared.Instance?.materialList, ConverterName)) return false;
            // At a batch boundary only: preserve clocks, current settings and maintenance history.
            _activeRecipe = resolved;
            environmentTemplate = resolved.mainNode;
            environmentValid = true;
            return true;
        }
    }
}
