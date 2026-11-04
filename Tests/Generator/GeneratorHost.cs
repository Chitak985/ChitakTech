using System;
using System.Collections.Generic;

// Minimal KSP lifecycle/UI host; the generator subclass and its batch code are real.
public class KSPField : Attribute
{
    public bool guiActive, guiActiveEditor;
    public string guiName;
}
public class KSPEvent : Attribute
{
    public bool guiActive, guiActiveEditor;
    public string guiName, groupName;
}
public enum StartState { None }
public static class HighLogic { public static bool LoadedSceneIsFlight = true; }
public class AvailablePart { public ConfigNode partConfig = new ConfigNode(); }
public class Control
{
    public bool active, guiActive, guiActiveEditor, guiActiveUnfocused;
    public string guiName;
}
public class Controls : Dictionary<string, Control>
{
    public new Control this[string name]
    {
        get { if (!TryGetValue(name, out var value)) Add(name, value = new Control()); return value; }
    }
}
namespace ChitakTech
{
    internal static class GeneratorConfig
    {
        internal static ConfigNode Build(ConfigNode source, string name, string interval) => source;
    }
    public partial class ChitakTechISRU
    {
        public bool enabled = true, isEnabled = true, _fatalConfigError, needsMaintenance;
        public bool maintenanceBlocksRunning, atDeposit = true, environmentAvailable = true, stopDuringNextTick;
        protected object _runtimeData = new object();
        public CTShared.ChargablePartState state;
        public Controls Events = new Controls(), Actions = new Controls(), Fields = new Controls();
        protected bool CanOperate => !maintenanceBlocksRunning && state != CTShared.ChargablePartState.Off;
        protected bool IsAtRequiredDeposit(ChitakTechISRUBiomeConfig biome) => atDeposit;
        private ChitakTechISRUBiomeConfig GetEffectiveBiomeConfig() => new ChitakTechISRUBiomeConfig();
        protected virtual ConfigNode GetConverterConfig() => null;
        public virtual void StartConverter()
        {
            if (needsMaintenance || !CanOperate || !atDeposit || !environmentAvailable)
                throw new Exception("Automatic start reached the manual error/message path.");
            isRunning = true; UpdateEventVisibility();
        }
        public virtual void StopConverter() { isRunning = false; UpdateEventVisibility(); }
        public virtual void OnStart(StartState startState) { }
        public virtual void OnLoad(ConfigNode node) { }
        public virtual void OnSave(ConfigNode node) { }
        public virtual void FixedUpdate()
        {
            if (stopDuringNextTick) { isRunning = false; stopDuringNextTick = false; }
            UpdateEventVisibility();
        }
        protected virtual void UpdateEventVisibility()
        {
            Events["StartConverter"].active = Events["StartConverter"].guiActive
                = Events["StartConverter"].guiActiveUnfocused = !isRunning;
            Events["StopConverter"].active = Events["StopConverter"].guiActive
                = Events["StopConverter"].guiActiveUnfocused = isRunning;
        }
        protected virtual bool TryRunBatch(ChitakTechISRUBiomeConfig biome) => false;
        protected virtual bool TryResolveRecipeTime(ChitakTechISRUBiomeConfig biome, out double time)
        { time = 1; return true; }
    }
}
