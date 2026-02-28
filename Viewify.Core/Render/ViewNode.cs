using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Viewify.Base;
using Viewify.Core.Utils;

namespace Viewify.Core.Render;

public class ViewNode
{
    public override string ToString()
    {
        return $"ViewNode of {View?.GetType().Name}";
    }

    public View? View { get; private set; }
    public Scheduler Scheduler { get; init; }
    public StatefulClassRecord Record { get; init; }

    public ImmutableTreeHashTable<object> Context { get; private set; } = _defaultContext;

    private static readonly ImmutableTreeHashTable<object> _defaultContext = new();

    // TODO context record
    // TODO effect deps record

    private object?[] _effectDeps;
    private readonly bool[] _effectDepChanged;

    public ViewNode(View? view, Scheduler scheduler, ViewRecordCache cache)
    {
        View = view;
        Scheduler = scheduler;
        Record = cache.Get(view?.GetType() ?? typeof(View));
        _effectDeps = new object?[Record.EffectDepFields.Count + Record.EffectDepProperties.Count];
        _effectDepChanged = new bool[Record.EffectDepFields.Count + Record.EffectDepProperties.Count];
    }

    public void OnVisit(Fiber<ViewNode> newFiber, bool operative = false)
    {
        // assume this == newFiber.Content

        var oldFiber = newFiber.Alternate;

        if (newFiber.OperativeFibers != null)
        {
            foreach (var item in newFiber.OperativeFibers)
            {
                item.Content.OnVisit(item, true);
            }
        }

        switch (newFiber.Tag)
        {
            // TODO delay effects to execute after traverse
            case FiberTag.Create:
                OnMount(newFiber);
                break;
            case FiberTag.Update:
                OnUpdate(oldFiber!, newFiber);
                break;
            case FiberTag.Remove:
                OnUnmount(operative ? newFiber : oldFiber!);
                break;
            case FiberTag.Insert:
                OnMove(operative ? newFiber : oldFiber!);
                break;

            case FiberTag.Idle:
            default:
                break;
        }
    }

    public void UpdateContext(View? newView, Fiber<ViewNode>? parentFiber)
    {
        if (newView is ContextProvider cp)
        {
            var key = cp.Value?.GetType().GetUniqueName() ?? "";
            Context = new(parentFiber?.Content.Context, new Dictionary<string, object>
            {
                [key] = cp.Value!,
            });
        }
        else
        {
            Context = new(parentFiber?.Content.Context);
        }
    }

    public void OnInit(Fiber<ViewNode> newFiber, Fiber<ViewNode>? oldFiber, bool doInitStates = false)
    {
        // assert newFiber.content == this

        var oldView = oldFiber?.Content.View;
        var newView = newFiber.Content.View;

        if (newView == null)
        {
            return;
        }

        if (doInitStates)
        {
            // do nothing
        }
        else if (oldView == null)
        {
            Record.InitializeState(newFiber, Scheduler, true);
        }
        else
        {
            // these will be rewritten
            Record.MigrateStates(oldView, newView);
        }

        if (oldFiber != null)
        {
            Record.InitializeContext(newView, oldFiber.Content.Context);
        }

    }

    public void OnBeforeMount(Fiber<ViewNode>? oldFiber, Fiber<ViewNode> newFiber)
    {
        // assume newFiber.Content == this
        var oldView = oldFiber?.Content.View;
        var newView = newFiber.Content.View;

        // state
        // oldFiber must have different type of view
        // this makes state migration meaningless
        // but in this point the new fiber is not committed yet
        // so bounded states are irrational and useless
        // these are just to assure rendering
        Record.InitializeState(newFiber, Scheduler, true);

        // context
        UpdateContext(View, newFiber.Parent);
        Record.InitializeContext(View, Context);
    }

    public void OnMount(Fiber<ViewNode> newFiber)
    {
        // state
        // now this is the fully functional state
        Record.InitializeState(newFiber, Scheduler);

        if (View is NativeView nativeView)
        {
            nativeView.Mount(Scheduler.Handler);
        }

        // calc effect deps
        // execute mount, layout and async effects on first mount
        Record.CompareAndCalculateEffectDependencies(View, _effectDeps, _effectDepChanged);
        Record.ExecuteMountEffects(View);
        Record.ExecuteAsyncEffects(View, _effectDepChanged);

    }

    public void OnBeforeUpdate(Fiber<ViewNode>? oldFiber, Fiber<ViewNode> newFiber)
    {
        // assume newFiber.Content == this
        var oldView = oldFiber?.Content.View;
        var newView = newFiber.Content.View;

        // view itself
        newFiber.Content.View = oldView;
        // props of view
        Record.CompareAndMigrateProps(newView, oldView);
        // effect deps
        newFiber.Content._effectDeps = oldFiber?.Content._effectDeps ?? newFiber.Content._effectDeps;
        // context of view
        // context is essentially a sort of property
        // this makes this update rational
        UpdateContext(oldView, newFiber.Parent);
        Record.InitializeContext(oldView, Context);

        // Wire phase: run before-update effects
        var hasChange = Record.CompareAndCalculateEffectDependencies(oldView, _effectDeps, _effectDepChanged);
        if (hasChange)
        {
            Record.ExecuteWireEffects(oldView, _effectDepChanged);
        }
    }

    public void OnUpdate(Fiber<ViewNode> oldFiber, Fiber<ViewNode> newFiber)
    {
        // assume newFiber.Content == this
        // assume OnBeforeUpdate already executed
        // this is executed during the commit phase

        var oldView = oldFiber.Content.View;
        var newView = newFiber.Content.View;

        // state
        // this cannot be migrated before update
        // since OnBeforeUpdate is executed before committing
        // and the new fiber is not necessarily applied at that point
        Record.MigrateStateFiberNodes(oldView, newFiber);

        if (newFiber.Content.View is NativeView nativeView)
        {
            nativeView.Update(Scheduler.Handler);
        }

        // Layout phase: compare deps and run layout + async effects
        var hasChange = Record.CompareAndCalculateEffectDependencies(oldView, _effectDeps, _effectDepChanged);
        if (hasChange)
        {
            Record.ExecuteEffects(oldView, _effectDepChanged);
            Record.ExecuteAsyncEffects(oldView, _effectDepChanged);
        }
    }

    public void OnUnmount(Fiber<ViewNode> oldFiber)
    {
        // execute effects for case 3
        Record.ExecuteUnmountEffects(oldFiber.Content.View);

        if (oldFiber.Content.View is NativeView nativeView)
        {
            nativeView.Unmount(Scheduler.Handler);
        }
    }

    public void OnMove(Fiber<ViewNode> oldFiber)
    {
        if (oldFiber.Content.View is NativeView nativeView)
        {
            nativeView.Move(Scheduler.Handler);
        }
    }
}
