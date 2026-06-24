using System;
using System.Threading.Tasks;
using Godot;

namespace Nova;

/// <summary>
/// Full-screen time-lapse overlay, ported from the HyBloom fork's TimeScrollController (not upstream
/// Colorless). Cascade ramp: sec -> min -> hour -> day (based on span), then reverse. Peak speed is
/// auto-computed so the clock advances from 'from' to approximately 'to - finalTickDuration' before
/// the final 1x tick, eliminating the end-snap.
///
/// Almost all of this is platform-independent .NET math (DateTime arithmetic, the analytic SmoothStep
/// integration, the peak-speed solving algebra) carried over verbatim from Nova1 - only the engine
/// call surface changed: Unity coroutines -> async/await ToSignal, Time.deltaTime ->
/// GetProcessDeltaTime(), Time.realtimeSinceStartup -> Time.GetTicksUsec(), TextMeshProUGUI -> Label,
/// Lua binding -> StateManager/ObjectManager. This is the porting-guide's "engine-API port, keep logic
/// as-is" case in its purest form; it doesn't go through the generic AnimationEntry/PropertyAnimation
/// framework since a cascading-speed clock isn't a single eased property.
///
/// The control is its own panel (Visible/Modulate toggled directly on self) rather than Nova1's
/// separate panel child object - Godot's Visible = false doesn't freeze a node's running async method
/// the way Unity's GameObject.SetActive(false) would stop a coroutine, so there's no need to keep the
/// controller on a distinct "always active" parent.
///
/// Registers as an IStateObject purely for the ResetToBaseline hook, same reasoning as
/// VideoController: a time-scroll left running from an abandoned live session needs to be force-
/// cancelled when a fresh game/restore replay starts. Nova1 has no equivalent guard; this follows the
/// convention nova2 already established for this category of component (see VideoController).
/// </summary>
public partial class TimeScrollController : Control, IStateObject
{
    [Export]
    private string _bindName;

    [Export]
    private Label _displayLabel;

    // Timing (seconds)
    [Export]
    private float _holdStartDuration = 0.5f;
    // Duration of each cascade speed stage (1->60, 60->3600, 3600->86400)
    [Export]
    private float _stageTransitionDuration = 0.8f;
    [Export]
    private float _finalTickDuration = 4f;
    [Export]
    private float _holdEndDuration = 1.5f;
    [Export]
    private float _fadeOutDuration = 0.8f;

    // ── public state ──────────────────────────────────────────────────────
    public bool IsFinished { get; private set; }

    // ── cascade speed table ───────────────────────────────────────────────
    // SpeedBreakpoints[i]: sim-sec/real-sec threshold at which unit i becomes visible.
    //   0 = 1       seconds tick
    //   1 = 60      minutes tick    (1 min/sec)
    //   2 = 3600    hours tick      (1 hour/sec)
    //   3 = 86400   days tick       (1 day/sec)
    private static readonly float[] s_speedBreakpoints = { 1f, 60f, 3600f, 86400f };

    // Minimum span (seconds) to include stage i (transition Breakpoints[i]->Breakpoints[i+1])
    private static readonly double[] s_stageMinSpan = { 0.0, 120.0, 7200.0 };

    // Current clock value - field so helper methods can update it
    private DateTime _current;

    // Display state
    // _displaySpeed: current sim-sec/real-sec (set every frame by ramp/peak/tick phases)
    // _lastDisplayKey: encoded key of last rendered frame; avoids redundant text-set calls
    // _animStartUsec/_displayUpdateTimer: drive rolling-digit frame throttle
    private float _displaySpeed = 1f;
    private long _lastDisplayKey = long.MinValue;
    private ulong _animStartUsec;
    private float _displayUpdateTimer;
    // True only during ramp-up and peak; false during ramp-down so the display
    // shows real _current values decelerating smoothly into the final tick.
    private bool _rollingEnabled;

    // Rolling-digit visual rates (changes/real-sec) and fps cap for the rolling mode
    private const float RollingFps = 60f;
    private const float SecRollRate = 60f;
    private const float MinRollRate = 60f;
    private const float HourRollRate = 40f;

