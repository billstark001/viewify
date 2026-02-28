using Viewify.Base;
using Viewify.Core.Render;

namespace Viewify.Test;

// ── Views used by effect tests ────────────────────────────────────────────────

internal class MountFlagView : View
{
    public bool MountCalled  { get; private set; }
    public bool UnmountCalled { get; private set; }

    [MountEffect]
    void OnMount()   => MountCalled   = true;

    [UnmountEffect]
    void OnUnmount() => UnmountCalled = true;

    public override View? Render() => null;
}

internal class LayoutEffectView : View
{
    [State(0)] public IState<int> Counter = null!;
    public List<int> LayoutLog { get; } = new();

    [LayoutEffect(nameof(Counter))]
    void OnCounterChanged() => LayoutLog.Add(~Counter);

    public override View? Render() => null;
}

internal class LegacyEffectView : View
{
    [State(0)] public IState<int> Counter = null!;
    public List<int> EffectLog { get; } = new();

    [Effect(nameof(Counter))]   // backward-compat alias
    void OnCounterChanged() => EffectLog.Add(~Counter);

    public override View? Render() => null;
}

internal class WireEffectView : View
{
    [State(0)] public IState<int> Counter = null!;
    public List<int> WireLog { get; } = new();

    [WireEffect(nameof(Counter))]
    void BeforeUpdate() => WireLog.Add(~Counter);

    public override View? Render() => null;
}

internal class AsyncEffectView : View
{
    [State(0)] public IState<int> Counter = null!;
    public List<int> AsyncLog { get; } = new();

    [AsyncEffect(nameof(Counter))]
    void OnAsync() => AsyncLog.Add(~Counter);

    public override View? Render() => null;
}

// ── Tests ─────────────────────────────────────────────────────────────────────

/// <summary>Tests for all five effect phases.</summary>
public class EffectTests
{
    private static Scheduler MakeScheduler(View root)
        => new Scheduler(root, new RecordingHandler());

    private static void Drain(Scheduler s, int max = 2000)
    {
        int i = 0;
        while (s.UnitTick() && ++i < max) { }
    }

    // ── Mount / Unmount ───────────────────────────────────────────────────────

    [Fact]
    public void MountEffect_CalledOnFirstMount()
    {
        var view = new MountFlagView();
        Drain(MakeScheduler(view));
        Assert.True(view.MountCalled);
    }

    [Fact]
    public void UnmountEffect_IsRegistered()
    {
        // Verify that the attribute is picked up by StatefulClassRecord
        var cache  = new ViewRecordCache();
        var record = cache.Get(typeof(MountFlagView));
        Assert.NotEmpty(record.UnmountEffects);
    }

    // ── LayoutEffect ──────────────────────────────────────────────────────────

    [Fact]
    public void LayoutEffect_RunsOnMount()
    {
        var view = new LayoutEffectView();
        Drain(MakeScheduler(view));
        // Effect runs on mount (first dep calculation always shows "changed")
        Assert.NotEmpty(view.LayoutLog);
    }

    [Fact]
    public void LayoutEffect_RunsWhenDepChanges()
    {
        var view = new LayoutEffectView();
        var s    = MakeScheduler(view);
        Drain(s);
        var before = view.LayoutLog.Count;

        view.Counter %= 42;
        Drain(s);

        Assert.True(view.LayoutLog.Count > before);
        Assert.Equal(42, view.LayoutLog[^1]);
    }

    [Fact]
    public void LegacyEffectAttribute_IsEquivalentToLayoutEffect()
    {
        var view = new LegacyEffectView();
        var s    = MakeScheduler(view);
        Drain(s);

        view.Counter %= 7;
        Drain(s);

        Assert.Contains(7, view.EffectLog);
    }

    // ── WireEffect ────────────────────────────────────────────────────────────

    [Fact]
    public void WireEffect_IsRegisteredInStatefulClassRecord()
    {
        var record = new ViewRecordCache().Get(typeof(WireEffectView));
        Assert.NotEmpty(record.WireEffects);
    }

    [Fact]
    public void WireEffect_RunsWhenDepChanges()
    {
        var view = new WireEffectView();
        var s    = MakeScheduler(view);
        Drain(s);
        var before = view.WireLog.Count;

        view.Counter %= 10;
        Drain(s);

        Assert.True(view.WireLog.Count > before);
    }

    // ── AsyncEffect ───────────────────────────────────────────────────────────

    [Fact]
    public void AsyncEffect_IsRegisteredInStatefulClassRecord()
    {
        var record = new ViewRecordCache().Get(typeof(AsyncEffectView));
        Assert.NotEmpty(record.AsyncEffects);
    }

    [Fact]
    public void AsyncEffect_RunsWhenDepChanges()
    {
        var view = new AsyncEffectView();
        var s    = MakeScheduler(view);
        Drain(s);
        var before = view.AsyncLog.Count;

        view.Counter %= 5;
        Drain(s);

        // Give async time to run (it's synchronous in this test since Task.Run is fire-and-forget)
        Thread.Sleep(50);
        Assert.True(view.AsyncLog.Count > before);
    }
}
