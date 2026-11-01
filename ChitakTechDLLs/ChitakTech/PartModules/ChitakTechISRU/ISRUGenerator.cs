using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace ChitakTech
{
    public partial class ChitakTechISRU
    {
        private List<ChitakTechMaterialInstance> generatorMaterials;
        private List<PreparedResourceOutput> generatorResources;
        private float generatorLastNotice = float.NegativeInfinity;
        private double generatorResourceMultiplier, generatorMaterialMultiplier;
        private ConfigNode pendingGeneratorBatch;
        private string generatorPreparationError;

        private bool GeneratorPreparationFailed(string error)
        {
            if (generatorPreparationError != error)
                CTShared.LogError(ConverterName + ": " + error, "ChitakTechGenerator/prepare");
            generatorPreparationError = error;
            return false;
        }

        protected void ResetGeneratorBatch()
        {
            batchProgress = resolvedRecipeTime = 0;
            generatorMaterials = null;
            generatorResources = null;
            runOneCycle = false;
            pendingGeneratorBatch = null;
            ClearPassiveConsumption(); ResetPassiveTimers();
        }

        protected bool PrepareGeneratorBatch(ChitakTechISRUBiomeConfig biome, out double time)
        {
            time = 0;
            generatorMaterials = null;
            if (!RefreshEnvironmentRecipe()) return false;
            var inputs = new Dictionary<string, ChitakTechMaterialInstance>();
            if (!TryResolveInputMaterialOutputs(_activeRecipe._outputMaterials, inputs,
                    out var outputs, out var values, biome.inputMultiplier)) return false;
            string expression = _activeRecipe._recipeTimeExpression;
            if (!TryResolveMaterialReferences(expression, inputs, values, true, "interval", out expression)) return false;
            if (!CTMathExpr.TryEvaluateNumericExpression(expression, out time, out string error)
                || !CTShared.IsFinite(time) || time <= 0)
                return GeneratorPreparationFailed("interval must resolve to a finite positive number. " + error);
            var materials = new List<ChitakTechMaterialInstance>();
            foreach (var output in outputs)
            {
                var definition = CTShared.Instance.materialList.FirstOrDefault(m => m.name == output.name);
                if (definition == null) return false;
                ChitakTechISRURecipe.ResourceOutputMaterial resolved;
                if (string.IsNullOrEmpty(output.id) || !values.TryGetValue(output.id, out resolved))
                {
                    if (!TryCreateOutputMaterialReferenceValue(output, definition, out resolved)) return false;
                    resolved.amount *= biome.inputMultiplier;
                }
                // As with ISRU, only whole material instances can leave the converter.
                // A generator has no output buffer; any sub-unit remainder is discarded.
                if (!CTShared.IsFinite(resolved.amount) || resolved.amount < 0 || resolved.amount > int.MaxValue)
                    return GeneratorPreparationFailed("Material amount is outside the supported range.");
                int count = (int)Math.Floor(resolved.amount);
                if (count <= 0) continue;
                if (!double.TryParse(resolved.outVolume, NumberStyles.Float, CultureInfo.InvariantCulture, out double volume)
                    || !CTShared.IsFinite(volume) || volume <= 0 || volume > float.MaxValue) return false;
                materials.Add(new ChitakTechMaterialInstance(definition, resolved.shape, resolved.size, (float)volume,
                    new Dictionary<string, string>(resolved.parameters)) { amount = count });
            }
            // Do not silently skip an overflowed amount in the shared output preparation.
            if (_activeRecipe._outputs.Any(o => !CTShared.IsFinite(o.amount * biome.outputMultiplier))
                || biome.outputs.Any(o => !CTShared.IsFinite(o.amount * _activeRecipe._biomeResourceScale * biome.outputMultiplier)))
                return GeneratorPreparationFailed("Resource amount is not finite after applying multipliers.");
            generatorResources = PrepareResourceOutputs(biome);
            generatorResourceMultiplier = biome.outputMultiplier;
            generatorMaterialMultiplier = biome.inputMultiplier;
            generatorMaterials = materials;
            generatorPreparationError = null;
            return true;
        }

        protected bool CompleteGeneratorBatch(ChitakTechISRUBiomeConfig biome)
        {
            if ((generatorMaterials == null || generatorResourceMultiplier != biome.outputMultiplier
                    || generatorMaterialMultiplier != biome.inputMultiplier)
                && !PrepareGeneratorBatch(biome, out _)) return DiscardGeneratorBatch();
            var transfers = new List<ChitakTechResourceNetwork.Transfer>();
            // Refresh environments BEFORE taking rollback snapshots so an environmental
            // void is not accidentally reversed by this batch's output rollback.
            var stores = vessel.parts.SelectMany(p => p.Modules.OfType<ChitakTechMaterialStorage>())
                .Where(s => s.TransfersEnabled).ToList();
            var before = stores.ToDictionary(s => s, s => s.contents.Select(m => new ChitakTechMaterialInstance(m)).ToList());
            bool committed = false;
            try
            {
                foreach (var output in generatorResources)
                {
                    var resourceStores = vessel.parts.SelectMany(p => p.Modules.OfType<ChitakTechStorage>())
                        .Where(s => s.SupportedResources.Contains(output.name)
                            && (s.storageType == "multiShared" || s.activeResource == output.name)).ToList();
                    double inserted = 0;
                    if (resourceStores.Count == 0)
                        inserted = Math.Abs(ChitakTechResourceNetwork.Request(part, output.name, -output.amount,
                            ResourceFlowMode.STAGE_PRIORITY_FLOW, transfers));
                    else foreach (var storage in resourceStores)
                    {
                        double moved = storage.RequestStoredResource(output.name, -(output.amount - inserted));
                        inserted += Math.Abs(moved);
                        if (moved != 0)
                        {
                            var captured = storage; string resource = output.name;
                            transfers.Add(new ChitakTechResourceNetwork.Transfer { amount = moved,
                                undo = signed => captured.UndoTransfer(resource, signed) });
                        }
                        if (inserted >= output.amount) break;
                    }
                    if (inserted + Math.Max(1e-10, output.amount * 1e-10) < output.amount) return DiscardGeneratorBatch();
                }
                foreach (var material in generatorMaterials)
                {
                    int left = material.amount;
                    foreach (var storage in stores)
                    {
                        int count = storage.TransferCapacity(material, left);
                        if (count <= 0) continue;
                        if (storage.AddMaterial(new ChitakTechMaterialInstance(material) { amount = count })) left -= count;
                        if (left == 0) break;
                    }
                    if (left != 0) return DiscardGeneratorBatch();
                }
                committed = true;
                return true;
            }
            catch (Exception ex)
            { CTShared.LogError(ex.ToString(), "ChitakTechGenerator/output"); return DiscardGeneratorBatch(); }
            finally
            {
                if (!committed)
                {
                    ChitakTechResourceNetwork.Rollback(transfers);
                    foreach (var entry in before)
                    { entry.Key.contents.Clear(); entry.Key.contents.AddRange(entry.Value); }
                }
                generatorMaterials = null;
                generatorResources = null;
            }
        }

        private bool DiscardGeneratorBatch()
        {
            if (Time.realtimeSinceStartup - generatorLastNotice >= 3)
            {
                ScreenMessages.PostScreenMessage(ConverterName + ": output could not be stored; entire batch discarded.",
                    4f, ScreenMessageStyle.UPPER_CENTER);
                generatorLastNotice = Time.realtimeSinceStartup;
            }
            return true; // Completed, deliberately voided: the next interval still starts.
        }

        protected void LoadGeneratorBatch(ConfigNode node)
        {
            pendingGeneratorBatch = null;
            if (node?.GetNode("GENERATOR_BATCH") == null) return;
            pendingGeneratorBatch = new ConfigNode("GENERATOR_BATCH");
            node.GetNode("GENERATOR_BATCH").CopyTo(pendingGeneratorBatch);
        }
        protected void RestoreGeneratorBatch()
        {
            if (resolvedRecipeTime <= 0 || pendingGeneratorBatch == null) return;
            try
            {
                var materials = new List<ChitakTechMaterialInstance>();
                foreach (ConfigNode node in pendingGeneratorBatch.GetNodes("MATERIAL"))
                {
                    if (!ChitakTechMaterialInstance.TryFromConfigNode(node, out var material))
                        throw new FormatException("Invalid saved generator material.");
                    materials.Add(material);
                }
                var resources = new List<PreparedResourceOutput>();
                foreach (ConfigNode node in pendingGeneratorBatch.GetNodes("RESOURCE"))
                {
                    string name = node.GetValue("name");
                    double amount = double.Parse(node.GetValue("amount"), CultureInfo.InvariantCulture);
                    if (PartResourceLibrary.Instance.GetDefinition(name) == null || !CTShared.IsFinite(amount) || amount < 0)
                        throw new FormatException("Invalid saved generator resource.");
                    resources.Add(new PreparedResourceOutput { name = name, amount = amount });
                }
                generatorResourceMultiplier = double.Parse(pendingGeneratorBatch.GetValue("resourceMultiplier"), CultureInfo.InvariantCulture);
                generatorMaterialMultiplier = double.Parse(pendingGeneratorBatch.GetValue("materialMultiplier"), CultureInfo.InvariantCulture);
                if (!CTShared.IsFinite(generatorResourceMultiplier) || generatorResourceMultiplier < 0
                    || !CTShared.IsFinite(generatorMaterialMultiplier) || generatorMaterialMultiplier < 0)
                    throw new FormatException("Invalid saved generator multiplier.");
                generatorMaterials = materials; generatorResources = resources;
            }
            catch (Exception ex)
            { ResetGeneratorBatch(); isRunning = false; CTShared.LogError(ex.Message, "ChitakTechGenerator/load"); }
            pendingGeneratorBatch = null;
        }
        protected void SaveGeneratorBatch(ConfigNode node)
        {
            while (node.HasNode("GENERATOR_BATCH")) node.RemoveNode("GENERATOR_BATCH");
            if (resolvedRecipeTime <= 0) return;
            if (pendingGeneratorBatch != null)
            {
                pendingGeneratorBatch.CopyTo(node.AddNode("GENERATOR_BATCH"));
                return;
            }
            if (generatorMaterials == null) return;
            var batch = node.AddNode("GENERATOR_BATCH");
            batch.AddValue("resourceMultiplier", generatorResourceMultiplier.ToString("R", CultureInfo.InvariantCulture));
            batch.AddValue("materialMultiplier", generatorMaterialMultiplier.ToString("R", CultureInfo.InvariantCulture));
            foreach (var material in generatorMaterials) batch.AddNode(material.ToConfigNode("MATERIAL"));
            foreach (var resource in generatorResources)
            {
                var output = batch.AddNode("RESOURCE"); output.AddValue("name", resource.name);
                output.AddValue("amount", resource.amount.ToString("R", CultureInfo.InvariantCulture));
            }
        }
    }
}
