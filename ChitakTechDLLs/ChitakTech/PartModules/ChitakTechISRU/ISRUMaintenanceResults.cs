using System.Linq;

namespace ChitakTech
{
    public partial class ChitakTechISRU
    {
        private bool HasMaintenanceResult(string type) => ActiveMaintenance.Any(d =>
            MaintenanceFor(d).Active(d)?.results.Any(r => r.type == type) == true);

        private bool MaintenanceBlocksRunning => HasMaintenanceResult("preventRunning")
            || HasMaintenanceResult("unchargeConstant");
        private bool MaintenanceBlocksCharging => HasMaintenanceResult("preventCharging")
            || HasMaintenanceResult("unchargeConstant");

        private void EnforceMaintenanceResults()
        {
            if (MaintenanceBlocksRunning && isRunning)
                TriggerPowerfail(GetPowerfailContextPart(), ChitakTechISRURecipe.PowerfailResult.Void);
            if (HasMaintenanceResult("unchargeConstant")) chargePercent = 0;
            if (MaintenanceBlocksCharging && state == CTShared.ChargablePartState.Charging)
                state = CTShared.ChargablePartState.On;
        }

        // True means the context was destroyed; stop accessing its converter immediately.
        private bool ApplyMaintenanceResults(MaintenanceStage stage)
        {
            foreach (var result in stage.results)
            {
                switch (result.type)
                {
                    case "preventRunning":
                        if (isRunning) TriggerPowerfail(GetPowerfailContextPart(), ChitakTechISRURecipe.PowerfailResult.Void);
                        break;
                    case "preventCharging":
                        if (state == CTShared.ChargablePartState.Charging) state = CTShared.ChargablePartState.On;
                        break;
                    case "powerfailVOID":
                        TriggerPowerfail(GetPowerfailContextPart(), ChitakTechISRURecipe.PowerfailResult.Void); break;
                    case "powerfailSTOP":
                        TriggerPowerfail(GetPowerfailContextPart(), ChitakTechISRURecipe.PowerfailResult.Stop); break;
                    case "powerfailPAUSE":
                        TriggerPowerfail(GetPowerfailContextPart(), ChitakTechISRURecipe.PowerfailResult.Pause);
                        // Preserve progress/consumed inputs, but require Start to resume.
                        isRunning = false; break;
                    case "uncharge": case "unchargeConstant":
                        TriggerPowerfail(GetPowerfailContextPart(), ChitakTechISRURecipe.PowerfailResult.Void);
                        chargePercent = 0; break;
                    case "explode":
                        TriggerPowerfail(GetPowerfailContextPart(), ChitakTechISRURecipe.PowerfailResult.Explode,
                            result.radius, result.temperature);
                        return true;
                    case "massiveVesselExplode":
                        CTShared.Instance?.MassiveVesselExplosion(GetProcessingVessel(), result.range, result.lifetime);
                        return true;
                }
            }
            return false;
        }
    }
}
