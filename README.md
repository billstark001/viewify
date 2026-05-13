# Viewify

**Viewify** is a C# declarative UI framework inspired by React.  It implements
a component-based view system with state management, lifecycle effects,
context propagation, and dependency injection, backed by a fiber-based
reconciliation engine.  The framework is platform-agnostic: UI backends
(Windows Forms, GTK3, …) are plugged in through a thin `INativeHandler`
interface.

---

## Repository layout

```
viewify/
├── Viewify.Base/          # Public abstractions – no runtime dependencies
│   ├── View.cs            # Base component class
│   ├── NativeView.cs      # Base for platform-mapped widgets
│   ├── Fragment.cs        # Transparent grouping node
│   ├── ConditionView.cs   # Ternary conditional rendering
│   ├── ContextProvider.cs # Provides a typed value to descendant context consumers
│   ├── IState.cs          # State interfaces + operators
│   ├── IStateful.cs       # Marker interface (View, IDependency)
│   ├── IDependency.cs     # Dependency-injection contract
│   ├── EffectAttribute.cs # Lifecycle effect attributes (all five phases)
│   ├── PropAttribute.cs   # Marks component inputs
│   ├── StateAttribute.cs  # Declares reactive state with optional default/factory
│   ├── InjectAttribute.cs # Field/property-level DI mapping
│   ├── ContextAttribute.cs# Marks a field/property as a context consumer
│   ├── INativeHandler.cs  # Platform abstraction (cursor + widget lifecycle)
│   └── Native/            # Built-in native-view types
│       ├── Text.cs        # Display label
│       ├── Input.cs       # Text field
│       ├── Container.cs   # Layout container
│       ├── Image.cs       # Image widget
│       └── Switch.cs      # Toggle switch
├── Viewify.Core/          # Reconciliation engine
│   ├── Render/
│   │   ├── Fiber.cs                       # Fiber tree node + traversal
│   │   ├── Scheduler.cs                   # Tick-driven render loop + commit
│   │   ├── ViewNode.cs                    # Per-node lifecycle orchestration
│   │   ├── StatefulClassRecord.cs         # Reflection metadata cache per type
│   │   ├── StatefulClassRecordHandlers.cs # Metadata-driven operation helpers
│   │   ├── StateWithDispatch.cs           # Reactive state backed by scheduler dispatch
│   │   ├── ImmutableState.cs              # Read-only state (pre-commit temporary)
│   │   └── ViewRecordCache.cs             # LRU cache of StatefulClassRecord
│   └── Utils/
│       ├── AdaptiveMemoryCache.cs
│       ├── AsyncPriorityQueue.cs
│       ├── ImmutableTreeHashTable.cs
│       ├── ImmutableWrapper.cs
│       └── StringUtils.cs
├── Viewify.Gtk/           # GTK3 platform binding (GtkSharp 3.24.x)
├── Viewify.Test/          # xUnit test suite (net8.0)
└── docs/
    └── examples/
        ├── winforms.md    # Windows Forms example
        └── gtk.md         # GTK3 example
```

---

## Core concepts

### Components (`View`)

A component is a class that extends `View` and overrides `Render()`:

```csharp
public class Greeting(string name) : View
{
    [Prop] public string Name = name;

    public override View? Render()
        => new Text($"Hello, {Name}!");
}
```

### State

Reactive state is declared with `[State]`.  Reading uses the `~` operator;
writing uses the `%` operator, which enqueues a dispatch and schedules a
re-render:

```csharp
public class Counter : View
{
    [State(0)] public IState<int> Count = null!;

    public override View? Render()
        => new Container().SetChildren(
               new Text($"Count: {~Count}"),
               new Input(onChange: (_, _) => Count %= ~Count + 1));
}
```

### Props

Props are component inputs marked with `[Prop]`.  When the parent re-renders
the framework copies new prop values onto the existing component instance:

```csharp
public class Badge(int value) : View
{
    [Prop] public int Value = value;
    public override View? Render() => new Text(Value.ToString());
}
```

### Effects (five phases)

| Attribute | Phase | When it runs |
|---|---|---|
| `[MountEffect]` | **Mount** | Once, after the first commit |
| `[WireEffect(deps)]` | **Wire** | Before native-view update, when a dependency changes |
| `[LayoutEffect(deps)]` or `[Effect(deps)]` | **Layout** | After native-view update, when a dependency changes |
| `[UnmountEffect]` | **Unmount** | When the component is removed from the tree |
| `[AsyncEffect(deps)]` | **Async** | Fire-and-forget after layout, when a dependency changes |

