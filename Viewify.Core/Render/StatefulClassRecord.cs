using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Viewify.Base;
using Viewify.Core.Utils;

namespace Viewify.Core.Render;

public class StatefulClassRecord
{

    const BindingFlags F = BindingFlags.Public |
                           BindingFlags.NonPublic |
                           // BindingFlags.Static |
                           // BindingFlags.FlattenHierarchy |
                           BindingFlags.Instance;
    public Type ClassType { get; }

    public IList<(FieldInfo, IEnumerable<(Func<object?, object?>, Action<object?, object?>)>)> DependencyFields { get; }
    public IList<(PropertyInfo, IEnumerable<(Func<object?, object?>, Action<object?, object?>)>)> DependencyProperties { get; }

    public IList<FieldInfo> PropFields { get; }
    public IList<PropertyInfo> PropProperties { get; }

    public IList<(FieldInfo, Type, object?, IDefaultValueFactory?)> StateFields { get; }
    public IList<(PropertyInfo, Type, object?, IDefaultValueFactory?)> StateProperties { get; }

    public IList<(FieldInfo, string)> ContextFields { get; }
    public IList<(PropertyInfo, string)> ContextProperties { get; }

    // ── Effect phase collections ─────────────────────────────────────────────

    /// <summary>Mount phase: run once, immediately after the first commit.</summary>
    public IList<MethodInfo> MountEffects { get; }

    /// <summary>Unmount phase: run when the component leaves the tree.</summary>
    public IList<MethodInfo> UnmountEffects { get; }

    /// <summary>
    /// Wire phase (before update): run during reconciliation, keyed by
    /// dependency name → methods to execute when that dependency changes.
    /// </summary>
    public IDictionary<string, IList<MethodInfo>> WireEffects { get; }

    /// <summary>
    /// Layout phase (after commit): run after native views have been updated,
    /// keyed by dependency name → methods to execute when that dependency changes.
    /// </summary>
    public IDictionary<string, IList<MethodInfo>> Effects { get; }

    /// <summary>
    /// Async phase: run asynchronously after all layout effects, keyed by
    /// dependency name → methods to execute when that dependency changes.
    /// </summary>
    public IDictionary<string, IList<MethodInfo>> AsyncEffects { get; }

    // Dependency tracking arrays – built from the union of all dep-keyed effects
    public IList<(FieldInfo, MethodInfo?)> EffectDepFields { get; }
    public IList<(PropertyInfo, MethodInfo?)> EffectDepProperties { get; }

    const string STATE_GET = nameof(IState<int>.Get);
    const string STATE_SET = nameof(IState<int>.Set);


    static bool IsValidStateDefinition(Type t)
    {
        return t.IsAssignableTo(typeof(IState))
            && t.IsGenericType
            && t.GetGenericTypeDefinition() == typeof(IState<>);
    }

