using System;

namespace IslandTaskbar.Models
{
    internal enum LaunchReferenceKind { Executable, Shortcut, PackagedApplication }

    /// <summary>
    /// Typed untrusted input until resolved (D-014). File values are canonical absolute paths;
    /// packaged values are opaque AppUserModelIDs. Encoding/validation belongs to the M1/M2 boundaries.
    /// </summary>
    internal readonly record struct LaunchReference(LaunchReferenceKind Kind, string Value)
    {
        private StringComparer ValueComparer => Kind == LaunchReferenceKind.PackagedApplication
            ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

        public bool Equals(LaunchReference other) =>
            Kind == other.Kind && ValueComparer.Equals(Value, other.Value);

        public override int GetHashCode() =>
            HashCode.Combine(Kind, Value is null ? 0 : ValueComparer.GetHashCode(Value));
    }
}

