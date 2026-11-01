using System;

namespace ChitakTech
{
    // Game-time seconds, like the configured lifetime. Kept independent of Unity.
    internal static class ExplosionAnimation
    {
        internal const double GrowSeconds = 2, ShrinkSeconds = 5, ShockSeconds = 20;
        internal static double MainScale(double age, double hold)
            => Math.Max(0, Math.Min(Math.Min(age / GrowSeconds, 1),
                (GrowSeconds + hold + ShrinkSeconds - age) / ShrinkSeconds));
        internal static double ShockScale(double age) => Math.Max(0, Math.Min(10, age / GrowSeconds));
        internal static double ShockOpacity(double age)
            => age < 0 || age >= ShockSeconds ? 0 : .35 * Math.Pow(1 - age / ShockSeconds, 2);
        internal static double Duration(double hold) => Math.Max(GrowSeconds + hold + ShrinkSeconds, ShockSeconds);
        internal static bool Crossed(double previousAge, double age, double normalizedDistance, double maxScale)
            => age >= 0 && previousAge < GrowSeconds * maxScale
                && normalizedDistance >= 0 && normalizedDistance <= maxScale
                && normalizedDistance >= Math.Max(0, previousAge / GrowSeconds)
                && normalizedDistance <= age / GrowSeconds;
    }
}
