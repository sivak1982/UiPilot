using System.Text.Json;
using System.Windows.Threading;
using WpfPilot.Inspection;
using WpfPilot.Tools;

namespace WpfPilot.Tests;

internal static class TestSupport
{
    /// <summary>
    /// Build a ToolContext bound to the current thread's dispatcher. Tools registered for tests
    /// must not call OnUi (nothing pumps this dispatcher); they just return plain values.
    /// </summary>
    public static ToolContext CreateContext() =>
        new ToolContext(Dispatcher.CurrentDispatcher, new ElementRegistry(), new BindingDiagnostics());

    public static JsonElement Json(string raw)
    {
        using var doc = JsonDocument.Parse(raw);
        return doc.RootElement.Clone();
    }
}
