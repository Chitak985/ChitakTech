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
        private bool warnedName;

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
            try { return GeneratorConfig.Build(source, displayName, interval); }
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
            Events["ChangeRecipeParameters"].guiActiveEditor = !isRunning
                && Events["ChangeRecipeParameters"].active;
            Actions["StartConverterAction"].guiName = "Start generator";
            Actions["StopConverterAction"].guiName = "Stop generator";
        }
        protected override bool TryRunBatch(ChitakTechISRUBiomeConfig biome) => CompleteGeneratorBatch(biome);
        protected override bool TryResolveRecipeTime(ChitakTechISRUBiomeConfig biome, out double time)
            => PrepareGeneratorBatch(biome, out time);
    }

}
