using System;
using System.Collections.Generic;
using System.Linq;
using ChitakTech;

internal static class Program
{
    private static int assertions;
    private static void Check(bool condition, string why)
    { assertions++; if (!condition) throw new Exception(why); }
    private static MaintenanceDefinition Definition(params MaintenanceStage[] stages)
    {
        var definition = new MaintenanceDefinition { name = "Screws", statusOptimal = "Like New" };
        definition.stages.AddRange(stages.OrderByDescending(s => s.order));
        return definition;
    }
    private static MaintenanceStage Stage(int order, double time, bool parallel = false, bool runtime = false)
        => new MaintenanceStage { order = order, time = time, ignoreOrder = parallel, tickRuntime = runtime, tickOff = !runtime, status = "Stage " + order };
    private static ConfigNode Config(out ConfigNode stage)
    {
        var root = new ConfigNode();
        var maintenance = root.AddNode("MAINTENANCE");
        maintenance.AddValue("name", "Screws"); maintenance.AddValue("statusOptimal", "Like New");
        stage = maintenance.AddNode("STAGE");
        stage.AddValue("order", 1); stage.AddValue("status", "Loose"); stage.AddValue("time", 10);
        stage.AddValue("tickOff", true); stage.AddValue("tickReady", true); stage.AddValue("tickCharging", true); stage.AddValue("tickWorking", true);
        return root;
    }
    private static void Timers()
    {
        TickFlags();
        ResultTests();
        var definition = Definition(Stage(1, 10), Stage(3, 10), Stage(2, 10));
        var state = new MaintenanceState();
        state.Advance(definition, 9, 0, () => 0);
        Check(state.activeOrder == null, "initial state lasts the full first interval");
        state.Advance(definition, 12, 0, () => 0);
        Check(state.activeOrder == 2 && Math.Abs(state.elapsed[1] - 1) < 1e-8, "large ticks preserve sequential elapsed time");
        state.Repair(definition); Check(state.activeOrder == 3, "repair returns to preceding stage");
        state.Repair(definition); Check(state.activeOrder == null, "repair first stage returns to optimal");
        definition = Definition(Stage(3, 100), Stage(2, 100), Stage(1, 5, true));
        state = new MaintenanceState(); state.Advance(definition, 5, 0, () => 0);
        Check(state.activeOrder == 1, "parallel stage jumps ahead");
        state.Repair(definition); Check(state.activeOrder == 2, "parallel repair activates its predecessor even when skipped");
        state.Advance(definition, 4, 0, () => 0); Check(state.activeOrder == 2, "repaired stage timer restarts");
        state.Advance(definition, 1, 0, () => 0); Check(state.activeOrder == 1, "parallel issue can recur");
        definition = Definition(Stage(1, 10, false, true));
        state = new MaintenanceState(); state.Advance(definition, 100, 0, () => 0);
        Check(state.activeOrder == null, "runtime timers do not age while stopped");
        state.Advance(definition, 100, 9, () => 0); Check(state.activeOrder == null, "runtime uses actual work time");
        state.Advance(definition, 1, 1, () => 0); Check(state.activeOrder == 1, "runtime threshold triggers");
        definition.stages[0].canFix = false; state.Repair(definition);
        Check(state.activeOrder == 1, "unfixable stages cannot be repaired");
        definition = Definition(Stage(1, 10)); definition.stages[0].chance = .25;
        state = new MaintenanceState(); state.Advance(definition, 10, 0, () => .75);
        Check(state.activeOrder == null && state.elapsed[1] == 0, "failed chance roll resets timer");
        state.Advance(definition, 10, 0, () => .1); Check(state.activeOrder == 1, "next chance roll can trigger");
        definition.stages[0].chance = 0; state = new MaintenanceState();
        state.Advance(definition, 1000, 0, () => throw new Exception("zero chance should not roll"));
        Check(state.activeOrder == null, "chance zero never activates");
    }

