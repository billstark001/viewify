# Windows Forms Platform Binding Example

This document shows how to build a minimal **Windows Forms** host for Viewify.
It mirrors the code that previously lived in the removed `Viewify.Test.WinForm`
project.

---

## 1. Project setup

Create a WinForms app and reference `Viewify.Base` and `Viewify.Core`:

```xml
<!-- MyApp.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWindowsForms>true</UseWindowsForms>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Viewify.Base\Viewify.Base.csproj" />
    <ProjectReference Include="..\Viewify.Core\Viewify.Core.csproj" />
  </ItemGroup>
</Project>
```

---

## 2. Implementing `INativeHandler`

`INativeHandler` is the bridge between the Viewify fiber tree and the
WinForms control hierarchy.  The implementation below supports `Text`
(mapped to `Label`) and uses a cursor model to track position in the
`Control` tree.

```csharp
using Viewify.Base;
using Viewify.Base.Native;

public class WinFormsNativeHandler(Control baseControl) : INativeHandler
{
    public Control BaseControl    { get; init; }    = baseControl;
    public Control CurrentControl { get; private set; } = baseControl;
    public int     CurrentIndex   { get; private set; } = 0;

    // ── Cursor navigation ────────────────────────────────────────────────────

    public void ResetCursor(NativeView? v)
    {
        CurrentControl = v?.NativeObject as Control ?? BaseControl;
        CurrentIndex   = 0;
    }

    public void AdvanceCursor() => ++CurrentIndex;

    public void DescendCursor()
    {
        var target = GetPointed() as Control;
        if (target != null) { CurrentControl = target; CurrentIndex = 0; }
    }

    public void AscendCursor()
    {
        if (CurrentControl == BaseControl) { CurrentIndex = 0; return; }
        CurrentControl = CurrentControl.Parent!;
        CurrentIndex   = CurrentControl.Controls.GetChildIndex(CurrentControl);
    }

    public object? GetPointed()
        => CurrentControl.Controls.Count > CurrentIndex
            ? CurrentControl.Controls[CurrentIndex]
            : null;

    public void BindReference(NativeView v) => throw new NotImplementedException();

    // ── Component handlers ───────────────────────────────────────────────────

    public void Mount(NativeView v)
    {
        if (v is Text t)
        {
            var lbl = new Label { Text = t.Value };
            t.NativeObject = lbl;
            CurrentControl.Controls.Add(lbl);
        }
    }

    public void Update(NativeView v)
    {
        if (v is Text t && t.NativeObject is Label lbl)
            lbl.Text = t.Value;
    }

    public void Unmount(NativeView v)
    {
        if (v is Text t && t.NativeObject is Label lbl)
            CurrentControl.Controls.Remove(lbl);
    }

    public void Move(NativeView v)
    {
        if (v is Text t && t.NativeObject is Label lbl)
            CurrentControl.Controls.SetChildIndex(lbl, CurrentIndex);
    }
}
```

---

## 3. Defining views

```csharp
using Viewify.Base;
using Viewify.Base.Native;

// A simple context object shared between components
public class AppContext
{
    public int Count { get; init; }
}

// Child component: reads from context
public class CounterLabel : View
{
    [Context] public AppContext Ctx { get; set; } = new();
    public override View? Render() => new Text($"Count: {Ctx.Count}");
}

// Root component: owns state and provides context
public class CounterView(int initial) : View
{
    [Prop]   public int           Initial   = initial;
    [State(0)] public IState<int> Counter   = null!;

    public override View? Render()
        => new ContextProvider<AppContext>(new AppContext { Count = ~Counter })
               .SetChildren(new CounterLabel(), new CounterLabel());

    // Layout effect: runs every time Counter changes
    [LayoutEffect(nameof(Counter))]
    void OnCounterChanged()
        => Console.WriteLine($"Counter changed to {~Counter}");
}
```

---

## 4. Hosting in a Form

```csharp
using Viewify.Core.Render;

public partial class MainForm : Form
{
    Scheduler          _scheduler = null!;
    WinFormsNativeHandler _handler = null!;
    CounterView        _root    = null!;

    public MainForm() => InitializeComponent();

    private void MainForm_Load(object sender, EventArgs e)
    {
        _root      = new CounterView(0);
        _handler   = new WinFormsNativeHandler(flowLayoutPanel1);
        _scheduler = new Scheduler(_root, _handler);

        // Drive the scheduler on the Application.Idle event.
        // Application.Idle fires whenever the Win32 message queue is empty,
        // so the actual cadence depends on system load and message activity.
        Application.Idle += (_, _) => _scheduler.Tick();
    }

    // Button click → dispatch a state update
    private void btnIncrement_Click(object sender, EventArgs e)
        => _root.Counter %= ~_root.Counter + 1;
}
```

---

## 5. Program entry point

```csharp
internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
```

---

## How the scheduler loop works

| Phase | What happens |
|---|---|
| `Tick()` is called | Processes as many unit-ticks as fit in the time budget (default 8 ms) |
| Dispatch dequeued | State-update lambda runs; re-render root is set |
| Work-in-progress root created | New fiber tree built via `CreateWipRootIfNecessary` |
| Diff walk | `PerformDiffWorkAndGetNext` reconciles old and new trees fiber-by-fiber |
| Commit | `CommitWipRoot` traverses the WIP tree, calls `INativeHandler` methods, and executes effects |

Calling `Tick()` from `Application.Idle` ensures the UI thread is never
blocked: if the time budget is exhausted mid-diff, the remaining work is
picked up on the next idle event.
