using NksHub.Textida.Tests;

namespace NksHub.Textida.Core.Tests;

/// <summary>The sizes that the tests of big files ask <see cref="HugeText"/> for: 2 GiB at most unless <c>TEXTIDA_HUGE=1</c>.</summary>
public class HugeTextSizeTests
{
    [Fact]
    public void A_size_is_capped_at_2_GiB_and_a_boundary_test_gets_4_MiB_past_the_boundary_unless_the_huge_switch_is_on()
    {
        if (HugeText.Scale != 1) return; // a dry run shrinks every size
        long small = HugeText.Size(300L << 20), giant = HugeText.Size(10L << 30), past = HugeText.SizePast(2L << 30, 3L << 30);
        Assert.Equal(300L << 20, small);
        if (HugeText.Enabled)
        {
            Assert.Equal(10L << 30, giant);
            Assert.Equal(3L << 30, past);
        }
        else
        {
            Assert.Equal(2L << 30, giant);
            Assert.Equal((2L << 30) + (4L << 20), past);
        }
    }

    [Fact]
    public void A_boundary_test_that_asks_for_less_than_the_boundary_gets_what_it_asked_for()
    {
        if (HugeText.Scale != 1) return;
        Assert.Equal(1L << 30, HugeText.SizePast(2L << 30, 1L << 30));
        Assert.Equal(1L << 30, HugeText.Size(1L << 30));
    }
}
