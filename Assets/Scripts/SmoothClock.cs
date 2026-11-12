using System;

/// Turns a stepwise clock into a smooth one.
///
/// Problem: the audio system reports its position in chunks (one value per audio buffer, about 21 ms or more).
/// Reading it from a 60 fps game loop means the value is "stale" by anywhere from 0 to one buffer, and that
/// staleness changes every frame. Interpolating from the last change makes the clock jump by up to a frame
/// every time the audio advances. Notes shimmer and judging gets noisy.
///
/// Fix: let real time (smooth, high resolution) drive the clock, and only estimate the constant
/// offset between the two:   offset = source - realtime.
/// A stale reading can only make that offset look too small, never too large. So the best estimate is the
/// MAXIMUM offset seen over a short window: the reading taken right after the source advanced.
/// The estimate then moves slowly (slew limited) so a better sample never makes the clock jump.
///
/// Failure handling:
///  - Offset falls for good (source stalled or was reset): the max over the last 0.3 s drops well below
///    the estimate, so the window is rebuilt from recent samples.
///  - One reading far too high (a driver glitch such as a doubled position): rejected. Only accepted
///    after upConfirm readings in a row, which is a real jump (seek, restart).
public sealed class SmoothClock
{
    const int Capacity = 4096;   // 2 s at 2000 fps, far more than needed

    readonly double windowSec, recentSec, slewPerSec, fastSlewPerSec, fastAbove, downTol, upSpike;
    readonly int upConfirm;

    readonly double[] ts = new double[Capacity];
    readonly double[] raws = new double[Capacity];
    int first, count;
    double lastT;
    int pendingUp;

    public bool HasValue { get; private set; }

    /// Estimated (source time - real time). Source time at realtime t is t + Offset.
    public double Offset { get; private set; }

    /// Times the estimate was restarted (stall, seek, or confirmed jump). Shown in the timing overlay.
    public int Resets { get; private set; }

    /// Single readings thrown away as glitches.
    public int RejectedSpikes { get; private set; }

    public SmoothClock(double windowSec = 2.0, double recentSec = 0.3, double slewPerSec = 0.03,
                       double fastSlewPerSec = 0.3, double fastAbove = 0.05, double downTol = 0.04,
                       double upSpike = 0.05, int upConfirm = 5)
    {
        this.windowSec = windowSec;
        this.recentSec = recentSec;
        this.slewPerSec = slewPerSec;
        this.fastSlewPerSec = fastSlewPerSec;
        this.fastAbove = fastAbove;
        this.downTol = downTol;
        this.upSpike = upSpike;
        this.upConfirm = upConfirm;
    }

    public double SourceAt(double realtime) => realtime + Offset;

    public void Reset()
    {
        HasValue = false;
        first = 0; count = 0; pendingUp = 0;
        Offset = 0;
    }

    /// Feed one reading: real time now, and what the source says now.
    public void Add(double t, double source)
    {
        double raw = source - t;

        if (!HasValue)
        {
            Restart(t, raw);
            return;
        }

        // Reading far above the estimate: glitch, unless it keeps happening.
        if (raw - Offset > upSpike)
        {
            pendingUp++;
            if (pendingUp >= upConfirm) { Restart(t, raw); Resets++; }
            else RejectedSpikes++;
            return;
        }
        pendingUp = 0;

        Push(t, raw);
        Trim(t - windowSec);

        double recent = MaxSince(t - recentSec);
        if (Offset - recent > downTol)
        {
            // The offset really went down (stall, seek back). Forget the older, higher samples.
            // Restart from a shorter, newer span than the one that detected it: the longer span may still hold
            // samples from before the change, and they would keep the estimate too high for a whole window.
            double span = recentSec / 3.0;
            Trim(t - span);
            Offset = MaxSince(t - span);
            Resets++;
            lastT = t;
            return;
        }

        double target = MaxSince(double.NegativeInfinity);
        double err = target - Offset;
        double dt = Math.Min(Math.Max(t - lastT, 0.0), 0.25);
        double rate = Math.Abs(err) > fastAbove ? fastSlewPerSec : slewPerSec;
        double limit = rate * dt;
        Offset += Math.Max(-limit, Math.Min(limit, err));
        lastT = t;
    }

    void Restart(double t, double raw)
    {
        first = 0; count = 0; pendingUp = 0;
        Push(t, raw);
        Offset = raw;
        lastT = t;
        HasValue = true;
    }

    void Push(double t, double raw)
    {
        if (count == Capacity) { first = (first + 1) % Capacity; count--; }   // full: drop the oldest
        int i = (first + count) % Capacity;
        ts[i] = t;
        raws[i] = raw;
        count++;
    }

    void Trim(double olderThan)
    {
        while (count > 1 && ts[first] < olderThan) { first = (first + 1) % Capacity; count--; }
    }

    double MaxSince(double since)
    {
        double best = double.NegativeInfinity;
        for (int k = 0; k < count; k++)
        {
            int i = (first + k) % Capacity;
            if (ts[i] >= since && raws[i] > best) best = raws[i];
        }
        return best;
    }
}
