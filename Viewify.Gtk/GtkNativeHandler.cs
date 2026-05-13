using Gtk;
using Viewify.Base;
using Viewify.Base.Native;
using NativeText      = Viewify.Base.Native.Text;
using NativeInput     = Viewify.Base.Native.Input;
using NativeContainer = Viewify.Base.Native.Container;
using NativeSwitch    = Viewify.Base.Native.Switch;
using NativeImage     = Viewify.Base.Native.Image;
using GtkSwitch       = Gtk.Switch;
using GtkImage        = Gtk.Image;

namespace Viewify.Gtk;

/// <summary>
/// <b>GTK3 platform binding for Viewify.</b>
/// <para>
/// Maps Viewify native-view types to their GTK3 widget equivalents and
/// implements the cursor-based positioning protocol required by
/// <see cref="INativeHandler"/>.
/// </para>
///
/// <para><b>GTK version notes</b></para>
/// <list type="bullet">
///   <item>
///     This implementation targets <b>GTK3</b> via the
///     <c>GtkSharp 3.24.x</c> NuGet package.  The package provides
///     managed P/Invoke wrappers; the native <c>libgtk-3</c> shared
///     library must be present at runtime.
///   </item>
///   <item>
///     <b>GTK4</b> (a.k.a. <c>Gtk4Sharp</c>) renames several types and
///     removes deprecated APIs.  Key differences:
///     <list type="bullet">
///       <item><c>Container.Add</c> → <c>Widget.SetParent</c> /
///         layout-manager APIs</item>
///       <item><c>Box.PackStart</c> removed; use <c>Box.Append</c></item>
///       <item><c>Entry</c> signals renamed; <c>changed</c> →
///         <c>Gtk.Editable::changed</c></item>
///       <item><c>Image</c> → <c>Gtk.Picture</c> /
///         <c>Gtk.Image</c> with <c>IconName</c> property</item>
///       <item>Most widget constructors no longer accept initial values
///         directly; use property setters instead</item>
///     </list>
///   </item>
/// </list>
///
/// <para><b>Cursor model</b></para>
/// The handler tracks a (container, index) cursor that mirrors the
/// fiber tree traversal order.  Each <c>DescendCursor</c> pushes into
/// the first child container; <c>AscendCursor</c> pops back to the
/// parent; <c>AdvanceCursor</c> moves to the next sibling slot.
/// </summary>
public class GtkNativeHandler : INativeHandler
{
    /// <summary>Root container that hosts all top-level widgets.</summary>
    public Box RootContainer { get; }

    private readonly Stack<(Box Container, int Index)> _stack = new();
    private Box    _current;
    private int    _index;

    public GtkNativeHandler(Box rootContainer)
    {
        RootContainer = rootContainer;
        _current      = rootContainer;
        _index        = 0;
    }

    // ── Cursor navigation ────────────────────────────────────────────────────

    /// <inheritdoc/>
    public void ResetCursor(NativeView? v)
    {
        _stack.Clear();
        _current = v?.NativeObject as Box ?? RootContainer;
        _index   = 0;
    }

    /// <inheritdoc/>
    public void AdvanceCursor() => ++_index;

    /// <inheritdoc/>
    public void DescendCursor()
    {
        var pointed = GetPointed() as Box;
        if (pointed != null)
        {
            _stack.Push((_current, _index));
            _current = pointed;
            _index   = 0;
        }
    }

    /// <inheritdoc/>
    public void AscendCursor()
    {
        if (_stack.Count == 0)
        {
            _current = RootContainer;
            _index   = 0;
            return;
        }
        (_current, _index) = _stack.Pop();
    }

    /// <inheritdoc/>
    public object? GetPointed()
    {
        var children = _current.Children;
        return _index < children.Length ? children[_index] : null;
    }

    /// <inheritdoc/>
    public void BindReference(NativeView v)
        => throw new NotImplementedException(
            "Reference binding is not yet implemented for GTK.");

    // ── Mount / Update / Unmount / Move ──────────────────────────────────────

    /// <inheritdoc/>
    public void Mount(NativeView v)
    {
        switch (v)
        {
            case NativeText t:
            {
                var lbl = new Label(t.Value) { Halign = Align.Start };
                t.NativeObject = lbl;
                InsertAt(_current, lbl, _index);
                lbl.Show();
                break;
            }
            case NativeInput inp:
            {
                var entry = new Entry { Text = inp.Value };
                if (inp.OnChange is EventHandler h)
                    entry.Changed += (sender, _) => h(sender, EventArgs.Empty);
                inp.NativeObject = entry;
                InsertAt(_current, entry, _index);
                entry.Show();
                break;
            }
            case NativeContainer _:
            {
                var box = new Box(Orientation.Vertical, 0);
                v.NativeObject = box;
                InsertAt(_current, box, _index);
                box.Show();
                break;
            }
            case NativeSwitch sw:
            {
                var toggle = new GtkSwitch { Active = sw.Value };
                if (sw.OnChange is EventHandler h)
                    toggle.StateSet += (sender, _) => { h(sender, EventArgs.Empty); };
                sw.NativeObject = toggle;
                InsertAt(_current, toggle, _index);
                toggle.Show();
                break;
            }
            case NativeImage img:
            {
                // GTK3: Gtk.Image can load from a file path.
                // GTK4: Use Gtk.Picture for file-backed images.
                var gtkImg = string.IsNullOrEmpty(img.Source)
                    ? new GtkImage()
                    : new GtkImage(img.Source);
                img.NativeObject = gtkImg;
                InsertAt(_current, gtkImg, _index);
                gtkImg.Show();
                break;
            }
        }
    }

    /// <inheritdoc/>
    public void Update(NativeView v)
    {
        switch (v)
        {
            case NativeText t when t.NativeObject is Label lbl:
                lbl.Text = t.Value;
                break;
            case NativeInput inp when inp.NativeObject is Entry entry:
                if (entry.Text != inp.Value)
                    entry.Text = inp.Value;
                break;
            case NativeSwitch sw when sw.NativeObject is GtkSwitch toggle:
                if (toggle.Active != sw.Value)
                    toggle.Active = sw.Value;
                break;
            case NativeImage img when img.NativeObject is GtkImage gtkImg:
                if (!string.IsNullOrEmpty(img.Source))
                    gtkImg.File = img.Source;
                break;
        }
    }

    /// <inheritdoc/>
    public void Unmount(NativeView v)
    {
        if (v.NativeObject is Widget w)
        {
            (w.Parent as global::Gtk.Container)?.Remove(w);
            w.Destroy();
            v.NativeObject = null;
        }
    }

    /// <inheritdoc/>
    public void Move(NativeView v)
    {
        if (v.NativeObject is Widget w && w.Parent is Box box)
        {
            // GTK3 Box does not expose a reorder-at-index API for arbitrary
            // children; remove and re-insert at the new cursor position.
            box.Remove(w);
            InsertAt(box, w, _index);
        }
    }

    // ── Internal helpers ─────────────────────────────────────────────────────

    /// <summary>
    /// Inserts <paramref name="widget"/> into <paramref name="parent"/> at
    /// position <paramref name="index"/>.
    /// </summary>
    private static void InsertAt(Box parent, Widget widget, int index)
    {
        // GTK3 Box.PackStart appends; ReorderChild lets us position it.
        parent.PackStart(widget, expand: false, fill: false, padding: 0);
        parent.ReorderChild(widget, index);
    }
}
