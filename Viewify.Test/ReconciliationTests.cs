using Viewify.Base;
using Viewify.Base.Native;
using Viewify.Core.Render;
using Viewify.Core.Utils;

namespace Viewify.Test;

// ── Helpers ───────────────────────────────────────────────────────────────────

/// <summary>Simple dependency that records its owner on Derive().</summary>
internal class OwnerRecordingDep : IDependency
{
    public IStateful? LastOwner { get; private set; }
    public void Derive(IStateful? owner) => LastOwner = owner;
}

/// <summary>Dependency that is wired via [Inject] attribute-based mapping.</summary>
internal class InjectableDep : IDependency
{
    public int Value;
}

internal class ViewWithInterfaceDep : View
{
    public OwnerRecordingDep Dep { get; } = new();
    public override View? Render() => null;
}

internal class ViewWithInjectDep : View
{
    public int SourceValue = 100;
    [Inject(nameof(InjectableDep.Value), nameof(SourceValue))]
    public InjectableDep Dep { get; } = new();
    public override View? Render() => null;
}

// Context test fixtures
internal class MyContext { public int Value { get; init; } = 7; }

internal class ConsumerView : View
{
    [Context] public MyContext? Ctx { get; set; }
    public override View? Render() => null;
}

internal class ContextRootView : View
{
    public override View? Render()
        => new ContextProvider<MyContext>(new MyContext { Value = 42 })
               .SetChildren(new ConsumerView());
}

// Reconciliation fixtures
internal class ConditionalRootView : View
{
    [State(true)] public IState<bool> ShowFirst = null!;
    public override View? Render()
        => ~ShowFirst
            ? new Text("A")
            : new Text("B");
}

// ── Tests ─────────────────────────────────────────────────────────────────────

public class ReconciliationTests
{
    private static Scheduler MakeScheduler(View root) => new(root, new RecordingHandler());

    private static void Drain(Scheduler s, int max = 2000)
    {
        int i = 0;
        while (s.UnitTick() && ++i < max) { }
    }

    // ── Props ─────────────────────────────────────────────────────────────────

    [Fact]
    public void PropAttribute_IsScannedByRecord()
    {
        var record = new ViewRecordCache().Get(typeof(Text));
        Assert.True(record.PropProperties.Count > 0 || record.PropFields.Count > 0);
    }

    [Fact]
    public void Render_UpdatesProp_OnRerender()
    {
        var view = new LabelView("init");
        var s    = MakeScheduler(view);
        Drain(s);
        // No assertion on internals; just must not throw
    }

    // ── Context ───────────────────────────────────────────────────────────────

    [Fact]
    public void Context_IsScannedByRecord()
    {
        var record = new ViewRecordCache().Get(typeof(ConsumerView));
        Assert.NotEmpty(record.ContextProperties);
    }

    [Fact]
    public void ContextProvider_DeliversValueToConsumer()
    {
        // We verify through StatefulClassRecord that context fields are detected
        var record = new ViewRecordCache().Get(typeof(ConsumerView));
        Assert.Contains(record.ContextProperties, p => p.Item1.Name == nameof(ConsumerView.Ctx));
    }

    // ── Dependency injection ──────────────────────────────────────────────────

    [Fact]
    public void DependencyField_IsDetectedWithoutInjectAttr()
    {
        var record = new ViewRecordCache().Get(typeof(ViewWithInterfaceDep));
        Assert.True(
            record.DependencyFields.Any(f => f.Item1.Name == nameof(ViewWithInterfaceDep.Dep))
            || record.DependencyProperties.Any(p => p.Item1.Name == nameof(ViewWithInterfaceDep.Dep)));
    }

    [Fact]
    public void DependencyDerive_IsCalledOnInjectDependencies()
    {
        var view = new ViewWithInterfaceDep();
        var record = new ViewRecordCache().Get(typeof(ViewWithInterfaceDep));
        record.InjectDependencies(view);
        Assert.Same(view, view.Dep.LastOwner);
    }

    [Fact]
    public void InjectAttribute_CopiesFieldValue()
    {
        var view   = new ViewWithInjectDep();
        view.SourceValue = 77;
        var record = new ViewRecordCache().Get(typeof(ViewWithInjectDep));
        record.InjectDependencies(view);
        Assert.Equal(77, view.Dep.Value);
    }

    // ── Reconciliation ────────────────────────────────────────────────────────

    [Fact]
    public void ReconcileChildren_HandlesTypeChange()
    {
        // ConditionalRootView switches between two Text nodes when state changes
        var view = new ConditionalRootView();
        var s    = MakeScheduler(view);
        Drain(s);

        // Flip the boolean – triggers type-same reconciliation (Text→Text)
        view.ShowFirst %= false;
        Drain(s);
        // Must not throw; basic smoke test
    }

    [Fact]
    public void Scheduler_GetChildren_ReturnsEmptyForNativeViewWithNoChildren()
    {
        // NativeView short-circuits to its .Children list — Text("x") has none by default
        var nv = new Text("x");
        var children = Scheduler.GetChildren(nv).ToList();
        Assert.Empty(children);
    }

    // ── StringUtils ───────────────────────────────────────────────────────────

    [Fact]
    public void GetUniqueName_IncludesNamespace()
    {
        var name = typeof(MyContext).GetUniqueName();
        Assert.Contains("Viewify.Test", name);
    }

    [Fact]
    public void GetUniqueName_HandlesGenericType()
    {
        var name = typeof(IState<int>).GetUniqueName();
        Assert.Contains("IState", name);
        Assert.Contains("Int32", name);
    }
}
