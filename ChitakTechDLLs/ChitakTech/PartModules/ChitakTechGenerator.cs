using System;
using System.Linq;

namespace ChitakTech
{
    // Uses the same batch, settings, maintenance and save-state engine as ISRU.
    public class ChitakTechGenerator : ChitakTechISRU
    {
        [KSPField(guiActive = true, guiActiveEditor = true, guiName = "Name")]
        public string displayName = "Unknown Generator";
        [KSPField] public string interval = "1";
        [KSPField] public bool alwaysOn = false;
        [KSPField] public bool hideVoidMessage = false;
        private bool warnedName;
        private ChitakTechISRURecipe.PowerfailResult powerfail = ChitakTechISRURecipe.PowerfailResult.Void;
        private bool environmentFailed;

        protected override bool HideGeneratorVoidMessage => hideVoidMessage;

        protected override ConfigNode GetConverterConfig()
        {
            var modules = part?.partInfo?.partConfig?.GetNodes("MODULE")
                .Where(n => n.GetValue("name") == "ChitakTechGenerator").ToArray();
            int index = part == null ? -1 : part.Modules.OfType<ChitakTechGenerator>().ToList().IndexOf(this);
            if (modules == null || index < 0 || index >= modules.Length) return null;
            var source = modules[index];
            displayName = source.GetValue("displayName")?.Trim();
            if (string.IsNullOrEmpty(displayName))
            {
                displayName = "Unknown Generator";
                if (!warnedName) CTShared.LogWarning("Missing/blank displayName; using Unknown Generator.", "ChitakTechGenerator/config");
                warnedName = true;
            }
            interval = source.GetValue("interval") ?? "1";
            alwaysOn = bool.TryParse(source.GetValue("alwaysOn"), out bool autoRun) && autoRun;
            hideVoidMessage = bool.TryParse(source.GetValue("hideVoidMessage"), out bool hideMessage) && hideMessage;
            try
            {
                powerfail = GeneratorConfig.ReadPowerfail(source);
                return GeneratorConfig.Build(source, displayName, interval);
            }
            catch (FormatException ex)
            { CTShared.LogError(ex.Message, "ChitakTechGenerator/config"); return null; }
        }

        [KSPEvent(guiActive = true, guiActiveEditor = false, guiName = "Start generator",
            groupName = "chitaktechisru")]
        public override void StartConverter()
        {
            if (isRunning || _fatalConfigError) return;
            // A maintenance PAUSE retains its resolved batch; an explicit Stop does not.
            if (resolvedRecipeTime <= 0) ResetGeneratorBatch();
            state = CTShared.ChargablePartState.On;
            base.StartConverter();
            if (!isRunning) state = CTShared.ChargablePartState.Off;
        }
        [KSPEvent(guiActive = false, guiActiveEditor = false, guiName = "Stop generator",
            groupName = "chitaktechisru")]
        public override void StopConverter()
        {
            base.StopConverter();
            ResetGeneratorBatch();
            state = CTShared.ChargablePartState.Off;
        }
        public override void OnStart(StartState startState)
        {
            base.OnStart(startState);
            RestoreGeneratorBatch();
            state = isRunning ? CTShared.ChargablePartState.On : CTShared.ChargablePartState.Off;
            UpdateEventVisibility();
        }
        public override void OnLoad(ConfigNode node) { base.OnLoad(node); LoadGeneratorBatch(node); }
        public override void OnSave(ConfigNode node) { base.OnSave(node); SaveGeneratorBatch(node); }
        public override void FixedUpdate()
        {
            state = isRunning ? CTShared.ChargablePartState.On : CTShared.ChargablePartState.Off;
            base.FixedUpdate();
            // Retry after the shared engine has refreshed the environment and maintenance.
            // A biome change or power failure may have stopped us during this tick.
            if (environmentFailed && HighLogic.LoadedSceneIsFlight && part != null && vessel != null)
            {
                var biome = GetEffectiveBiomeConfig();
                environmentFailed = biome != null && ChitakTechEnvironmentConditions.Destructive(biome, _runtimeData);
            }
            if (alwaysOn && !environmentFailed && HighLogic.LoadedSceneIsFlight && enabled && isEnabled
                && part != null && vessel != null)
                TryAutoStartGenerator();
        }
        protected override void UpdateEventVisibility()
        {
            base.UpdateEventVisibility();
            if (_fatalConfigError) return;
            foreach (string name in new[] { "RunOneCycle", "SwitchRecipe", "EnableCharging", "DisableCharging", "TurnOnConverter", "TurnOffConverter" })
            { Events[name].guiActive = Events[name].guiActiveUnfocused = Events[name].active = false; }
            foreach (string name in new[] { "chargeDisplay", "stateDisplay" })
                Fields[name].guiActive = Fields[name].guiActiveUnfocused = false;
            Events["StartConverter"].guiName = "Start generator";
            Events["StartConverter"].active = Events["StartConverter"].guiActive = !isRunning;
            Events["StopConverter"].guiName = "Stop generator";
            if (alwaysOn)
                foreach (string name in new[] { "StartConverter", "StopConverter" })
                    Events[name].active = Events[name].guiActive = Events[name].guiActiveEditor
                        = Events[name].guiActiveUnfocused = false;
            Events["ChangeRecipeParameters"].guiActiveEditor = !isRunning
                && Events["ChangeRecipeParameters"].active;
            Actions["StartConverterAction"].guiName = "Start generator";
            Actions["StopConverterAction"].guiName = "Stop generator";
            Actions["StartConverterAction"].active = Actions["StopConverterAction"].active = !alwaysOn;
        }
        protected override bool TryRunBatch(ChitakTechISRUBiomeConfig biome) => CompleteGeneratorBatch(biome);
        protected override void OnDestructiveEnvironment()
        {
            if (powerfail == ChitakTechISRURecipe.PowerfailResult.Pause && environmentFailed)
            {
                statusDisplay = "Paused";
                return;
            }
            environmentFailed = true;
            CTShared.LogWarning($"{ConverterName} exceeded a destructive limit in {_runtimeData.planet}/{_runtimeData.biome}. "
                + $"Situation={_runtimeData.sitCon}, altitude={_runtimeData.alt} m, G={_runtimeData.g}, "
                + $"temperature={_runtimeData.temperature} K, pressure={_runtimeData.pressure} kPa.",
                "ChitakTechGenerator/environment");
            TriggerPowerfail(part, powerfail);
        }
        protected override bool TryResolveRecipeTime(ChitakTechISRUBiomeConfig biome, out double time)
            => PrepareGeneratorBatch(biome, out time);
    }

}
