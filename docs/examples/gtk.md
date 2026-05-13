# GTK3 Platform Binding (`Viewify.Gtk`)

`Viewify.Gtk` provides a GTK3 host for Viewify using the
[GtkSharp](https://github.com/GtkSharp/GtkSharp) NuGet package
(`GtkSharp 3.24.x`).

---

## Prerequisites

| Platform | Command |
|---|---|
| Debian/Ubuntu | `sudo apt install libgtk-3-0` |
| Fedora/RHEL   | `sudo dnf install gtk3` |
| macOS         | `brew install gtk+3` |
| Windows       | Use the `GtkSharp.Dependencies` NuGet package or install GTK3 via MSYS2 |

---

## Supported components

| Viewify type | GTK3 widget | Update API |
|---|---|---|
| `Text`      | `Gtk.Label`  | `Label.Text` |
| `Input`     | `Gtk.Entry`  | `Entry.Text` + `Changed` event |
| `Container` | `Gtk.Box` (vertical) | — |
| `Switch`    | `Gtk.Switch` | `Switch.Active` + `StateSet` event |
| `Image`     | `Gtk.Image`  | `Image.File` |

---

## GTK3 vs GTK4 differences

The binding targets **GTK3**.  Porting to GTK4 (`Gtk4Sharp`) requires:

| Area | GTK3 | GTK4 |
|---|---|---|
| Adding children | `Box.PackStart` + `Box.ReorderChild` | `Box.Append` + `Box.ReorderChildAfter` |
| Removing children | `Container.Remove` | `Widget.Unparent` |
| Images from files | `new Gtk.Image(path)` | `new Gtk.Picture(Gio.File.NewForPath(path))` |
| Destroying widgets | `Widget.Destroy()` | `Widget.Unparent()` (no explicit destroy) |

---

## Quick-start example

```csharp
using Gtk;
using Viewify.Base;
using Viewify.Base.Native;
using Viewify.Core.Render;
using Viewify.Gtk;

// --- View definition --------------------------------------------------------

public class ClickCounterView : View
{
    [State(0)] public IState<int> Clicks = null!;

    public override View? Render()
        => new Container().SetChildren(
               new Text($"Clicks: {~Clicks}"),
               new Input(value: "Click me",
                         onChange: (_, _) => Clicks %= ~Clicks + 1));
}

// --- Application entry point ------------------------------------------------

class Program
{
    static void Main(string[] args)
    {
        Application.Init();

        var win    = new Window("Viewify GTK Demo");
        var box    = new Box(Orientation.Vertical, 4);
        var root   = new ClickCounterView();
        var handler = new GtkNativeHandler(box);
        var sched  = new Scheduler(root, handler);

        win.Add(box);
        win.ShowAll();
        win.DeleteEvent += (_, _) => Application.Quit();

        // Drive the Viewify scheduler from the GLib main-loop idle handler
        GLib.Idle.Add(() => { sched.Tick(); return true; });

        Application.Run();
    }
}
```

---

## Notes on `INativeHandler` cursor model

GTK3 does not provide a positional index when iterating `Box` children.
`GtkNativeHandler` maintains its own `(Box container, int index)` stack:

```
RootBox
├─ [0] Label   ← cursor at index 0
├─ [1] Entry   ← AdvanceCursor() → index 1
└─ [2] Box     ← DescendCursor() enters this Box
      ├─ [0] ...
```

`InsertAt` appends a widget via `PackStart` and then calls `ReorderChild`
to move it to the desired position, keeping the physical child order in
sync with the fiber tree order.