    public StatefulClassRecord(Type type)
    {
        if (!type.IsAssignableTo(typeof(IStateful)))
        {
            throw new InvalidOperationException();
        }

        ClassType = type;

        var fDeps = new List<(FieldInfo, IEnumerable<(Func<object?, object?>, Action<object?, object?>)>)>();

        var pDeps = new List<(PropertyInfo, IEnumerable<(Func<object?, object?>, Action<object?, object?>)>)>();

        var fProps = new List<FieldInfo>();

        var pProps = new List<PropertyInfo>();

        var fStates = new List<(FieldInfo, Type, object?, IDefaultValueFactory?)>();

        var pStates = new List<(PropertyInfo, Type, object?, IDefaultValueFactory?)>();

        var fContexts = new List<(FieldInfo, string)>();

        var pContexts = new List<(PropertyInfo, string)>();


        PropAttribute? propFlag;
        StateAttribute? stateFlag1;
        ContextAttribute? contextFlag;
        List<(Func<object?, object?>, Action<object?, object?>)> injectMethods = new();

        bool isValidState;
        bool isDependency;

        void setFlags(Attribute a, Type? t)
        {
            if (a is PropAttribute ap)
            {
                propFlag = ap;
            }
            if (a is StateAttribute ads)
            {
                stateFlag1 = ads;
            }
            if (a is ContextAttribute ac)
            {
                contextFlag = ac;
            }
            if (a is InjectAttribute ai)
            {
                var _f = ai.Source != null ? ClassType.GetField(ai.Source) : null;
                var _ft = t?.GetField(ai.Prop);
                Func<object?, object?>? getter = _f != null ? _f.GetValue : null;
                Action<object?, object?>? setter = _ft != null ? _ft.SetValue : null;
                if (setter != null && getter != null)
                {
                    injectMethods.Add((getter, setter));
                }
            }
        }

        foreach (var f in ClassType.GetFields(F))
        {
            propFlag = null;
            stateFlag1 = null;
            contextFlag = null;
            injectMethods.Clear();
            isValidState = IsValidStateDefinition(f.FieldType);
            isDependency = f.FieldType.IsAssignableTo(typeof(IDependency));

            foreach (var a in f.GetCustomAttributes())
            {
                setFlags(a, f.FieldType);
            }

            if (propFlag != null)
            {
                fProps.Add(f);
            }
            else if (isValidState)
            {
                var genericArgument = f.FieldType.GetGenericArguments()[0];
                fStates.Add((f, genericArgument, stateFlag1?.Default, stateFlag1?.Factory));
            }
            else if (contextFlag != null)
            {
                fContexts.Add((f, f.FieldType.GetUniqueName()));
            }
            else if (isDependency)
            {
                fDeps.Add((f, injectMethods.ToImmutableList()));
            }
        }

        foreach (var p in ClassType.GetProperties(F))
        {
            propFlag = null;
            stateFlag1 = null;
            contextFlag = null;
            injectMethods.Clear();
            isValidState = IsValidStateDefinition(p.PropertyType);
            isDependency = p.PropertyType.IsAssignableTo(typeof(IDependency));

            foreach (var a in p.GetCustomAttributes())
            {
                setFlags(a, p.PropertyType);
            }

            if (propFlag != null)
            {
                pProps.Add(p);
            }
            else if (isValidState)
            {
                var genericArgument = p.PropertyType.GetGenericArguments()[0];
                pStates.Add((p, genericArgument, stateFlag1?.Default, stateFlag1?.Factory));
            }
            else if (contextFlag != null)
            {
                pContexts.Add((p, p.PropertyType.GetUniqueName()));
            }
            else if (isDependency)
            {
                pDeps.Add((p, injectMethods.ToImmutableList()));
            }
        }

        PropFields = fProps.AsReadOnly();
        PropProperties = pProps.AsReadOnly();
        StateFields = fStates.AsReadOnly();
        StateProperties = pStates.AsReadOnly();
        ContextFields = fContexts.AsReadOnly();
        ContextProperties = pContexts.AsReadOnly();
        DependencyFields = fDeps.AsReadOnly();
        DependencyProperties = pDeps.AsReadOnly();

        // ── Effect scanning ──────────────────────────────────────────────────

        var mountEffects   = new List<MethodInfo>();
        var unmountEffects = new List<MethodInfo>();
        var wireEffects    = new Dictionary<string, List<MethodInfo>>();
        var effects        = new Dictionary<string, List<MethodInfo>>();
        var asyncEffects   = new Dictionary<string, List<MethodInfo>>();

        static void addToDeps(
            Dictionary<string, List<MethodInfo>> dict,
            IEnumerable<string> deps,
            MethodInfo method)
        {
            foreach (var dep in deps)
            {
                if (!dict.TryGetValue(dep, out var lst))
                {
                    lst = [];
                    dict.Add(dep, lst);
                }
                lst.Add(method);
            }
        }

        foreach (var m in ClassType.GetMethods(F))
        {
            bool mountFlag   = m.GetCustomAttribute<MountEffectAttribute>()   != null;
            bool unmountFlag = m.GetCustomAttribute<UnmountEffectAttribute>() != null;

            // Wire effects ([WireEffect])
            var wireAttrs = m.GetCustomAttributes<WireEffectAttribute>().ToList();
            if (wireAttrs.Count > 0)
            {
                var deps = wireAttrs.SelectMany(a => a.Dependencies).ToList();
                addToDeps(wireEffects, deps, m);
                // Wire effects also run on mount unless [MountEffect] is already present
                if (!mountFlag) mountFlag = true;
            }

            // Layout effects ([LayoutEffect] or legacy [Effect])
            var layoutAttrs = m.GetCustomAttributes<LayoutEffectAttribute>().ToList();
            var legacyAttrs = m.GetCustomAttributes<EffectAttribute>().ToList();
            var layoutDeps  = layoutAttrs.SelectMany(a => a.Dependencies)
                              .Concat(legacyAttrs.SelectMany(a => a.Dependencies))
                              .ToList();
            if (layoutDeps.Count > 0 || layoutAttrs.Count > 0 || legacyAttrs.Count > 0)
            {
                addToDeps(effects, layoutDeps, m);
                if (!mountFlag) mountFlag = true;
            }

            // Async effects ([AsyncEffect])
            var asyncAttrs = m.GetCustomAttributes<AsyncEffectAttribute>().ToList();
            if (asyncAttrs.Count > 0)
            {
                var deps = asyncAttrs.SelectMany(a => a.Dependencies).ToList();
                addToDeps(asyncEffects, deps, m);
                if (!mountFlag) mountFlag = true;
            }

            if (mountFlag)   mountEffects.Add(m);
            if (unmountFlag) unmountEffects.Add(m);
        }

        MountEffects   = mountEffects.AsReadOnly();
        UnmountEffects = unmountEffects.AsReadOnly();

        static ImmutableDictionary<string, IList<MethodInfo>> freeze(Dictionary<string, List<MethodInfo>> d)
        {
            var out2 = new Dictionary<string, IList<MethodInfo>>();
            foreach (var (k, v) in d) out2[k] = v.AsReadOnly();
            return out2.ToImmutableDictionary();
        }

        WireEffects  = freeze(wireEffects);
        Effects      = freeze(effects);
        AsyncEffects = freeze(asyncEffects);

        // ── Effect dependency tracking ───────────────────────────────────────
        // Union the keys from all dep-keyed effect dictionaries
        var allDepKeys = WireEffects.Keys
            .Concat(Effects.Keys)
            .Concat(AsyncEffects.Keys)
            .Distinct()
            .ToHashSet();

        List<(FieldInfo, MethodInfo?)> effectDepFields = [];
        List<(PropertyInfo, MethodInfo?)> effectDepProperties = [];

        foreach (var k in allDepKeys)
        {
            var f = type.GetField(k, F);
            var p = type.GetProperty(k, F);

            if (f != null)
            {
                effectDepFields.Add((f, f.FieldType.IsAssignableTo(typeof(IState)) ? f.FieldType.GetMethod(STATE_GET, F) : null));
            }
            else if (p != null)
            {
                effectDepProperties.Add((p, p.PropertyType.IsAssignableTo(typeof(IState)) ? p.PropertyType.GetMethod(STATE_GET, F) : null));
            }
        }

        EffectDepFields      = effectDepFields.AsReadOnly();
        EffectDepProperties  = effectDepProperties.AsReadOnly();
    }

}
