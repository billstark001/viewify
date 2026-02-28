using System.Text;
using Viewify.Core.Render;

namespace Viewify.Test;

/// <summary>Tests for the Fiber tree data structure.</summary>
public class FiberTests
{
    // Minimal content type
    record Payload(string Name);

    static Fiber<Payload> MakeFiber(string name) => new(new Payload(name));

    // ── Tree navigation ──────────────────────────────────────────────────────

    [Fact]
    public void Next_ReturnsChild_WhenChildExists()
    {
        var parent = MakeFiber("p");
        var child  = MakeFiber("c");
        parent.Child = child;

        var next = parent.Next(lastIsParent: false, out var type);

        Assert.Equal(child, next);
        Assert.Equal(FiberNextType.Child, type);
    }

    [Fact]
    public void Next_ReturnsSibling_WhenNoChild()
    {
        var a = MakeFiber("a");
        var b = MakeFiber("b");
        a.Sibling = b;

        var next = a.Next(lastIsParent: false, out var type);

        Assert.Equal(b, next);
        Assert.Equal(FiberNextType.Sibling, type);
    }

    [Fact]
    public void Next_ReturnsParent_WhenNoChildOrSibling()
    {
        var parent = MakeFiber("p");
        var child  = MakeFiber("c");
        child.Parent = parent;

        var next = child.Next(lastIsParent: false, out var type);

        Assert.Equal(parent, next);
        Assert.Equal(FiberNextType.Parent, type);
    }

    [Fact]
    public void Next_SkipsChild_WhenLastIsParent()
    {
        var parent  = MakeFiber("p");
        var child   = MakeFiber("c");
        var sibling = MakeFiber("s");
        parent.Child   = child;
        parent.Sibling = sibling;

        // pretend we came up from a child, so we should skip child traversal
        var next = parent.Next(lastIsParent: true, out var type);

        Assert.Equal(sibling, next);
        Assert.Equal(FiberNextType.Sibling, type);
    }

    // ── Detach ───────────────────────────────────────────────────────────────

    [Fact]
    public void Detach_ClearsAlternateTagAndOperatives()
    {
        var a = MakeFiber("a");
        var b = MakeFiber("b");
        a.Alternate = b;
        a.Tag       = FiberTag.Update;
        a.AddOperativeFiber(b);

        a.Detach();

        Assert.Null(a.Alternate);
        Assert.Null(a.Tag);
        Assert.Null(a.OperativeFibers);
    }

    // ── OperativeFibers ──────────────────────────────────────────────────────

    [Fact]
    public void AddOperativeFiber_CreatesListOnFirstAdd()
    {
        var a = MakeFiber("a");
        var b = MakeFiber("b");

        Assert.Null(a.OperativeFibers);

        a.AddOperativeFiber(b);

        Assert.NotNull(a.OperativeFibers);
        Assert.Single(a.OperativeFibers);
        Assert.Equal(b, a.OperativeFibers![0]);
    }

    // ── PrintTree ────────────────────────────────────────────────────────────

    [Fact]
    public void PrintTree_ContainsContentString()
    {
        var root  = MakeFiber("root");
        var child = MakeFiber("child");
        root.Child = child;
        child.Parent = root;

        var output = root.PrintTree();

        Assert.Contains("root", output);
        Assert.Contains("child", output);
    }

    // ── Tilde operator ───────────────────────────────────────────────────────

    [Fact]
    public void TildeOperator_ReturnsContent()
    {
        var f = MakeFiber("x");
        Assert.Equal(new Payload("x"), ~f);
    }
}