    private static void TickFlags()
    {
        // Exhaust every combination of flags and status bits, including charging + working.
        for (int flags = 0; flags < 32; flags++)
            for (int status = 0; status < 16; status++)
            {
                var stage = new MaintenanceStage { tickRuntime = (flags & 16) != 0,
                    tickOff = (flags & 1) != 0, tickCharging = (flags & 2) != 0,
                    tickReady = (flags & 4) != 0, tickWorking = (flags & 8) != 0 };
                double expected = (flags & status & 15) != 0 ? 10 : stage.tickRuntime ? 3 : 0;
                Check(stage.ClockSeconds(10, 3, (MaintenanceClockStatus)status) == expected,
                    "clock flags use OR, never sum elapsed and runtime");
            }
        var root = Config(out var node);
        foreach (string key in new[] { "tickRuntime", "tickOff", "tickCharging", "tickReady", "tickWorking" }) node.SetValue(key, false);
        int errors = CTShared.Errors.Count;
        var recipe = new ChitakTechISRURecipe();
        Check(recipe.ParseMaintenance(root), "all false flags do not invalidate recipe");
        Check(CTShared.Errors.Count == errors + 1, "all false flags log stage error");
        var saved = new MaintenanceState();
        saved.Advance(recipe.maintenance[0], 10000, 5000, () => 0, status: (MaintenanceClockStatus)15);
        Check(saved.activeOrder == null && saved.elapsed.Count == 0, "all false stage never ticks");
        root = new ConfigNode(); var maintenance = root.AddNode("MAINTENANCE");
        maintenance.AddValue("name", "Defaults"); maintenance.AddValue("statusOptimal", "Good");
        node = maintenance.AddNode("STAGE"); node.AddValue("order", 1); node.AddValue("time", 1); node.AddValue("status", "Bad");
        recipe = new ChitakTechISRURecipe();
        Check(recipe.ParseMaintenance(root), "omitted flags keep recipe valid");
        var defaults = recipe.maintenance[0].stages[0];
        Check(!defaults.tickRuntime && !defaults.tickOff && !defaults.tickCharging && !defaults.tickReady && !defaults.tickWorking,
            "all five flags default false");
        node.AddValue("tickWorking", "notABoolean");
        Check(!new ChitakTechISRURecipe().ParseMaintenance(root), "malformed boolean still invalidates configuration");
    }

