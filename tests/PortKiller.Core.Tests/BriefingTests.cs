using PortKiller.Core;
using Xunit;

namespace PortKiller.Core.Tests;

public class BriefingTests
{
    [Theory]
    [InlineData(BriefingTopic.Hunger)]
    [InlineData(BriefingTopic.Happiness)]
    [InlineData(BriefingTopic.Energy)]
    [InlineData(BriefingTopic.Health)]
    [InlineData(BriefingTopic.Cleanliness)]
    public void Every_topic_has_three_distinct_lines(BriefingTopic topic)
    {
        var good = Briefing.Line(topic, 90);
        var warn = Briefing.Line(topic, 40);
        var bad = Briefing.Line(topic, 10);

        Assert.All(new[] { good, warn, bad }, l => Assert.False(string.IsNullOrWhiteSpace(l)));
        Assert.Equal(3, new[] { good, warn, bad }.Distinct().Count());
    }

    [Theory]
    [InlineData(50, "good")]
    [InlineData(49.9, "warn")]
    [InlineData(25, "warn")]
    [InlineData(24.9, "bad")]
    public void Tier_boundaries_match_the_bar_colors(double value, string tier)
    {
        var expected = tier switch
        {
            "good" => Briefing.Line(BriefingTopic.Health, 100),
            "warn" => Briefing.Line(BriefingTopic.Health, 40),
            _ => Briefing.Line(BriefingTopic.Health, 0),
        };

        Assert.Equal(expected, Briefing.Line(BriefingTopic.Health, value));
    }

    [Fact]
    public void Summary_names_the_most_urgent_problem()
    {
        var s = new PetStats { Hunger = 90, Happiness = 80, Energy = 70, Health = 100, Cleanliness = 15 };

        Assert.Equal(Briefing.Line(BriefingTopic.Cleanliness, 15), Briefing.Summary(s));
    }

    [Fact]
    public void Summary_is_cheerful_when_everything_is_fine()
    {
        Assert.Contains("좋아요", Briefing.Summary(new PetStats()));
    }
}
