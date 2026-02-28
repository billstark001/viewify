using Viewify.Base;
using Viewify.Core.Render;

namespace Viewify.Test;

// ── Helpers ──────────────────────────────────────────────────────────────────

internal class StatefulView : View
{
    [State(0)]    public IState<int>    IntState    = null!;
    [State("hi")] public IState<string> StringState = null!;
    public override View? Render() => null;
}

internal class FactoryView : View
{
    [State(factory: typeof(Int42Factory))] public IState<int> FactoredState = null!;
    public override View? Render() => null;
}

internal class Int42Factory : IDefaultValueFactory
{
    public object? Create() => 42;
}

// ── Tests ─────────────────────────────────────────────────────────────────────

/// <summary>Tests for IState and StateWithDispatch.</summary>
public class StateTests
{
    private static Scheduler MakeScheduler(View root)
    {
        var h = new RecordingHandler();
        return new Scheduler(root, h);
    }

    private static void Drain(Scheduler s, int max = 1000)
    {
        int i = 0;
        while (s.UnitTick() && ++i < max) { }
    }

    // ── ImmutableState ───────────────────────────────────────────────────────

    [Fact]
    public void ImmutableState_Get_ReturnsInitialValue()
    {
        var s = new ImmutableState<int>(7);
        Assert.Equal(7, s.Get());
    }

    [Fact]
    public void ImmutableState_Set_Throws()
    {
        var s = new ImmutableState<int>(0);
        Assert.Throws<InvalidOperationException>(() => s.Set(1));
    }

    [Fact]
    public void ImmutableState_Create_ReturnsCorrectGenericType()
    {
        var s = ImmutableState<int>.Create(typeof(string), "hello");
        Assert.Equal("hello", ((IState<string>)s).Get());
    }

    [Fact]
    public void ImmutableState_TildeOperator_GetsValue()
    {
        IState<int> s = new ImmutableState<int>(99);
        Assert.Equal(99, ~s);
    }

    // ── StateWithDispatch ────────────────────────────────────────────────────

    [Fact]
    public void StateWithDispatch_InitializesWithDefaultValue()
    {
        var view = new StatefulView();
        var s    = MakeScheduler(view);
        Drain(s);

        Assert.Equal(0,    ~view.IntState);
        Assert.Equal("hi", ~view.StringState);
    }

    [Fact]
    public void StateWithDispatch_FactoryDefaultValue()
    {
        var view = new FactoryView();
        var s    = MakeScheduler(view);
        Drain(s);

        Assert.Equal(42, ~view.FactoredState);
    }

    [Fact]
    public void StateWithDispatch_Set_UpdatesValueAfterDrain()
    {
        var view = new StatefulView();
        var s    = MakeScheduler(view);
        Drain(s);

        view.IntState %= 55;
        Drain(s);

        Assert.Equal(55, ~view.IntState);
    }

    [Fact]
    public void StateWithDispatch_PercentOperator_SetsThenReturnsState()
    {
        var view = new StatefulView();
        var s    = MakeScheduler(view);
        Drain(s);

        var returned = view.IntState % 77;
        Drain(s);

        Assert.Same(view.IntState, returned);
        Assert.Equal(77, ~view.IntState);
    }

    [Fact]
    public void StateWithDispatch_MultipleUpdates_LastOneWins()
    {
        var view = new StatefulView();
        var s    = MakeScheduler(view);
        Drain(s);

        view.IntState %= 1;
        view.IntState %= 2;
        view.IntState %= 3;
        Drain(s);

        Assert.Equal(3, ~view.IntState);
    }
}
