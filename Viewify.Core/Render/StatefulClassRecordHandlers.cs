using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Viewify.Base;
using Viewify.Core.Utils;

namespace Viewify.Core.Render;

public static class StatefulClassRecordHandlers
{
    // states
    public static void InitializeState(
        this StatefulClassRecord record,
        Fiber<ViewNode> node,
        Scheduler scheduler,
        bool temporary = false
    )
    {
        var instance = node.Content.View;

        foreach (var (field, type, defaultValue, factory) in record.StateFields)
        {
            var iv = factory?.Create() ?? defaultValue!;
            IState s = temporary
                ? ImmutableState<int>.Create(type, iv)
                : StateWithDispatch.Create(type, scheduler, node, iv);
            field.SetValue(instance, s);
        }

        foreach (var (property, type, defaultValue, factory) in record.StateProperties)
        {
            var iv = factory?.Create() ?? defaultValue!;
            IState s = temporary
                ? ImmutableState<int>.Create(type, iv)
                : StateWithDispatch.Create(type, scheduler, node, iv);
            property.SetValue(instance, s);
        }
    }

    public static void MigrateStateFiberNodes(this StatefulClassRecord record, IStateful? view, Fiber<ViewNode> fiber)
    {
        foreach (var (field, _, _, _) in record.StateFields)
        {
            var state = field.GetValue(view) as StateWithDispatch;
            if (state != null)
            {
                state.Fiber = fiber;
            }
        }

        foreach (var (property, _, _, _) in record.StateProperties)
        {
            var state = property.GetValue(view) as StateWithDispatch;
            if (state != null)
            {
                state.Fiber = fiber;
            }
        }
    }

    public static void MigrateStates(this StatefulClassRecord record, IStateful? source, IStateful? destination)
    {
        foreach (var (field, _, _, _) in record.StateFields)
        {
            if (field.GetValue(source) is IState sourceState)
            {
                field.SetValue(destination, sourceState);
            }
        }

        foreach (var (property, _, _, _) in record.StateProperties)
        {
            if (property.GetValue(source) is IState sourceState)
            {
                property.SetValue(destination, sourceState);
            }
        }
    }

    // props
    public static bool CompareAndMigrateProps(this StatefulClassRecord record, IStateful? source, IStateful? destination)
    {
        bool hasChange = false;

        foreach (var field in record.PropFields)
        {
            var oldValue = field.GetValue(source);
            var newValue = field.GetValue(destination);
            var neq = !Equals(oldValue, newValue);
            hasChange = hasChange || neq;
            if (neq)
            {
                field.SetValue(destination, oldValue);
            }
        }

        foreach (var property in record.PropProperties)
        {
            var oldValue = property.GetValue(source);
            var newValue = property.GetValue(destination);
            var neq = !Equals(oldValue, newValue);
            hasChange = hasChange || neq;
            if (neq)
            {
                property.SetValue(destination, oldValue);
            }
        }

        return hasChange;
    }

    // effect dependency comparison
    public static bool CompareAndCalculateEffectDependencies(this StatefulClassRecord record, IStateful? source, object?[] destination, bool[] changed)
    {
        bool hasChange = false;
        int i = 0;

        foreach (var (field, getter) in record.EffectDepFields)
        {
            var oldValue = destination[i];
            object? newValue = field.GetValue(source);
            if (getter != null) // this is an IState<>
            {
                newValue = getter.Invoke(newValue, null);
            }
            var neq = !Equals(oldValue, newValue);
            hasChange = hasChange || neq;
            changed[i] = neq;
            if (neq)
            {
                destination[i] = newValue;
            }
            ++i;
        }

        foreach (var (property, getter) in record.EffectDepProperties)
        {
            var oldValue = destination[i];
            object? newValue = property.GetValue(source);
            if (getter != null) // this is an IState<>
            {
                newValue = getter.Invoke(newValue, null);
            }
            var neq = !Equals(oldValue, newValue);
            hasChange = hasChange || neq;
            changed[i] = neq;
            if (neq)
            {
                destination[i] = newValue;
            }
            ++i;
        }

        return hasChange;
    }

    // ── Effect execution helpers ─────────────────────────────────────────────

    /// <summary>Executes all mount-phase effects on <paramref name="view"/>.</summary>
    public static void ExecuteMountEffects(this StatefulClassRecord record, IStateful? view)
    {
        foreach (var item in record.MountEffects)
        {
            item.Invoke(view, null);
        }
    }

    /// <summary>Executes all unmount-phase effects on <paramref name="view"/>.</summary>
    public static void ExecuteUnmountEffects(this StatefulClassRecord record, IStateful? view)
    {
        foreach (var item in record.UnmountEffects)
        {
            item.Invoke(view, null);
        }
    }

