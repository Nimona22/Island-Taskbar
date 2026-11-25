namespace IslandTaskbar.Persistence
{
    /// <summary>Approved relative names only; M1 resolves LocalApplicationData at runtime (D-014, OD-006).</summary>
    internal static class PinsStorageContract
    {
        public const string DirectoryName = "IslandTaskbar";
        public const string FileName = "pins.json";
        public const string WriterLeaseFileName = "pins.json.lock";
    }
}

