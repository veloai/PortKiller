using PortKiller.Core;
using Xunit;

namespace PortKiller.Core.Tests;

public class DevStatusTests
{
    private static DevCheck C(string name, DevState s) => new(name, s, "");

    [Fact]
    public void First_check_reports_no_changes()
    {
        Assert.Empty(DevStatusRules.Diff(null, new[] { C("hermes", DevState.Down) }));
    }

    [Fact]
    public void Ok_to_down_and_down_to_ok_are_reported()
    {
        var before = new[] { C("hermes", DevState.Ok), C("alfa-pg", DevState.Down) };
        var after = new[] { C("hermes", DevState.Down), C("alfa-pg", DevState.Ok) };

        var changes = DevStatusRules.Diff(before, after);

        Assert.Contains(new DevChange("hermes", DevState.Ok, DevState.Down), changes);
        Assert.Contains(new DevChange("alfa-pg", DevState.Down, DevState.Ok), changes);
    }

    [Fact]
    public void Transitions_through_unknown_are_not_announced()
    {
        var before = new[] { C("hermes", DevState.Ok), C("alfa-pg", DevState.Unknown) };
        var after = new[] { C("hermes", DevState.Unknown), C("alfa-pg", DevState.Ok) };

        Assert.Empty(DevStatusRules.Diff(before, after));
    }

    [Fact]
    public void Lines_say_stopped_or_recovered_with_the_name()
    {
        Assert.Contains("hermes", DevStatusRules.Line(new DevChange("hermes", DevState.Ok, DevState.Down)));
        Assert.Contains("멈췄", DevStatusRules.Line(new DevChange("hermes", DevState.Ok, DevState.Down)));
        Assert.Contains("살아났", DevStatusRules.Line(new DevChange("hermes", DevState.Down, DevState.Ok)));
    }

    [Fact]
    public void Summary_names_only_the_broken_items()
    {
        var checks = new[] { C("Docker 엔진", DevState.Ok), C("hermes", DevState.Down), C("alfa-pg", DevState.Ok) };

        var s = DevStatusRules.Summary(checks);

        Assert.Contains("hermes", s);
        Assert.DoesNotContain("alfa-pg", s);
        Assert.Contains("정상", DevStatusRules.Summary(new[] { C("a", DevState.Ok) }));
        Assert.Contains("확인 불가", DevStatusRules.Summary(new[] { C("a", DevState.Ok), C("b", DevState.Unknown) }));
    }

    [Fact]
    public void Docker_ps_output_is_parsed_by_name()
    {
        var map = DevStatusRules.ParseDockerPs("hermes|running|Up 8 days\nalfa-pg|exited|Exited (0) 2 hours ago\n\nbroken-line\n");

        Assert.Equal("running", map["hermes"].State);
        Assert.Equal("exited", map["ALFA-PG"].State);
        Assert.Equal("Up 8 days", map["hermes"].Status);
        Assert.Equal(2, map.Count);
    }

    [Fact]
    public void Defaults_cover_docker_hermes_and_the_bridge()
    {
        var names = DevStatusRules.Defaults.Select(d => d.Name).ToList();

        Assert.Contains("Docker 엔진", names);
        Assert.Contains("hermes", names);
        Assert.Contains("Hermes 브리지", names);
        Assert.All(DevStatusRules.Defaults, d => Assert.Contains(d.Kind, new[] { "docker-desktop", "docker-engine", "container", "port" }));
    }
}