    // Incremented on every Play() call and on ResetToBaseline(); every await in the in-flight
    // sequence checks it on resume and bails out if it no longer matches, the async equivalent of
    // Unity's StopCoroutine for a component that has no CancellationToken plumbing.
    private int _playToken;

    // ── lifecycle ─────────────────────────────────────────────────────────

    public override void _EnterTree()
    {
        StateManager.Instance.RegisterState(_bindName, this);
    }

    public void Sync() { }
    public void SyncImmediate() { }
    public void SyncBackend() { }

    public void ResetToBaseline()
    {
        _playToken++;
        Visible = false;
        Modulate = Colors.White;
    }

    // ── public API ────────────────────────────────────────────────────────

    public async void Play(
        int fromYear, int fromMonth, int fromDay, int fromHour, int fromMin, int fromSec,
        int toYear, int toMonth, int toDay, int toHour, int toMin, int toSec,
        float midDuration)
    {
        var from = new DateTime(fromYear, fromMonth, fromDay, fromHour, fromMin, fromSec);
        var to = new DateTime(toYear, toMonth, toDay, toHour, toMin, toSec);

        var token = ++_playToken;

        Visible = true;
        Modulate = Colors.White;
        _displaySpeed = 1f;
        _lastDisplayKey = long.MinValue;
        _animStartUsec = Time.GetTicksUsec();
        _displayUpdateTimer = 0f;
        _rollingEnabled = false;
        _current = from;
        SetDisplay(_current);

        double spanSec = Math.Abs((to - from).TotalSeconds);
        if (spanSec < 1.0) spanSec = 1.0;

        // ── Determine active cascade stages ───────────────────────────────
        var stageCount = 1;
        for (var i = 1; i < s_stageMinSpan.Length; i++)
        {
            if (spanSec >= s_stageMinSpan[i]) stageCount = i + 1;
        }

        // Reduce stageCount if the cascade alone already exceeds the span
        while (stageCount > 0 && CascadeAdv(stageCount) * 2 + _finalTickDuration > spanSec)
        {
            stageCount--;
        }

        var naturalPeak = stageCount > 0 ? s_speedBreakpoints[stageCount] : 1f;

        // ── Compute adaptive peakSpeed and optional bridge ─────────────────
        // We want: cascadeUp + bridgeUp + peak + bridgeDown + cascadeDown + finalTick = span
        // Bridge: (naturalPeak + peakSpeed) / 2 * bridgeDuration  (each side)
        // Solve for peakSpeed given midDuration (user's desired peak length).
        double cascadeTotal = CascadeAdv(stageCount) * 2;
        double remaining = spanSec - cascadeTotal - _finalTickDuration;

        var peakSpeed = naturalPeak;
        var effectiveMidDuration = midDuration;
        var hasBridge = false;
        var bridgeDuration = _stageTransitionDuration;

        if (remaining > 0)
        {
            if (remaining <= (double)naturalPeak * midDuration)
            {
                // Natural peak is fast enough; shorten it to avoid overshoot
                peakSpeed = naturalPeak;
                effectiveMidDuration = Mathf.Max(0.2f, (float)(remaining / naturalPeak));
            }
            else
            {
                // Need a faster peak + bridge to cover the span in midDuration seconds:
                // remaining = naturalPeak * bridgeDuration
                //           + peakSpeed * (bridgeDuration + midDuration)
                // => peakSpeed = (remaining - naturalPeak*bridgeDuration)
                //                / (bridgeDuration + midDuration)
                double numerator = remaining - (double)naturalPeak * bridgeDuration;
                if (numerator > 0)
                {
                    peakSpeed = (float)(numerator / (bridgeDuration + midDuration));
                    hasBridge = peakSpeed > naturalPeak;
                }
                if (!hasBridge) peakSpeed = naturalPeak;
            }
        }

        // ── Phase 0: hold start ───────────────────────────────────────────
        await ToSignal(GetTree().CreateTimer(_holdStartDuration), SceneTreeTimer.SignalName.Timeout);
        if (token != _playToken) return;

        // ── Phase 1: cascade ramp-up  (sec -> min -> hour -> day) ───────────
        _rollingEnabled = true;
        for (var s = 0; s < stageCount; s++)
        {
            await RampStage(s_speedBreakpoints[s], s_speedBreakpoints[s + 1], _stageTransitionDuration, token);
            if (token != _playToken) return;
        }

        // Bridge up
        if (hasBridge)
        {
            await RampStage(naturalPeak, peakSpeed, bridgeDuration, token);
            if (token != _playToken) return;
        }

        // ── Phase 2: peak speed ───────────────────────────────────────────
        _displaySpeed = peakSpeed;
        if (effectiveMidDuration > 0f)
        {
            var peakStart = _current;
            var elapsed = 0f;
            while (elapsed < effectiveMidDuration)
            {
                var dt = (float)GetProcessDeltaTime();
                // Clamp so the loop exits cleanly on the exact final frame,
                // giving _current = peakStart + peakSpeed * effectiveMidDuration exactly.
                elapsed = Mathf.Min(elapsed + dt, effectiveMidDuration);
                _current = peakStart.AddSeconds((double)peakSpeed * elapsed);
                SetDisplay(_current);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                if (token != _playToken) return;
            }
        }

        // ── Phase 3: cascade ramp-down  (day -> hour -> min -> sec) ─────────
        // Switch off rolling so ramp-down shows real _current values,
        // producing a smooth deceleration directly into the final tick.
        _rollingEnabled = false;
        _lastDisplayKey = long.MinValue; // force one redraw with actual value
        // Dynamically scale ramp-down speeds so _current converges to
        // (to - finalTickDuration) regardless of frame-timing drift.
        var targetBeforeFinalTick = to.AddSeconds(-_finalTickDuration);
        double naturalRampDownAdv = NaturalRampDownAdv(stageCount, hasBridge, naturalPeak, peakSpeed, bridgeDuration);
        double toTraverse = (targetBeforeFinalTick - _current).TotalSeconds;

        // Scale factor: how much to multiply each ramp-down speed by.
        // Clamped to [0.05, 5] to stay visually reasonable.
        var scale = naturalRampDownAdv > 0.0
            ? Mathf.Clamp((float)(toTraverse / naturalRampDownAdv), 0.05f, 5f)
            : 1f;

        // Bridge down
        if (hasBridge)
        {
            await RampStage(peakSpeed * scale, naturalPeak * scale, bridgeDuration, token);
            if (token != _playToken) return;
        }

        // Cascade down: recompute scale before every stage so that frame-timing
        // errors from earlier stages are corrected rather than accumulated.
        // Each recomputation uses the live _current and the natural advance of
        // the remaining stages, so the final stage ends very close to
        // targetBeforeFinalTick without any hard snap.
        for (var s = stageCount - 1; s >= 0; s--)
        {
            double distLeft = Math.Max(0.0, (targetBeforeFinalTick - _current).TotalSeconds);
            double natural = CascadeAdv(s + 1);
            var sc = natural > 0.0
                ? Mathf.Clamp((float)(distLeft / natural), 0.05f, 5f)
                : 1f;
            await RampStage(s_speedBreakpoints[s + 1] * sc, s_speedBreakpoints[s] * sc, _stageTransitionDuration,
                token);
            if (token != _playToken) return;
        }

        // ── Phase 4: final tick  (1x real speed, whole-second jumps) ──────
        // Per-stage scale correction keeps residual drift to < 2 s, so the
        // hard snap below is imperceptible (sub-second jump at 1x display rate).
        _displaySpeed = 1f;
        _lastDisplayKey = long.MinValue;
        _current = targetBeforeFinalTick;
        SetDisplay(_current);

        var tickElapsed = 0f;
        var lastSecond = -1;
        while (tickElapsed < _finalTickDuration)
        {
            var dt = (float)GetProcessDeltaTime();
            tickElapsed += dt;
            _current = _current.AddSeconds(dt);
            var sec = _current.Second;
            if (sec != lastSecond)
            {
                lastSecond = sec;
                SetDisplay(_current);
            }
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (token != _playToken) return;
        }

        // Snap to exact target (residual < 1 real-time frame at 1x speed ~ imperceptible)
        _lastDisplayKey = long.MinValue; // force render even if second matches
        SetDisplay(to);

        // ── Phase 5: hold end ─────────────────────────────────────────────
        await ToSignal(GetTree().CreateTimer(_holdEndDuration), SceneTreeTimer.SignalName.Timeout);
        if (token != _playToken) return;

        // ── Phase 6: fade out ─────────────────────────────────────────────
        // Plain linear alpha fade, unlike the cascade math above - the porting-guide's animation
        // section prefers Godot's built-in Tween over bespoke per-frame loops whenever an effect is
        // just a single eased property, which this is.
        var tween = CreateTween();
        tween.TweenProperty(this, "modulate:a", 0.0, _fadeOutDuration);
        await ToSignal(tween, Tween.SignalName.Finished);
        if (token != _playToken) return;

        // Set IsFinished BEFORE hiding, mirroring Nova1's ordering in case a future caller polls it.
        IsFinished = true;
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (token != _playToken) return;

        Visible = false;
    }

