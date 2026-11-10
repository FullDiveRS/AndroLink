namespace AndroLink.UI;

/// <summary>Easing curves for <see cref="Animator"/>.</summary>
public static class Easing
{
    public static double Linear(double t) => t;
    public static double EaseOut(double t) => 1 - (1 - t) * (1 - t);
    public static double EaseIn(double t) => t * t;
    public static double EaseInOut(double t) => t < 0.5 ? 2 * t * t : 1 - Math.Pow(-2 * t + 2, 2) / 2;
    public static double EaseOutBack(double t)
    {
        const double c1 = 1.70158;
        const double c3 = c1 + 1;
        return 1 + c3 * Math.Pow(t - 1, 3) + c1 * Math.Pow(t - 1, 2);
    }
}

/// <summary>Handle to a running animation; dispose to cancel.</summary>
public sealed class AnimationToken : IDisposable
{
    internal Animation? Animation { get; set; }
    public void Dispose() => Animation?.Stop();
}

/// <summary>
/// Central tween engine. The platform host pumps <see cref="Tick"/> at ~60 fps;
/// animations invoke their callbacks and keep the UI thread busy between
/// frames. Controls typically invalidate their visuals inside the tick.
/// </summary>
public static class Animator
{
    private sealed class Running
    {
        public double From, To;
        public double Start; // milliseconds from Tick clock
        public double DurationMs;
        public Func<double, double> EasingFn = Easing.EaseOut;
        public Action<double> OnTick = delegate { };
        public Action? Completed;
        public UIElement? Owner;
    }

    private static readonly List<Running> Active = new();
    private static readonly object Gate = new();
    private static double _clockMs;
    private static long _lastReal;
    private static bool _clockStarted;

    /// <summary>
    /// Animates a value from..to over the duration. Set <paramref name="owner"/>
    /// to have the element's visuals invalidated automatically each tick.
    /// </summary>
    public static AnimationToken Animate(
        double from, double to, TimeSpan duration, Action<double> onTick,
        Func<double, double>? easing = null, Action? completed = null, UIElement? owner = null)
    {
        EnsureClock();
        var run = new Running
        {
            From = from,
            To = to,
            Start = _clockMs,
            DurationMs = Math.Max(1, duration.TotalMilliseconds),
            EasingFn = easing ?? Easing.EaseOut,
            OnTick = onTick,
            Completed = completed,
            Owner = owner,
        };
        lock (Gate)
        {
            Active.RemoveAll(r => r.Owner == owner && r.Completed == completed);
            Active.Add(run);
        }

        // Emit the first frame immediately so there is no visual pop.
        onTick(from);
        return new AnimationToken { Animation = new TokenHandle(run) };
    }

    /// <summary>Advances every running animation. Called by the platform at ~60 fps.</summary>
    public static void Tick()
    {
        EnsureClock();
        List<Running> snapshot;
        lock (Gate)
        {
            AdvanceClock();
            snapshot = new List<Running>(Active);
        }

        foreach (var run in snapshot)
        {
            var t = (_clockMs - run.Start) / run.DurationMs;
            if (t >= 1)
            {
                run.OnTick(run.To);
                lock (Gate) Active.Remove(run);
                run.Completed?.Invoke();
                continue;
            }
            var eased = run.EasingFn(Math.Clamp(t, 0, 1));
            run.OnTick(run.From + (run.To - run.From) * eased);
            run.Owner?.InvalidateVisual();
        }
    }

    public static bool HasActiveAnimations(UIElement? owner = null)
    {
        lock (Gate)
        {
            return owner is null ? Active.Count > 0 : Active.Any(a => a.Owner == owner);
        }
    }

    private static void EnsureClock()
    {
        if (_clockStarted) return;
        _lastReal = Environment.TickCount64;
        _clockStarted = true;
    }

    private static void AdvanceClock()
    {
        var now = Environment.TickCount64;
        _clockMs += Math.Min(100, now - _lastReal); // clamp pauses (breakpoints, suspended process)
        _lastReal = now;
    }

    private sealed class TokenHandle : Animation
    {
        public TokenHandle(Running run) => Run = run;
        public Running Run { get; }
        public override void Stop()
        {
            lock (Gate) Active.Remove(Run);
        }
    }
}

public abstract class Animation
{
    public abstract void Stop();
}
