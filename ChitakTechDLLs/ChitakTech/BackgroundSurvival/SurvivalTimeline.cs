using System;

namespace ChitakTech
{
    internal static class SurvivalTimeline
    {
        // Preserve unfinished intervals across updates instead of dropping catch-up time.
        internal static double Next(double cursor, double now, double period, double rotationPeriod,
            double periapsisEpoch, bool elliptical)
        {
            double step = 60;
            if (Finite(period) && period > 0) step = Math.Min(step, period / 720);
            if (Finite(rotationPeriod) && rotationPeriod != 0) step = Math.Min(step, Math.Abs(rotationPeriod) / 720);
            double next = Math.Min(now, cursor + Math.Max(0.01, step));
            if (Finite(periapsisEpoch))
            {
                next = IncludeApsis(cursor, next, periapsisEpoch, elliptical ? period : 0);
                if (elliptical) next = IncludeApsis(cursor, next, periapsisEpoch + period / 2, period);
            }
            return next;
        }

        private static double IncludeApsis(double cursor, double next, double epoch, double period)
        {
            double time = epoch;
            if (Finite(period) && period > 0) time += (Math.Floor((cursor - epoch) / period) + 1) * period;
            return time > cursor && time < next ? time : next;
        }
        internal static bool Finite(double n) => !double.IsNaN(n) && !double.IsInfinity(n);
        internal static double LatestApsis(double now, double epoch, double period)
            => Finite(period) && period > 0 ? epoch + Math.Floor((now - epoch) / period) * period : epoch;
    }
}