    /// <summary>
    /// Returns the total expected animation duration in seconds. Used by time_scroll.gd to drive
    /// entry.Delay(GetTotalDuration(...)) blocking - pure function, no side effects, safe to call
    /// during a restore replay where Play() itself is skipped.
    /// </summary>
    public float GetTotalDuration(
        int fromYear, int fromMonth, int fromDay, int fromHour, int fromMin, int fromSec,
        int toYear, int toMonth, int toDay, int toHour, int toMin, int toSec,
        float midDuration)
    {
        var from = new DateTime(fromYear, fromMonth, fromDay, fromHour, fromMin, fromSec);
        var to = new DateTime(toYear, toMonth, toDay, toHour, toMin, toSec);

        double spanSec = Math.Abs((to - from).TotalSeconds);
        if (spanSec < 1.0) spanSec = 1.0;

        var stageCount = 1;
        for (var i = 1; i < s_stageMinSpan.Length; i++)
        {
            if (spanSec >= s_stageMinSpan[i]) stageCount = i + 1;
        }
        while (stageCount > 0 && CascadeAdv(stageCount) * 2 + _finalTickDuration > spanSec)
        {
            stageCount--;
        }

        var naturalPeak = stageCount > 0 ? s_speedBreakpoints[stageCount] : 1f;
        double cascadeTotal = CascadeAdv(stageCount) * 2;
        double remaining = spanSec - cascadeTotal - _finalTickDuration;

        var hasBridge = false;
        var bridgeDuration = _stageTransitionDuration;
        var effectiveMidDuration = midDuration;

        if (remaining > 0)
        {
            if (remaining <= (double)naturalPeak * midDuration)
            {
                effectiveMidDuration = Mathf.Max(0.2f, (float)(remaining / naturalPeak));
            }
            else
            {
                double numerator = remaining - (double)naturalPeak * bridgeDuration;
                hasBridge = numerator > 0;
            }
        }

        return _holdStartDuration
            + stageCount * _stageTransitionDuration * 2
            + (hasBridge ? bridgeDuration * 2 : 0f)
            + effectiveMidDuration
            + _finalTickDuration
            + _holdEndDuration
            + _fadeOutDuration;
    }

