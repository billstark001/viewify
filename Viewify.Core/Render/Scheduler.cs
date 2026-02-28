using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using Viewify.Base;
using Viewify.Core.Utils;

namespace Viewify.Core.Render;


public sealed class Scheduler
{

    private readonly ConcurrentQueue<(Fiber<ViewNode>, Action)> _dispatchQueue = new();
    private readonly Dictionary<object, Fiber<ViewNode>> _dict = [];
    private readonly ViewRecordCache _recordCache = new();

    private Fiber<ViewNode> _root;
    private Fiber<ViewNode>? _renderRoot;
    private Fiber<ViewNode>? _wipRoot;
    private Fiber<ViewNode>? _current;

    public INativeHandler Handler { get; init; }


    public Scheduler(View rootNode, INativeHandler handler)
    {
        Handler = handler;
        _root = CreateFiber(rootNode);
        _root.Content.OnBeforeMount(null, _root);
        _root.Content.OnMount(_root);

        // render initial view
        _renderRoot = _root;
        CreateWipRootIfNecessary();
    }

    private Fiber<ViewNode> CreateFiber(View? view)
    {
        return new(new(view, this, _recordCache));
    }


    public void Tick(int max100Nanoseconds = 80000)
    {

        // if a dispatch is scheduled, prioritize it

        // unit: 1e-7s
        var startTicks = DateTime.UtcNow.Ticks;
        long tickSpan = 0;
        bool hasTasks = true;

        while (hasTasks && tickSpan < max100Nanoseconds)
        {
            hasTasks = UnitTick();
            tickSpan = DateTime.UtcNow.Ticks - startTicks;
        }
    }

    public bool UnitTick()
    {
        // precedence: 
        // - handle dispatch
        // - commit work
        // - create work
        // - diffing & reconciliation

        if (_dispatchQueue.TryDequeue(out var d))
        {
            HandleDispatch(d);
        }
        else if (_wipRoot == null)
        {
            return CreateWipRootIfNecessary();
        }
        else
        {
            if (_current == null)
            {
                // diffing is complete, commit
                CommitWipRoot();
            }
            else
            {
                _current = PerformDiffWorkAndGetNext(_current);
            }
        }

        return true;
    }

    public void Dispatch(Fiber<ViewNode> node, Action action)
    {
        _dispatchQueue.Enqueue((node, action));
    }

    public bool CreateWipRootIfNecessary()
    {
        if (_renderRoot == null)
        {
            return false;
        }

        var v = _renderRoot.Content.View;

        _wipRoot = CreateFiber(_renderRoot.Content.View);
        _wipRoot.Key = v?.Key;
        _wipRoot.Alternate = _renderRoot;
        _wipRoot.Parent = _renderRoot.Parent;
        _wipRoot.Tag = FiberTag.Update;

        _wipRoot.Content.OnBeforeUpdate(_renderRoot, _wipRoot);

        _current = _wipRoot;

        return true;

    }

    public void HandleDispatch((Fiber<ViewNode>, Action) t)
    {
        var (f, a) = t;
        a();

        // halt the current render
        _wipRoot = null;
        _current = null;

        // assign render root
        // if one already exists, check ancestry and use the higher one
        var renderRootFound = false;
        var current = f;
        while (_renderRoot != null && current != null)
        {
            if (current == _renderRoot)
            {
                renderRootFound = true;
                break;
            }
            current = current.Parent;
        }

        if (!renderRootFound)
        {
            _renderRoot = f;
        }
    }

    public static IEnumerable<View> GetChildren(View? v)
    {
        if (v == null)
        {
            yield break;
        }
        if (v is NativeView || v is Fragment)
        {
            if (v.Children != null)
            {
                foreach (var child in v.Children)
                {
                    yield return child;
                }
            }
            yield break;
        }
        var r = v.Render();
        if (r == null)
        {
            yield break;
        }
        yield return r;
    }

    public Fiber<ViewNode>? PerformDiffWorkAndGetNext(Fiber<ViewNode> current)
    {

        // TODO add necessary hooks
        var currentView = current.Content.View;
        var children = GetChildren(currentView);
        ReconcileChildren(current, children, currentView is Fragment f && f.UseKey);

        var isParent = false;
        var ret = current.Next(isParent, out var type);
        isParent = type == FiberNextType.Parent;
        while (ret != null && isParent)
        {
            ret = ret.Next(isParent, out type);
            isParent = type == FiberNextType.Parent;
        }
        return ret;
    }

