using NksHub.Textida.Core.Colors;

namespace NksHub.Textida.Core.Tests;

/// <summary>PZ.4.1: the platform call that reads the pixel under the pointer, and the conversion of an X image pixel.</summary>
public class ScreenColorTests
{
    [Fact]
    public void An_x_image_pixel_is_split_by_the_masks_of_its_channels()
    {
        Assert.Equal(new Rgba(0xFF, 0x80, 0x00), ScreenColor.FromMasks(0x00FF8000, 0xFF0000, 0x00FF00, 0x0000FF));
        Assert.Equal(new Rgba(0xFF, 0xFF, 0xFF), ScreenColor.FromMasks(0xFFFF, 0xF800, 0x07E0, 0x001F)); // 16 bits: 5-6-5
        Assert.Equal(new Rgba(0, 0, 0), ScreenColor.FromMasks(123, 0, 0, 0));
    }

    [Fact]
    public void On_windows_the_pixel_under_the_pointer_can_be_read_without_a_permission()
    {
        if (!OperatingSystem.IsWindows()) return; // macOS and Linux are checked on those systems
        Assert.Equal(ScreenColorSupport.Available, ScreenColor.Support(out _));
        if (!ScreenColor.TryRead(out var color, out int x, out int y)) return; // a session without a desktop (a service) has no screen to read
        Assert.InRange(color.A, 1, 1);
        Assert.True(x > -100_000 && y > -100_000);
    }
}
