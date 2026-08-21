using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using UiPilot.Inspection;
using Xunit;

namespace UiPilot.Tests;

public sealed class AvaloniaVisualTreeTests
{
    [Fact]
    public void FindAncestor_FallsBackToLogicalTreeForPopupContent()
    {
        var popup = new Popup();
        var child = new TextBlock { Text = "menu item" };
        popup.Child = child;
        var registry = new ElementRegistry();
        var id = registry.GetOrAdd(child);

        var ancestor = global::UiPilot.Avalonia.VisualTree.FindAncestor(
            registry, id, nameof(Popup), maxDepth: 10);

        Assert.NotNull(ancestor);
        Assert.Equal(nameof(Popup), ancestor!.Type);
    }
}
