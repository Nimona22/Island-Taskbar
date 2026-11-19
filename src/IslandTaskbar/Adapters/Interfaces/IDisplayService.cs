namespace IslandTaskbar.Adapters.Interfaces
{
    /// <summary>
    /// Supplies display information without exposing platform objects to the controller.
    /// </summary>
    internal interface IDisplayService
    {
        /// <summary>
        /// Returns the primary display's full screen bounds in physical pixels, including its origin.
        /// </summary>
        (int X, int Y, int Width, int Height) GetPrimaryDisplayBounds();
    }
}
