using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Viewify.Base;

/// <summary>
/// Marker interface for objects that participate in dependency injection.
/// A class may implement this interface (or an interface derived from it)
/// so that the framework automatically detects it as an injectable dependency.
/// <para>
/// Two patterns are supported:
/// <list type="bullet">
///   <item><b>Field/property definition</b> — annotate a field or property
///     of type <c>IDependency</c> with <c>[Inject]</c> to specify explicit
///     property-level mappings from the owner view.</item>
///   <item><b>Interface inheritance</b> — implement <c>IDependency</c> (or a
///     derived interface) without any attributes; the framework calls
///     <see cref="Derive"/> so the dependency can read values directly from
///     the owner view.</item>
/// </list>
/// </para>
/// </summary>
public interface IDependency : IStateful
{
    /// <summary>
    /// Called by the framework after the dependency is attached to its owner.
    /// Override to compute or propagate values from <paramref name="owner"/>.
    /// The default implementation is a no-op.
    /// </summary>
    /// <param name="owner">The view instance that holds this dependency.</param>
    public void Derive(IStateful? owner) { }
}

