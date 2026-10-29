using System;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace ChitakTech
{
    public partial class ChitakTechMaterialStorage
    {
        internal double RemainingAmountCapacity
        {
            get
            {
                if (maxAmount < 0) return double.PositiveInfinity;
                double used = contents.Where(m => m != null).Sum(m => (double)m.amount);
                foreach (ConfigNode saved in _pendingSavedContents)
                {
                    if (!int.TryParse(saved.GetValue("amount"), out int amount) || amount < 0) return 0;
                    used += amount;
                }
                return Math.Max(0, maxAmount - used);
            }
        }

        internal int TransferCapacity(ChitakTechMaterialInstance material, int requested)
        {
            if (!TransfersEnabled || material == null || requested <= 0 || material.volume <= 0) return 0;
            double space = Math.Max(0, EffectiveVolume - ComputeCurrentVolume());
            int count = (int)Math.Min(Math.Min(requested, material.amount),
                Math.Min(RemainingAmountCapacity, Math.Floor(space / material.volume)));
            if (count <= 0) return 0;
            var piece = new ChitakTechMaterialInstance(material) { amount = count };
            return AcceptsMaterial(piece) ? count : 0;
        }

        internal int TransferMaterialTo(ChitakTechMaterialStorage target, ChitakTechMaterialInstance material, int requested)
        {
            if (target == null || target == this || !TransfersEnabled || part.vessel != target.part?.vessel
                || !contents.Contains(material)) return 0;
            int count = target.TransferCapacity(material, requested);
            if (count <= 0) return 0;
            var piece = new ChitakTechMaterialInstance(material) { amount = count };
            if (!target.AddMaterial(piece)) return 0;
            material.amount -= count;
            if (material.amount == 0) contents.Remove(material);
            else material.UpdateParams("ChitakTechMaterialStorage/Transfer");
            return count;
        }

        [KSPEvent(guiActive = true, guiName = "Transfer to another storage")]
        public void OpenStorageTransfer() => StorageTransferWindow.Open(this);
    }

    public partial class ChitakTechStorage
    {
        [KSPEvent(guiActive = true, guiName = "Transfer to another storage")]
        public void OpenStorageTransfer() => StorageTransferWindow.Open(this);
    }

    // Owned by the source part: scene changes and part destruction automatically end the job.
    public sealed class StorageTransferWindow : MonoBehaviour
    {
        private PartModule source, target;
        private string resource, amountText = "1", error;
        private ChitakTechMaterialInstance material;
        private double remaining, transferred;
        private bool visible, running;
        private Rect rect = new Rect(240, 160, 540, 360);
        private Vector2 scroll;

        internal static void Open(PartModule source)
        {
            var window = source.gameObject.GetComponents<StorageTransferWindow>().FirstOrDefault(w => w.source == source)
                ?? source.gameObject.AddComponent<StorageTransferWindow>();
            window.source = source;
            window.visible = true;
            if (!window.running) { window.target = null; window.error = null; }
        }

        private static void Notify(string message) => ScreenMessages.PostScreenMessage(message, 6f, ScreenMessageStyle.UPPER_CENTER);
        private static string Label(PartModule module)
            => (module.part.partInfo?.title ?? module.part.name) + " (part " + module.part.flightID
                + ", module " + module.part.Modules.IndexOf(module) + ")";

        public void OnGUI()
        {
            if (!visible || !HighLogic.LoadedSceneIsFlight || source == null) return;
            rect = GUILayout.Window(GetInstanceID(), rect, Draw, "Transfer to another storage");
        }

        private void Draw(int id)
        {
            if (GUILayout.Button("Close")) visible = false;
            if (running)
            {
                GUILayout.Label("Transferred: " + transferred.ToString("G6") + "; remaining: " + remaining.ToString("G6"));
                if (GUILayout.Button("Cancel transfer")) Finish("cancelled");
            }
            else if (target == null)
            {
                scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(260));
                bool any = false;
                if (source.part?.vessel != null)
                    foreach (Part part in source.part.vessel.parts)
                        foreach (PartModule candidate in part.Modules)
                        {
                            if (candidate == source) continue;
                            if (source is ChitakTechStorage from && candidate is ChitakTechStorage to && from.TransfersEnabled)
                                foreach (var entry in from.GetStoredResources())
                                    if (entry.Value > 0 && to.GetAvailableSpace(entry.Key, 1, true) > 0)
                                    {
                                        any = true;
                                        if (GUILayout.Button(Label(to) + " — " + entry.Key))
                                        { target = to; resource = entry.Key; amountText = entry.Value.ToString("R", CultureInfo.InvariantCulture); }
                                    }
                            if (source is ChitakTechMaterialStorage mats && candidate is ChitakTechMaterialStorage dest && mats.TransfersEnabled)
                                foreach (var item in mats.contents.ToArray())
                                    if (dest.TransferCapacity(item, item.amount) > 0)
                                    {
                                        any = true;
                                        if (GUILayout.Button(Label(dest) + " — " + item.material.name + " / " + item.shape + " / " + item.size))
                                        { target = dest; material = item; amountText = item.amount.ToString(CultureInfo.InvariantCulture); }
                                    }
                        }
                if (!any) GUILayout.Label("No compatible storage with available capacity on this vessel.");
                GUILayout.EndScrollView();
            }
            else
            {
                GUILayout.Label(Label(target));
                GUILayout.Label("Amount" + (source is ChitakTechMaterialStorage ? " (whole units)" : ""));
                amountText = GUILayout.TextField(amountText);
                if (error != null) GUILayout.Label(error);
                if (GUILayout.Button("OK")) StartTransfer();
                if (GUILayout.Button("Cancel")) { target = null; error = null; }
            }
            GUI.DragWindow();
        }

        private void StartTransfer()
        {
            if (!double.TryParse(amountText, NumberStyles.Float, CultureInfo.InvariantCulture, out double amount)
                || !CTShared.IsFinite(amount) || amount <= 0
                || (source is ChitakTechMaterialStorage && (amount > int.MaxValue || amount != Math.Floor(amount))))
            { error = "Enter a positive " + (source is ChitakTechMaterialStorage ? "whole number." : "number."); return; }
            if (source.part?.vessel == null || target?.part?.vessel != source.part.vessel)
            { error = "Destination is no longer on this vessel."; return; }
            Notify("Storage transfer started.");
            if (source is ChitakTechMaterialStorage mats)
            {
                int moved = mats.TransferMaterialTo(target as ChitakTechMaterialStorage, material, (int)amount);
                Notify("Material transfer: " + moved + " units moved.");
                target = null;
            }
            else { remaining = amount; transferred = 0; running = true; }
        }

        public void FixedUpdate()
        {
            if (!running) return;
            var from = source as ChitakTechStorage;
            var to = target as ChitakTechStorage;
            if (!HighLogic.LoadedSceneIsFlight || from == null || to == null || from.part?.vessel == null
                || from.part.vessel != to.part?.vessel || !from.TransfersEnabled || !to.TransfersEnabled)
            { Finish("storage unavailable"); return; }
            double space = to.GetAvailableSpace(resource, 1, true);
            if (from.GetStoredAmount(resource) <= 0 || space <= 0)
            { Finish("source empty or destination full"); return; }
            double accepted = from.TransferResourceTo(to, resource, remaining);
            transferred += accepted;
            remaining -= accepted;
            if (remaining <= Math.Max(1e-10, transferred * 1e-12)) Finish("requested amount reached");
        }

        private void Finish(string reason)
        {
            running = false;
            Notify("Storage transfer finished: " + transferred.ToString("G6") + " units moved (" + reason + ").");
            target = null;
        }

        public void OnDestroy() { if (running) Finish("source unloaded or destroyed"); }
    }
}