    // ── helper coroutine ──────────────────────────────────────────────────

    // Analytical SmoothStep integration: position is computed directly from
    // the closed-form integral rather than accumulated frame-by-frame.
    // speed(t) = Lerp(speedFrom, speedTo, SmoothStep(t/duration))
    // advance(T) = speedFrom*T + (speedTo-speedFrom)*duration*(u^3 - u^4/2)
    //   where u = T/duration, and (u^3 - u^4/2) = Integral_0^u SmoothStep(s) ds
    // This eliminates O(dt*speed) per-frame drift entirely, so even a bridge
    // running at 65 000 000 sim-sec/real-sec converges to floating-point precision.
    private async Task RampStage(float speedFrom, float speedTo, float duration, int token)
    {
        var stageStart = _current;
        var elapsed = 0f;
        while (elapsed < duration)
        {
            var dt = (float)GetProcessDeltaTime();
            elapsed = Mathf.Min(elapsed + dt, duration);
            double u = elapsed / duration;
            double ss = u * u * u * (1.0 - 0.5 * u); // Integral_0^u SmoothStep(s) ds
            _current = stageStart.AddSeconds(speedFrom * elapsed + (speedTo - speedFrom) * duration * ss);
            _displaySpeed = Mathf.Lerp(speedFrom, speedTo, SmoothStep01((float)u));
            SetDisplay(_current);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (token != _playToken) return;
        }
    }

