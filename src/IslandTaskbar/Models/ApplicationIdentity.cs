using System;

namespace IslandTaskbar.Models
{
    internal enum ApplicationIdentityKind { AppUserModelId, ExecutablePath }

    /// <summary>
    /// A runtime-derived, reliable identity (D-014). Paths must already be canonical full paths.
    /// AppUserModelIDs are opaque; neither this value nor equality validates a target.
    /// </summary>
    internal readonly record struct ApplicationIdentity(ApplicationIdentityKind Kind, string Value)
    {
        private StringComparer ValueComparer => Kind == ApplicationIdentityKind.AppUserModelId
            ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

        public bool Equals(ApplicationIdentity other) =>
            Kind == other.Kind && ValueComparer.Equals(Value, other.Value);

        public override int GetHashCode() =>
            HashCode.Combine(Kind, Value is null ? 0 : ValueComparer.GetHashCode(Value));
    }
}