    public void ReconcileChildren(Fiber<ViewNode> v, IEnumerable<View> children, bool useKey = false)
    {
        // old fiber node

        var oldFiber = v.Alternate?.Child;
        if (useKey)
        {
            // create key records
            // oldFiber turns into the first fiber without key
            _dict.Clear();
            var currentFiber = oldFiber;
            var oldFiberSet = false;
            while (currentFiber != null)
            {
                if (currentFiber.Key != null)
                {
                    _dict[currentFiber.Key] = currentFiber;
                }
                else if (!oldFiberSet)
                {
                    oldFiber = currentFiber;
                    oldFiberSet = true;
                }
                currentFiber = currentFiber.Sibling;
            }
            if (!oldFiberSet)
            {
                oldFiber = null;
            }
        }

        // new views to create fiber nodes
        // note: null is a valid view type, 
        // so check the exhaustion of children by newViewExists
        var _children = children.GetEnumerator();
        var newViewExists = _children.MoveNext();
        var newView = newViewExists ? _children.Current : null;

        Fiber<ViewNode> prevNewFiber = v;

        var initial = true;

        while (oldFiber != null || newViewExists)
        {

            var newKey = newView?.Key;
            Fiber<ViewNode>? _f = null;
            var useKeyAndHasNewKey = useKey && newKey != null;
            var withKeyedOldView = useKeyAndHasNewKey && _dict.TryGetValue(newKey!, out _f);
            var logicalOldFiber = useKeyAndHasNewKey ? _f : oldFiber;

            // note: if the new view has a key and key is used,
            // logicalOldFiber is either the one with exact key match or null,
            // otherwise it is just the "physical" old fiber.

            var logicalOldView = logicalOldFiber?.Content.View;
            var viewHasSameType = logicalOldView?.GetType() == newView?.GetType();

            // used in arrayed fragments

            Fiber<ViewNode>? newFiber = null;

            if (viewHasSameType)
            {
                newFiber = CreateFiber(newView);

                newFiber.Parent = v;
                newFiber.Alternate = logicalOldFiber;
                newFiber.Tag = FiberTag.Update;

                if (withKeyedOldView)
                {
                    logicalOldFiber!.Tag = FiberTag.Insert;
                    newFiber.AddOperativeFiber(logicalOldFiber);
                }
                if (useKey && newKey == null)
                {
                    // TODO
                    // warn the user to add a key prop
                }
                newFiber.Content.OnBeforeUpdate(oldFiber, newFiber);
            }
            else
            {
                // remove if exists & add
                if (newViewExists)
                {
                    newFiber = CreateFiber(newView);
                    newFiber.Parent = v;
                    newFiber.Tag = FiberTag.Create;
                    if (useKey)
                    {
                        newFiber.Key = newKey;
                    }
                    newFiber.Content.OnBeforeMount(logicalOldFiber, newFiber);
                }
                if (logicalOldFiber != null)
                {
                    logicalOldFiber.Tag = FiberTag.Remove;
                    (newFiber ?? prevNewFiber).AddOperativeFiber(logicalOldFiber);
                }
            }


            // commit change and chain all fibers together
            if (initial)
            {
                v.Child = newFiber;
                initial = false;
            }
            else
            {
                prevNewFiber.Sibling = newFiber;
            }

            // replace prevNewFiber
            if (newFiber != null)
            {
                // prevNewFiber is still needed in null cases
                // since it is useful to record operative fibers
                prevNewFiber = newFiber;
            }

            // move the cursor
            // oldFiber
            oldFiber = oldFiber?.Sibling;
            while (useKey && oldFiber?.Key != null)
            {
                // find the next oldFiber node without a key, or null
                oldFiber = oldFiber.Sibling;
            }

            // newView
            newViewExists = _children.MoveNext();
            newView = newViewExists ? _children.Current : null;
        }

    }


    public void CommitWipRoot()
    {
        var currentFiber = _wipRoot;
        var currentAlreadyVisited = false;

        // calibrate the cursor
        // find the nearest parent native view
        // if nothing found, find the "body" container
        NativeView? parentNativeView = null;
        var cursorCurrentFiber = currentFiber;

        while (cursorCurrentFiber != null)
        {
            if (cursorCurrentFiber.Content.View is NativeView _n)
            {
                parentNativeView = _n;
                break;
            }
            cursorCurrentFiber = cursorCurrentFiber.Parent;
        }
        Handler.ResetCursor(parentNativeView);

        while (currentFiber != null && currentFiber != _wipRoot?.Parent)
        {
            if (!currentAlreadyVisited)
            {
                currentFiber.Content.OnVisit(currentFiber);
                currentFiber.Detach();
            }

            var nextFiber = currentFiber.Next(currentAlreadyVisited, out var nextType);
            currentAlreadyVisited = nextType == FiberNextType.Parent;

            if (currentFiber.Content.View is NativeView nativeView)
            {
                if (nextType == FiberNextType.Child)
                {
                    Handler.DescendCursor();
                }
                else if (nextType == FiberNextType.Sibling)
                {
                    Handler.AdvanceCursor();
                }
            }
            if (nextType == FiberNextType.Parent
                && nextFiber?.Content.View is NativeView)
            {
                Handler.AscendCursor();
            }
            currentFiber = nextFiber;
        }

        // replace the old node in-place
        if (_wipRoot!.Parent == null)
        {
            _root = _wipRoot;
        }
        else
        {
            var cursor = _wipRoot.Parent.Child;
            Fiber<ViewNode>? prev = null;
            bool found = false;
            while (cursor != null)
            {
                if (cursor == _renderRoot)
                {
                    found = true;
                    if (prev == null)
                    {
                        _wipRoot.Parent.Child = _wipRoot;
                    }
                    else
                    {
                        prev.Sibling = _wipRoot;
                    }
                    _wipRoot.Sibling = cursor.Sibling;
                    break;
                }

                prev = cursor;
                cursor = cursor.Sibling;
            }
            if (!found)
            {
                throw new InvalidDataException("Render root not found in parent's child list.");
            }
        }

        // mark state
        _wipRoot = null;
        _renderRoot = null;
        _current = null;
    }
}