    // Equivalent to Unity's Mathf.SmoothStep(0f, 1f, t) - inlined rather than calling into Godot's
    // Mathf.SmoothStep to avoid depending on its exact overload/clamping semantics for an input that
    // is already guaranteed to be in [0, 1] here.
    private static float SmoothStep01(float t) => t * t * (3f - 2f * t);

    // ── math helpers ──────────────────────────────────────────────────────

    // Total seconds advanced in one direction of the cascade ramp (SmoothStep integral = avg)
    private double CascadeAdv(int stages)
    {
        double adv = 0;
        for (var i = 0; i < stages; i++)
        {
            adv += (s_speedBreakpoints[i] + (double)s_speedBreakpoints[i + 1]) / 2.0 * _stageTransitionDuration;
        }
        return adv;
    }

    // Total seconds the natural (unscaled) ramp-down would advance
    private double NaturalRampDownAdv(int stages, bool bridge, float naturalPeak, float peakSpd, float bridgeDur)
    {
        var adv = CascadeAdv(stages);
        if (bridge)
        {
            adv += (peakSpd + naturalPeak) / 2.0 * bridgeDur;
        }
        return adv;
    }

    // ── display ───────────────────────────────────────────────────────────
    // Two modes based on current sim speed:
    //
    // Rolling mode  (speed >= SpeedBreakpoints[1] = 60):
    //   Fast sub-units are replaced by independent visual animations so the
    //   display shows smooth, legible scrolling instead of random-looking jumps.
    //   Coarse units (day/month/year) always show actual values.
    //   Updates are capped at RollingFps to avoid per-frame text-set overhead.
    //     speed >= 60     : seconds roll at SecRollRate
    //     speed >= 3 600   : + minutes roll at MinRollRate
    //     speed >= 86 400  : + hours   roll at HourRollRate
    //
    // Actual mode  (speed < 60):
    //   Shows real timestamp; text is set only when the second changes.

    private void SetDisplay(DateTime dt)
    {
        var spd = Mathf.Abs(_displaySpeed);

        if (_rollingEnabled && spd >= s_speedBreakpoints[1])
        {
            // Rolling mode: throttle to RollingFps
            _displayUpdateTimer += (float)GetProcessDeltaTime();
            if (_displayUpdateTimer < 1f / RollingFps) return;
            _displayUpdateTimer = 0f;

            var elapsed = (Time.GetTicksUsec() - _animStartUsec) / 1_000_000f;

            var visSec = (int)(elapsed * SecRollRate) % 60;
            var visMin = spd >= s_speedBreakpoints[2]
                ? (int)(elapsed * MinRollRate) % 60
                : dt.Minute;
            var visHour = spd >= s_speedBreakpoints[3]
                ? (int)(elapsed * HourRollRate) % 24
                : dt.Hour;

            _displayLabel.Text = string.Format(
                "{0}.{1:D2}.{2:D2} {3:D2}:{4:D2}:{5:D2}",
                dt.Year, dt.Month, dt.Day, visHour, visMin, visSec);
        }
        else
        {
            // Actual mode: only redraw when the second changes
            var key = (((dt.Year * 10000L + dt.Month * 100L + dt.Day) * 100L
                + dt.Hour) * 100L + dt.Minute) * 100L + dt.Second;
            if (key == _lastDisplayKey) return;
            _lastDisplayKey = key;

            _displayLabel.Text = string.Format(
                "{0}.{1:D2}.{2:D2} {3:D2}:{4:D2}:{5:D2}",
                dt.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, dt.Second);
        }
    }
}
