// Deliberately fixed regression data, independent of any shipped part or balancing.
internal static class ConstructionConfig
{
    internal static ConfigNode CreatePart()
    {
        var part = new ConfigNode("PART");
        var module = new ConfigNode("MODULE"); part.AddNode(module);
        module.AddValue("name", "ChitakTechPart");
        var cost = new ConfigNode("MATERIAL_COST"); module.AddNode(cost);
        cost.AddValue("name", "Wood"); cost.AddValue("shape", "HalfHollowLog");
        cost.AddValue("size", "none"); cost.AddValue("amount", 2);
        var bounds = new ConfigNode("PARAMS"); cost.AddNode(bounds);
        bounds.AddValue("height", "EM4.9"); bounds.AddValue("height", "EL5.1");
        bounds.AddValue("radius", "EM0.8"); bounds.AddValue("radius", "EL1.0");
        return part;
    }
}
