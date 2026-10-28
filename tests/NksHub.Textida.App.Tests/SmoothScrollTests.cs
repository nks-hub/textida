using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using NksHub.Textida.App.Editing;

namespace NksHub.Textida.App.Tests;

/// <summary>P9.2.1: the glide of smooth scrolling on its own, without a control.</summary>
public class SmoothScrollTests
{
    private const double Share = 0.3;

    /// <summary>Runs frames until the glide ends and returns the positions it went through.</summary>
    private static List<double> Frames(SmoothScroll glide, double start)
    {
        var positions = new List<double> { start };
        double position = start;
        for (int i = 0; i < 500 && glide.Active; i++)
        {
            position = glide.Next(position, Share);
            positions.Add(position);
        }
        return positions;
    }

    [Fact]
    public void A_push_glides_to_the_target_in_steps_that_get_smaller_and_ends_exactly_on_it()
    {
        var glide = new SmoothScroll();
        Assert.True(glide.Push(0, 120, 0, 1000));
        var positions = Frames(glide, 0);

        Assert.False(glide.Active);
        Assert.Equal(120, positions[^1]);
        Assert.True(positions.Count is > 5 and < 60, positions.Count.ToString());
        var steps = positions.Zip(positions.Skip(1), (a, b) => b - a).ToList();
        Assert.All(steps, s => Assert.True(s > 0));
        Assert.True(steps[0] > steps[1] && steps[1] > steps[2]); // quick at the start, gentle at the end
        Assert.Equal(120 * Share, positions[1], 9);
    }

    [Fact]
    public void Further_pushes_add_to_the_target_that_is_under_way_and_stay_inside_the_range()
    {
        var glide = new SmoothScroll();
        glide.Push(100, 50, 0, 400);
        double position = glide.Next(100, Share);
        Assert.True(glide.Push(position, 50, 0, 400)); // the target was 150, now 200
        Assert.Equal(200, glide.Target);
        Assert.True(glide.Push(position, 1000, 0, 400)); // cannot go past the end
        Assert.Equal(400, glide.Target);
        Assert.True(glide.Push(position, -5000, 0, 400)); // nor before the start
        Assert.Equal(0, glide.Target);
        Assert.Equal(0, Frames(glide, position)[^1]);
    }

    [Fact]
    public void A_push_that_leads_nowhere_starts_no_glide()
    {
        var glide = new SmoothScroll();
        Assert.False(glide.Push(0, -10, 0, 100)); // already at the start
        Assert.False(glide.Active);
        Assert.False(glide.Push(100, 10, 0, 100)); // already at the end
        Assert.False(glide.Push(50, 0.2, 0, 100)); // less than a pixel
        Assert.Equal(7, glide.Next(7, Share)); // an idle glide leaves the position alone
    }

    [Fact]
    public void A_position_someone_else_moved_ends_the_glide_without_a_jump()
    {
        var glide = new SmoothScroll();
        glide.Push(0, 200, 0, 1000);
        Assert.True(glide.Next(0, Share) > 0);
        Assert.Equal(500, glide.Next(500, Share)); // the scroll bar was moved to 500 meanwhile: the glide gives up and leaves it there
        Assert.False(glide.Active);

        // a push after the user moved the text starts from where the text is, not from the old target
        Assert.True(glide.Push(500, 30, 0, 1000));
        Assert.Equal(530, glide.Target);

        // the same when the text was moved between a push and the next push
        glide.Next(500, Share);
        Assert.True(glide.Push(100, 20, 0, 1000));
        Assert.Equal(120, glide.Target);
    }

    [Fact]
    public void A_small_difference_from_the_last_answer_is_the_control_rounding_and_not_a_user()
    {
        var glide = new SmoothScroll();
        glide.Push(0, 301, 0, 1000);
        double answer = glide.Next(0, Share);
        double rounded = Math.Round(answer);
        Assert.True(glide.Next(rounded, Share) > rounded);
        Assert.True(glide.Active);
    }

    [AvaloniaFact]
    public async Task The_timer_drives_the_frames_and_stops_when_the_target_is_reached()
    {
        double position = 0;
        var writes = new List<double>();
        var glide = new Glide(() => position, value => { position = value; writes.Add(value); });

        Assert.False(glide.Push(-5, 0, 100)); // nothing to do at the start
        Assert.True(glide.Push(90, 0, 100));
        Assert.True(glide.Active);
        for (int i = 0; i < 300 && glide.Active; i++)
        {
            await Task.Delay(10);
            Dispatcher.UIThread.RunJobs();
        }
        Assert.False(glide.Active);
        Assert.Equal(90, position);
        Assert.True(writes.Count > 3);
        Assert.Equal(writes.Order(), writes); // it only ever moved forward

        // the position changed by someone else ends the glide and nothing is written
        Assert.True(glide.Push(-90, 0, 100));
        position = 80;
        int before = writes.Count;
        for (int i = 0; i < 20; i++)
        {
            await Task.Delay(10);
            Dispatcher.UIThread.RunJobs();
        }
        Assert.False(glide.Active);
        Assert.Equal(before, writes.Count);
        Assert.Equal(80, position);
    }
}
