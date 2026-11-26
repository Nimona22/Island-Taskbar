using IslandTaskbar.Models;
using IslandTaskbar.Adapters.Interfaces;
using IslandTaskbar.Controllers;

int checks = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException("FAIL: " + name);
    checks++;
}

Check(TaskbarRowSizing.GetRowHeight([], 50) == 50, "empty list retains nominal row floor");
Check(TaskbarRowSizing.GetRowHeight([34, 48], 50) == 50, "short content retains nominal row floor");
Check(TaskbarRowSizing.GetRowHeight([50, 86, 62], 50) == 86, "later tall title determines row height");
Check(TaskbarRowSizing.GetRowHeight([86, 62, 50], 50) == 86, "row height is independent of item order");
Check(TaskbarRowSizing.GetRowHeight([51, 54], 50) == 54, "lowered scale can shrink row height");

void Budget(double pins, double windows, double pinRow, double windowRow, double budget,
    double expectedPins, double expectedWindows, string name)
{
    var result = TaskbarRowSizing.GetViewportHeights(pins, windows, pinRow, windowRow, budget);
    Check(result.Pins == expectedPins && result.Windows == expectedWindows, name);
}

Budget(50, 150, 50, 50, 300, 50, 150, "roomy display keeps one pin and three window rows");
Budget(70, 270, 70, 90, 250, 70, 180, "budget reduces window viewport and keeps pin row");
Budget(70, 270, 70, 90, 160, 70, 90, "exact minimum fits complete rows");
Budget(70, 270, 70, 90, 100, 70, 90, "short display preserves rows for outer scrolling");
Budget(70, 270, 70, 90, 0, 70, 90, "zero remaining budget never removes list reachability");
Budget(0, 270, 70, 90, 110, 0, 110, "empty pins leave viewport to windows");
Budget(70, 0, 70, 90, 40, 70, 0, "single populated section remains reachable on short display");
Budget(0, 0, 70, 90, 0, 0, 0, "empty lists do not invent rows");
Budget(70, 270, 70, 90, double.PositiveInfinity, 70, 270, "startup without display budget remains finite");

var adapter = new RecordingWindowAdapter();
using (var controller = new IslandWindowController(adapter, new TestDisplay()))
{
    controller.Initialize();
    foreach (double scale in new[] { 1d, 1.25, 1.5, 2 })
    {
        foreach (double height in new[] { 200d, 340, 864 / scale, 240 })
        {
            var outline = IslandSurfaceOutline.Create(420, height, 12, 20);
            var layout = new IslandSurfaceLayoutEventArgs(20, 0, 420, height, 20, scale, 0, outline, height);
            controller.UpdateSurfaceRegion(layout);
            controller.UpdateSurfaceRegion(layout); // DPI changes intentionally defer the centered region once.
            Check(adapter.Height == (int)Math.Ceiling(height * scale), "measured host height propagates at " + scale);
            Check(adapter.Polygon!.Where((_, index) => index % 2 == 1).Max() == (int)Math.Round(height * scale),
                "native input polygon follows grown/shrunk height at " + scale);
            Check(adapter.Polygon!.Where((_, index) => index % 2 == 0).Max()
                - adapter.Polygon!.Where((_, index) => index % 2 == 0).Min() == (int)Math.Round(420 * scale),
                "nominal surface width remains 420 DIPs at " + scale);
        }
    }
}

Console.WriteLine($"PASS: {checks} text-scaling sizing/geometry checks (Auto-eligible, Safe; no UI or OS mutations)");

sealed class TestDisplay : IDisplayService
{
    public (int X, int Y, int Width, int Height) GetPrimaryDisplayBounds() => (0, 0, 1920, 864);
}

sealed class RecordingWindowAdapter : IIslandWindowAdapter
{
    public int Height { get; private set; }
    public int[]? Polygon { get; private set; }
    public void MoveAndResize(int x, int y, int width, int height) => Height = height;
    public void SetSurfacePolygon(int[] coordinates) => Polygon = coordinates;
    public void ConfigureOverlay() { }
    public void ShowWithoutActivation() { }
    public void SetSurfaceRegion(int left, int top, int width, int height, int lowerCornerRadius, int upperCornerRadius) =>
        throw new InvalidOperationException("Expected the existing polygon region path.");
    public void Dispose() { }
}