    /// <summary>
    /// Executes wire-phase effects whose dependencies have changed.
    /// Wire effects run <em>before</em> the commit (during reconciliation).
    /// </summary>
    public static void ExecuteWireEffects(this StatefulClassRecord record, IStateful? view, bool[] changed)
    {
        ExecuteDepEffects(record.WireEffects, record.EffectDepFields, record.EffectDepProperties, view, changed);
    }

    /// <summary>
    /// Executes layout-phase effects whose dependencies have changed.
    /// Layout effects run <em>after</em> native views have been updated.
    /// </summary>
    public static void ExecuteEffects(this StatefulClassRecord record, IStateful? view, bool[] changed)
    {
        ExecuteDepEffects(record.Effects, record.EffectDepFields, record.EffectDepProperties, view, changed);
    }

    /// <summary>
    /// Enqueues async-phase effects whose dependencies have changed.
    /// Each method is invoked; if the return value is a <see cref="Task"/> it
    /// is awaited via <c>Task.Run</c> so the caller is never blocked.
    /// </summary>
    public static void ExecuteAsyncEffects(this StatefulClassRecord record, IStateful? view, bool[] changed)
    {
        int i = 0;
        foreach (var (field, _) in record.EffectDepFields)
        {
            if (changed[i] && record.AsyncEffects.TryGetValue(field.Name, out var methods))
            {
                foreach (var m in methods)
                {
                    var result = m.Invoke(view, null);
                    if (result is Task t) Task.Run(() => t);
                }
            }
            ++i;
        }
        foreach (var (property, _) in record.EffectDepProperties)
        {
            if (changed[i] && record.AsyncEffects.TryGetValue(property.Name, out var methods))
            {
                foreach (var m in methods)
                {
                    var result = m.Invoke(view, null);
                    if (result is Task t) Task.Run(() => t);
                }
            }
            ++i;
        }
    }

    // shared helper for dep-keyed effect dictionaries
    private static void ExecuteDepEffects(
        IDictionary<string, IList<MethodInfo>> effectDict,
        IList<(FieldInfo, MethodInfo?)> depFields,
        IList<(PropertyInfo, MethodInfo?)> depProperties,
        IStateful? view,
        bool[] changed)
    {
        int i = 0;

        foreach (var (field, _) in depFields)
        {
            if (changed[i] && effectDict.TryGetValue(field.Name, out var methods))
            {
                foreach (var item in methods)
                {
                    item.Invoke(view, null);
                }
            }
            ++i;
        }

        foreach (var (property, _) in depProperties)
        {
            if (changed[i] && effectDict.TryGetValue(property.Name, out var methods))
            {
                foreach (var item in methods)
                {
                    item.Invoke(view, null);
                }
            }
            ++i;
        }
    }

    // context
    public static void InitializeContext(this StatefulClassRecord record, IStateful? view, ImmutableTreeHashTable<object> context)
    {
        foreach (var (field, key) in record.ContextFields)
        {
            var val = context.Get(key);
            if (val != null && field.FieldType.IsAssignableFrom(val.GetType()))
            {
                field.SetValue(view, val);
            }
        }
        foreach (var (property, key) in record.ContextProperties)
        {
            var val = context.Get(key);
            if (val != null && property.PropertyType.IsAssignableFrom(val.GetType()))
            {
                property.SetValue(view, val);
            }
        }
    }

    // ── Dependency injection ─────────────────────────────────────────────────

    static void InjectDependency(
        this StatefulClassRecord record,
        IStateful? view,
        IStateful? dependency,
        IEnumerable<(
            Func<object?, object?>,
            Action<object?, object?>
            )> methods
        )
    {
        if (view == null || dependency == null)
        {
            return;
        }
        foreach (var (get, set) in methods)
        {
            set(dependency, get(view));
        }
    }

    /// <summary>
    /// Injects all <see cref="IDependency"/> fields/properties of <paramref name="view"/>.
    /// <list type="bullet">
    ///   <item>If <c>[Inject]</c> attributes are present the specified
    ///     property mappings are copied first (field/property definition pattern).</item>
    ///   <item>Afterwards <see cref="IDependency.Derive"/> is always called so
    ///     the dependency can derive values from its owner directly
    ///     (interface inheritance pattern).</item>
    /// </list>
    /// </summary>
    public static void InjectDependencies(this StatefulClassRecord record, IStateful? view)
    {
        foreach (var (f, methods) in record.DependencyFields)
        {
            var dep = f.GetValue(view);
            record.InjectDependency(view, dep as IStateful, methods);
            (dep as IDependency)?.Derive(view);
        }
        foreach (var (p, methods) in record.DependencyProperties)
        {
            var dep = p.GetValue(view);
            record.InjectDependency(view, dep as IStateful, methods);
            (dep as IDependency)?.Derive(view);
        }
    }
}