`deps` is a list of field/property names in the same class.  Methods with
a `[LayoutEffect]`/`[WireEffect]`/`[AsyncEffect]` attribute are also run on
mount (initial snapshot).

```csharp
public class Logger : View
{
    [State(0)] public IState<int> Tick = null!;

    [MountEffect]
    void OnMount() => Console.WriteLine("Mounted");

    [LayoutEffect(nameof(Tick))]
    void OnTick() => Console.WriteLine($"Tick: {~Tick}");

    [AsyncEffect(nameof(Tick))]
    async void FetchData() { await Task.Delay(100); /* … */ }

    [UnmountEffect]
    void OnUnmount() => Console.WriteLine("Unmounted");

    public override View? Render() => null;
}
```

### Context

Use `ContextProvider<T>` to publish a value and `[Context]` to consume it:

```csharp
public class ThemeProvider : View
{
    public override View? Render()
        => new ContextProvider<Theme>(new Theme { IsDark = true })
               .SetChildren(new ThemedButton());
}

public class ThemedButton : View
{
    [Context] public Theme Theme { get; set; } = new();
    public override View? Render() => new Text(Theme.IsDark ? "dark" : "light");
}
```

### Dependency injection

Two patterns are supported:

**Interface inheritance** — implement `IDependency` and override
`Derive(IStateful? owner)` to pull values from the owner view:

```csharp
public interface IAnalytics : IDependency { void Track(string ev); }

public class ConsoleAnalytics : IAnalytics
{
    public void Derive(IStateful? owner) { /* inspect owner if needed */ }
    public void Track(string ev) => Console.WriteLine(ev);
}

public class MyView : View
{
    public IAnalytics Analytics = new ConsoleAnalytics();
    // framework calls Analytics.Derive(this) automatically
    …
}
```

**Field/property definition** — use `[Inject]` to copy a value from the
owner field to a field on the dependency:

```csharp
public class MyDep : IDependency { public int SharedValue; }

public class MyView : View
{
    public int ImportantValue = 42;
    [Inject(nameof(MyDep.SharedValue), nameof(ImportantValue))]
    public MyDep Dep = new();
}
```

### Conditional rendering

```csharp
View.Case(condition, trueView, falseView)
```

### List rendering

Wrap items in `View.Loop(…)` or a `Fragment(useKey: true)` to enable
key-based reconciliation:

```csharp
public override View? Render()
    => View.Loop(Items.Select(i => new ItemView(i).SetKey(i.Id)).ToArray());
```

---

## Building

```bash
dotnet build Viewify.sln
```

Targets **net6.0** and **net8.0** for `Viewify.Base`, `Viewify.Core`, and
`Viewify.Gtk`.  Tests run on **net8.0** only.

```bash
dotnet test Viewify.Test/Viewify.Test.csproj
```

---

## Platform bindings

| Package | Backend | Status |
|---|---|---|
| `Viewify.Gtk` | GTK3 via GtkSharp 3.24 | Included |
| Windows Forms | Example | [`docs/examples/winforms.md`](docs/examples/winforms.md) |

See [`docs/examples/gtk.md`](docs/examples/gtk.md) for the GTK3 quick-start.

---

## Engine overview

```
State update  (state % newValue)
      │
      ▼
Scheduler.Dispatch()        ← enqueues (fiber, action)
      │
      ▼  next Tick()
HandleDispatch()            ← applies action, sets _renderRoot
      │
      ▼
PerformDiffWorkAndGetNext() ← incremental fiber diff
  ReconcileChildren()
    OnBeforeUpdate / OnBeforeMount per node
      │
      ▼  diff complete
CommitWipRoot()             ← traverse WIP tree
  per node: OnMount / OnUpdate / OnUnmount / OnMove
    INativeHandler.Mount / Update / Unmount / Move
    Wire → Layout → Async effects
```

Each call to `Tick(budgetNs)` processes as many unit-ticks as fit in the
time budget, making it safe to drive from a UI-thread idle handler (e.g.
`Application.Idle` for WinForms, `GLib.Idle` for GTK).

---

## License

See [LICENSE.txt](LICENSE.txt).
