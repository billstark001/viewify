using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Viewify.Base;

/// <summary>
/// <b>Mount phase.</b> The decorated method is invoked once, immediately after the
/// component is first committed to the tree. Equivalent to React's
/// <c>useEffect(() => ..., [])</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class MountEffectAttribute : Attribute { }

/// <summary>
/// <b>Unmount phase.</b> The decorated method is invoked when the component is
/// removed from the tree. Use it to release resources acquired during mount.
/// Equivalent to React's cleanup function returned from <c>useEffect</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class UnmountEffectAttribute : Attribute { }

/// <summary>
/// <b>Wire phase (before update).</b> The decorated method is invoked during
/// reconciliation, <em>before</em> the work-in-progress tree is committed.
/// Suitable for deriving computed values that later render phases need.
/// Dependency names (<paramref name="dependencies"/>) refer to fields or
/// properties of the same class; the method runs only when at least one
/// dependency has changed (empty means always run).
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class WireEffectAttribute(params string[] dependencies) : Attribute
{
    public string[] Dependencies { get; init; } = (string[])dependencies.Clone();
}

/// <summary>
/// <b>Layout phase (after update).</b> The decorated method is invoked during
/// the commit phase, after native/platform views have been updated.
/// Dependency names (<paramref name="dependencies"/>) refer to fields or
/// properties of the same class; the method runs on first mount and whenever
/// a dependency changes. Equivalent to React's <c>useLayoutEffect</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class LayoutEffectAttribute(params string[] dependencies) : Attribute
{
    public string[] Dependencies { get; init; } = (string[])dependencies.Clone();
}

/// <summary>
/// <b>Layout phase — legacy alias for <see cref="LayoutEffectAttribute"/>.</b>
/// Kept for backward compatibility.  New code should use
/// <c>[LayoutEffect]</c> instead.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class EffectAttribute(params string[] dependencies) : Attribute
{
    public string[] Dependencies { get; init; } = (string[])dependencies.Clone();
}

/// <summary>
/// <b>Async phase.</b> The decorated method is invoked asynchronously after
/// the commit is fully complete (i.e. after all layout effects have run).
/// The method may return <c>void</c> or <c>Task</c>; a <c>Task</c> return
/// value is awaited. Equivalent to React's <c>useEffect</c>.
/// Dependency names (<paramref name="dependencies"/>) refer to fields or
/// properties of the same class; the method runs on first mount and whenever
/// a dependency changes.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class AsyncEffectAttribute(params string[] dependencies) : Attribute
{
    public string[] Dependencies { get; init; } = (string[])dependencies.Clone();
}