    private static void ResultTests()
    {
        var definition = Definition(Stage(30, 1), Stage(20, 1), Stage(10, 1));
        definition.stages[2].returnOrder = 30;
        var saved = new MaintenanceState();
        var triggers = new List<int>();
        saved.Advance(definition, 3, 0, () => 0, s => triggers.Add(s.order));
        Check(triggers.SequenceEqual(new[] { 30, 20, 10 }), "all crossed stages trigger in order");
        saved.Repair(definition);
        Check(saved.activeOrder == 30, "explicit returnOrder overrides predecessor");
        var root = Config(out var stage);
        stage.AddValue("returnOrder", 999);
        Check(!new ChitakTechISRURecipe().ParseMaintenance(root), "undefined returnOrder rejected");
        root = Config(out stage);
        stage.AddNode("RESULT").AddValue("type", "explode");
        Check(!new ChitakTechISRURecipe().ParseMaintenance(root), "explosion needs radius and temperature");
        root = Config(out stage);
        stage.AddNode("RESULT").AddValue("type", "unknown");
        Check(!new ChitakTechISRURecipe().ParseMaintenance(root), "unknown result rejected");
        root = Config(out stage);
        var result = stage.AddNode("RESULT"); result.AddValue("type", "massiveVesselExplode"); result.AddValue("range", 100);
        Check(!new ChitakTechISRURecipe().ParseMaintenance(root), "massive explosion needs lifetime");
        result.AddValue("lifetime", 5);
        Check(new ChitakTechISRURecipe().ParseMaintenance(root), "massive explosion valid");
        root = Config(out stage);
        stage.AddNode("RESULT").AddValue("type", "unchargeConstant");
        var recipe = new ChitakTechISRURecipe();
        Check(recipe.ParseMaintenance(root), "constant uncharge valid");
        var converter = new ChitakTechISRU { _activeRecipe = recipe, chargePercent = 100, isRunning = true };
        converter.State(recipe.maintenance[0]);
        converter.Tick(10);
        Check(converter.chargePercent == 0 && !converter.isRunning, "uncharge triggers VOID and drains charge");
        Check(converter.BlockRunning && converter.BlockCharging, "constant uncharge blocks run and charge");
        converter.chargePercent = 25; converter.isRunning = true;
        converter.Tick(1);
        Check(converter.chargePercent == 0 && !converter.isRunning, "constant result reenforced");
        var snapshot = new ConfigNode(); converter.SaveState(snapshot);
        var restored = new ChitakTechISRU { _activeRecipe = recipe }; restored.LoadState(snapshot);
        Check(restored.BlockRunning && restored.BlockCharging, "loaded active stage retains restrictions");
        foreach (string type in new[] { "powerfailVOID", "powerfailSTOP", "powerfailPAUSE", "preventRunning", "preventCharging", "uncharge" })
        {
            root = Config(out stage); stage.AddNode("RESULT").AddValue("type", type);
            recipe = new ChitakTechISRURecipe();
            Check(recipe.ParseMaintenance(root), type + " parses");
            converter = new ChitakTechISRU { _activeRecipe = recipe, isRunning = true, chargePercent = 100,
                state = CTShared.ChargablePartState.Charging };
            converter.State(recipe.maintenance[0]); converter.Tick(10);
            Check(type == "preventCharging" ? converter.state != CTShared.ChargablePartState.Charging
                : !converter.isRunning, type + " takes effect");
            int fired = converter.results.Count;
            converter.Tick(1);
            Check(converter.results.Count == fired, type + " is not retriggered every tick");
        }
    }
    private static void ParsingAndMultipliers()
    {
        var root = Config(out var stage); var recipe = new ChitakTechISRURecipe();
        Check(recipe.ParseMaintenance(root), "minimal documented config parses");
        var parsed = recipe.maintenance.Single().stages.Single();
        Check(parsed.canFix && !parsed.autoFix && !parsed.tickRuntime && !parsed.ignoreOrder
            && parsed.chance == 1 && parsed.evaFixers.All(n => n == 0), "defaults match config contract");
        root = Config(out stage); stage.AddValue("chance", 1.1);
        Check(!new ChitakTechISRURecipe().ParseMaintenance(root), "invalid chance rejected");
        root = Config(out stage); stage.SetValue("time", 0);
        Check(!new ChitakTechISRURecipe().ParseMaintenance(root), "zero time cannot cause a timer loop");
        root = Config(out stage); stage.AddValue("canFix", false); stage.AddValue("fixTime", "invalid");
        stage.AddNode("RESOURCE_TO_FIX");
        Check(new ChitakTechISRURecipe().ParseMaintenance(root), "canFix false ignores all fixing fields");
        root = Config(out stage); stage.AddValue("fixersEngineers", 1.5);
        Check(!new ChitakTechISRURecipe().ParseMaintenance(root), "fractional fixer count rejected");
        root = Config(out stage); stage.AddValue("speedMul", 0);
        Check(!new ChitakTechISRURecipe().ParseMaintenance(root), "zero duration multiplier rejected");
        var biomeNode = new ConfigNode(); var a = Stage(1, 10); var b = Stage(1, 10);
        foreach (string key in ChitakTechISRUBiomeConfig.MultiplierFields.Keys)
        { biomeNode.AddValue(key, 2); a.multipliers[key] = 3; b.multipliers[key] = 4; }
        var biome = new ChitakTechISRUBiomeConfig(biomeNode);
        var effective = biome.WithMaintenance(new[] { a, b });
        Check(ChitakTechISRUBiomeConfig.MultiplierFields.Count == 15, "all fifteen biome multipliers included");
        foreach (var mapping in ChitakTechISRUBiomeConfig.MultiplierFields)
        {
            var field = typeof(ChitakTechISRUBiomeConfig).GetField(mapping.Value);
            Check((double)field.GetValue(effective) == 24, mapping.Key + " combines biome and maintenance types");
            Check((double)field.GetValue(biome) == 2, mapping.Key + " leaves shared config unchanged");
        }
    }
    private static void RepairsAndPersistence()
    {
        var stage = Stage(1, 10); stage.fixTime = 3;
        stage.resources["Tape"] = 2; stage.tools["Wrench"] = 1; stage.evaFixers[0] = 1;
        stage.crewFixers[1] = 1; stage.fixersCrewSamePart = true;
        var definition = Definition(stage); var recipe = new ChitakTechISRURecipe(); recipe.maintenance.Add(definition);
        var converter = new ChitakTechISRU { _activeRecipe = recipe };
        var state = converter.State(definition); state.Activate(stage);
        converter.FixMaintenance(); converter.Tick(1);
        Check(state.repairProgress == 0 && state.repairStatus == "missing fixers", "manual repair waits for both fixer groups");
        var eva = new Vessel { isEVA = true }; var evaPart = new Part(); eva.parts.Add(evaPart);
        evaPart.protoModuleCrew.Add(new ProtoCrewMember { trait = "Engineer" }); FlightGlobals.Vessels.Add(eva);
        var other = new Part(); other.protoModuleCrew.Add(new ProtoCrewMember { trait = "Pilot" }); converter.vessel.parts.Add(other);
        converter.Tick(1); Check(state.repairProgress == 0, "same-part restriction excludes crew on another part");
        converter.part.protoModuleCrew.Add(new ProtoCrewMember { trait = "Pilot" });
        converter.Tick(1); Check(state.repairStatus == "missing Tape", "missing supplies pause repairs");
        var tape = new ChitakTechResourceNetwork.Endpoint { resourceName = "Tape", amount = 5 };
        var wrench = new ChitakTechResourceNetwork.Endpoint { resourceName = "Wrench", amount = 1 };
        converter.part.resources.Add(tape); converter.part.resources.Add(wrench);
        converter.Tick(1); Check(state.repairProgress == 1 && tape.amount == 5, "supplies consumed only at completion");
        var saved = new ConfigNode(); converter.ToggleAutoMaintenanceFixing(); converter.SaveState(saved);
        var restored = new ChitakTechISRU { _activeRecipe = recipe, part = converter.part, vessel = converter.vessel };
        restored.LoadState(saved); var restoredState = restored.State(definition);
        Check(restored.autoMaintenanceFixing && restoredState.repairProgress == 1 && restoredState.manualRepair
            && restoredState.activeOrder == 1, "save/load retains repairs, active order and auto toggle");
        wrench.amount = 0; restored.Tick(10);
        Check(restoredState.repairProgress == 1 && tape.amount == 5, "losing a tool pauses without consuming supplies");
        wrench.amount = 1; restored.Tick(2);
        Check(restoredState.activeOrder == null && tape.amount == 3 && wrench.amount == 1, "completed repair consumes supplies and retains tools");
        restoredState.Activate(stage); restored.Tick(5);
        Check(restoredState.activeOrder == 1 && tape.amount == 3, "auto toggle does not repair a stage with autoFix false");
        stage.autoFix = true; restored.Tick(3);
        Check(restoredState.activeOrder == null && tape.amount == 1, "autoFix and enabled toggle repair together");
        restoredState.Activate(stage); restored.ToggleAutoMaintenanceFixing(); tape.amount = 10; restored.Tick(3);
        Check(restoredState.activeOrder == 1, "toggle off prevents automatic fixing");
        stage.tools["Tape"] = 9; restored.FixMaintenance(); restored.Tick(3);
        Check(restoredState.activeOrder == 1 && tape.amount == 10, "supply also required as tool must remain after consumption");
        stage.tools.Remove("Tape"); stage.canFix = false; restored.Tick(3);
        Check(restoredState.activeOrder == 1 && tape.amount == 10, "canFix false blocks manual and auto repairs");
        restored.LoadState(new ConfigNode());
        Check(!restored.autoMaintenanceFixing && restored.State(definition).activeOrder == null,
            "inventory prefab state cannot leak between different parts");
        var clock = restored.State(definition); stage.canFix = true; stage.autoFix = false;
        Planetarium.now += 100;
        clock.lastUniversalTime = Planetarium.now - 100; restored.Tick(1);
        Check(clock.activeOrder == 1, "wall timers catch up after unloading");
    }
    private static void HistoryConditions()
    {
        var root = Config(out var node);
        var type = root.GetNodes("MAINTENANCE").Single();
        var duplicate = type.AddNode("STAGE");
        duplicate.AddValue("order", 1); duplicate.AddValue("status", "Duplicate"); duplicate.AddValue("time", 5);
        Check(!new ChitakTechISRURecipe().ParseMaintenance(root), "duplicate stage orders invalidate maintenance");

        root = Config(out node); type = root.GetNodes("MAINTENANCE").Single();
        foreach (int order in new[] { 10, 42, 26, 50, 60, 70 })
        {
            var additional = type.AddNode("STAGE");
            additional.AddValue("order", order); additional.AddValue("status", "Other"); additional.AddValue("time", 5);
        }
        node.AddValue("requiredOrdersAND", "10, 42");
        node.AddValue("requiredOrdersAND", "26");
        node.AddValue("requiredOrdersOR", "10,50");
        node.AddValue("requiredOrdersOR", "60,70");
        node.AddValue("requiredOrderNOT", "50");
        node.AddValue("requiredOrderNOT", "1");
        var recipe = new ChitakTechISRURecipe();
        Check(recipe.ParseMaintenance(root), "repeated AND/OR/NOT conditions parse with whitespace");
        var stage = recipe.maintenance.Single().stages.Single(s => s.order == 1);
        var state = new MaintenanceState();
        state.occurredOrders.UnionWith(new[] { 10, 42, 60 });
        Check(!stage.RequirementsMet(state.occurredOrders), "every repeated AND list is required");
        state.occurredOrders.Add(26); state.occurredOrders.Remove(60);
        Check(!stage.RequirementsMet(state.occurredOrders), "matching one OR list does not satisfy the other");
        state.occurredOrders.Add(70);
        Check(stage.RequirementsMet(state.occurredOrders), "any match within each OR list suffices");
        state.occurredOrders.Add(50);
        Check(!stage.RequirementsMet(state.occurredOrders), "first NOT condition blocks the stage");
        state.occurredOrders.Remove(50); state.occurredOrders.Add(1);
        Check(!stage.RequirementsMet(state.occurredOrders), "repeated NOT condition blocks recurrence");
        foreach (var bad in new[] { "", "10,", "10,,42", "1.5", "2147483648", "unknown" })
        {
            root = Config(out node); node.AddValue("requiredOrdersAND", bad);
            Check(!new ChitakTechISRURecipe().ParseMaintenance(root), "malformed prerequisite is rejected: " + bad);
        }
        root = Config(out node); node.AddValue("requiredOrderNOT", "1,2");
        Check(!new ChitakTechISRURecipe().ParseMaintenance(root), "NOT accepts only a single order per value");
        root = Config(out node); node.AddValue("requiredOrdersOR", "999");
        Check(!new ChitakTechISRURecipe().ParseMaintenance(root), "undefined referenced order is rejected");

        var first = Stage(3, 5); var gated = Stage(2, 10, true); var final = Stage(1, 20, true);
        gated.requiredOrdersAND.Add(3);
        var definition = Definition(first, gated, final);
        state = new MaintenanceState();
        state.Advance(definition, 4, 0, () => 0);
        Check(!state.elapsed.ContainsKey(2), "gated parallel timer never starts early");
        state.Advance(definition, 4, 0, () => 0);
        Check(state.occurredOrders.SetEquals(new[] { 3 }) && state.elapsed[2] == 3,
            "gate opens at the activation boundary, without accruing time before it");
        state.Advance(definition, 7, 0, () => 0);
        Check(state.activeOrder == 2 && state.occurredOrders.SetEquals(new[] { 3, 2 }), "successful stages accumulate history");
        state.Repair(definition); state.Repair(definition);
        Check(state.activeOrder == null && state.occurredOrders.SetEquals(new[] { 3, 2 }), "repairs do not erase occurrence history");
        first.chance = 0; state = new MaintenanceState();
        state.Advance(definition, 10, 0, () => 0);
        Check(!state.occurredOrders.Contains(3) && !state.elapsed.ContainsKey(2), "failed chance rolls do not unlock dependents");

        first = Stage(3, 5); gated = Stage(2, 10, true); gated.requiredOrderNOT.Add(3);
        definition = Definition(first, gated); state = new MaintenanceState();
        state.Advance(definition, 50, 0, () => 0);
        Check(state.activeOrder == 3 && state.elapsed[2] == 5, "NOT stops an already-started parallel timer");
        state.Repair(definition); state.Advance(definition, 50, 0, () => 0);
        Check(!state.occurredOrders.Contains(2), "repairing the blocker never removes a historical NOT block");

        definition = Definition(Stage(3, 100), Stage(2, 100), Stage(1, 5, true));
        state = new MaintenanceState(); state.Advance(definition, 5, 0, () => 0);
        Check(state.occurredOrders.SetEquals(new[] { 1 }), "skipped stages are not invented in history");
        state.Repair(definition);
        Check(state.occurredOrders.SetEquals(new[] { 1, 2 }), "a predecessor activated by repair is recorded");

        recipe = new ChitakTechISRURecipe(); recipe.maintenance.Add(definition);
        var converter = new ChitakTechISRU { _activeRecipe = recipe };
        converter.State(definition).occurredOrders.UnionWith(new[] { 1, 2 });
        var save = new ConfigNode(); converter.SaveState(save);
        var restored = new ChitakTechISRU { _activeRecipe = recipe }; restored.LoadState(save);
        Check(restored.State(definition).occurredOrders.SetEquals(new[] { 1, 2 }), "full history persists even after returning to optimal");
        var otherDefinition = Definition(Stage(1, 10)); otherDefinition.name = "Other type";
        Check(restored.State(otherDefinition).occurredOrders.Count == 0, "history is isolated between maintenance types");
        restored.LoadState(new ConfigNode());
        Check(restored.State(definition).occurredOrders.Count == 0, "loading another inventory part clears history");
        save = new ConfigNode(); var oldState = save.AddNode("MAINTENANCE_STATE");
        oldState.AddValue("name", definition.name); oldState.AddValue("activeOrder", 1);
        restored.LoadState(save);
        Check(restored.State(definition).occurredOrders.SetEquals(new[] { 1 }), "legacy saves recover only the known active stage");
    }

    private static int Main()
    {
        try { Timers(); ParsingAndMultipliers(); RepairsAndPersistence(); HistoryConditions(); Console.WriteLine(assertions + " assertions passed."); return 0; }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
