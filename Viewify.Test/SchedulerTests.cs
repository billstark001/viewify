using Viewify.Base;
using Viewify.Base.Native;
using Viewify.Core.Render;

namespace Viewify.Test;

// ── Helpers ──────────────────────────────────────────────────────────────────

/// <summary>Minimal INativeHandler that records calls.</summary>
internal class RecordingHandler : INativeHandler
{
    public List<string> Log { get; } = new();

    public void ResetCursor(NativeView? v)  => Log.Add($"Reset:{v?.GetType().Name ?? "null"}");
    public void AscendCursor()              => Log.Add("Ascend");
    public void DescendCursor()             => Log.Add("Descend");
    public void AdvanceCursor()             => Log.Add("Advance");
    public object? GetPointed()             => null;
    public void BindReference(NativeView v) => Log.Add($"BindRef:{v.GetType().Name}");
    public void Mount(NativeView v)         => Log.Add($"Mount:{v.GetType().Name}");
    public void Update(NativeView v)        => Log.Add($"Update:{v.GetType().Name}");
    public void Unmount(NativeView v)       => Log.Add($"Unmount:{v.GetType().Name}");
    public void Move(NativeView v)          => Log.Add($"Move:{v.GetType().Name}");
}

/// <summary>Simple view that renders a single Text node.</summary>
internal class LabelView(string text) : View
{
    [Prop] public string Text { get; } = text;
    public override View? Render() => new Text(Text);
}

/// <summary>View that renders nothing (returns null).</summary>
internal class EmptyView : View
{
    public override View? Render() => null;
}

// ── Tests ─────────────────────────────────────────────────────────────────────

/// <summary>Integration-level tests for the Scheduler.</summary>
public class SchedulerTests
{
    private static (Scheduler, RecordingHandler) MakeScheduler(View root)
    {
        var h = new RecordingHandler();
        var s = new Scheduler(root, h);
        return (s, h);
    }

    [Fact]
    public void Constructor_DoesNotThrow_ForSimpleView()
    {
        var ex = Record.Exception(() =>
        {
            var (s, _) = MakeScheduler(new EmptyView());
            DrainScheduler(s);
        });
        Assert.Null(ex);
    }

    [Fact]
    public void Tick_ProcessesInitialRender()
    {
        var (s, h) = MakeScheduler(new LabelView("hello"));
        DrainScheduler(s);
        // The handler should have received a Mount call for the Text node
        Assert.Contains(h.Log, l => l.StartsWith("Mount:"));
    }

    [Fact]
    public void UnitTick_ReturnsFalse_WhenNothingToDo()
    {
        var (s, _) = MakeScheduler(new EmptyView());
        DrainScheduler(s);
        // After draining, there should be no more work
        Assert.False(s.UnitTick());
    }

    [Fact]
    public void GetChildren_ReturnsRenderedView_ForNonNative()
    {
        var label = new LabelView("hi");
        var children = Scheduler.GetChildren(label).ToList();
        Assert.Single(children);
        Assert.IsType<Text>(children[0]);
    }

    [Fact]
    public void GetChildren_ReturnsDirectChildren_ForFragment()
    {
        var fragment = new Fragment().SetChildren(new EmptyView(), new EmptyView());
        var children = Scheduler.GetChildren(fragment).ToList();
        Assert.Equal(2, children.Count);
    }

    [Fact]
    public void GetChildren_ReturnsEmpty_ForNull()
    {
        Assert.Empty(Scheduler.GetChildren(null));
    }

    [Fact]
    public void Dispatch_QueuedAction_IsExecuted()
    {
        var view = new CounterView();
        var (s, _) = MakeScheduler(view);
        DrainScheduler(s);

        // Trigger a state change via the view's state
        view.Counter %= 99;
        DrainScheduler(s);

        // After drain the scheduler must have processed the dispatch without throwing
    }

    // helpers
    private static void DrainScheduler(Scheduler s, int maxTicks = 1000)
    {
        int i = 0;
        while (s.UnitTick() && ++i < maxTicks) { }
    }
}

// ── Auxiliary view types ──────────────────────────────────────────────────────

internal class CounterView : View
{
    [State(0)] public IState<int> Counter = null!;
    public override View? Render() => null;
}
