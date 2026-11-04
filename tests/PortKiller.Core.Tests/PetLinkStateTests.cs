using PortKiller.Core;
using Xunit;
using Pickup = PortKiller.Core.AlfaChatClient.PickupState;
using Link = PortKiller.Core.AlfaChatClient.LinkState;

namespace PortKiller.Core.Tests;

/// <summary>ALFA 연동 상태 기계(§14, §11.4). 시계는 고정 값으로 준다.</summary>
public class PetLinkStateTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 4, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Expires = T0.AddMinutes(10);

    private static PetLinkState Waiting(bool hadKey = false)
        => PetLinkState.Initial(hadKey, hadKey ? T0.AddDays(-1) : null).Requested("A1B2C3", Expires);

    [Fact]
    public void Starts_off_without_a_key_and_linked_with_one()
    {
        Assert.Equal(PetLinkPhase.Off, PetLinkState.Initial(false).Phase);
        var linked = PetLinkState.Initial(true, T0);
        Assert.Equal(PetLinkPhase.Linked, linked.Phase);
        Assert.Equal(T0, linked.LinkedAt);
        Assert.Null(linked.LastSeen);
    }

    [Fact]
    public void Request_waits_with_the_code()
    {
        var s = Waiting();

        Assert.Equal(PetLinkPhase.Waiting, s.Phase);
        Assert.Equal("A1B2C3", s.Code);
        Assert.Equal("WAITING", s.MenuWord);
        Assert.Equal("🔗 ALFA 연동 · WAITING", s.MenuLabel);
        Assert.Equal("10:00", PetLinkState.Clock(s.Remaining(T0)));
        Assert.Equal("00:59", PetLinkState.Clock(s.Remaining(Expires.AddSeconds(-59))));
    }

    [Theory]
    [InlineData(Pickup.Pending)]
    [InlineData(Pickup.Retry)]
    public void Pending_and_network_errors_keep_waiting_even_after_expiry(Pickup result)
    {
        var s = Waiting().Pickup(result, Expires.AddMinutes(3));

        Assert.Equal(PetLinkPhase.Waiting, s.Phase);
        Assert.Equal(TimeSpan.Zero, s.Remaining(Expires.AddMinutes(3)));
    }

    [Fact]
    public void Late_approval_still_links()
    {
        var late = Expires.AddMinutes(2);
        var s = Waiting().Pickup(Pickup.Pending, Expires).Pickup(Pickup.Issued, late);

        Assert.Equal(PetLinkPhase.Linked, s.Phase);
        Assert.Equal(late, s.LinkedAt);
        Assert.Equal(late, s.LastSeen);
        Assert.Null(s.Code);
    }

    [Fact]
    public void Closed_before_expiry_is_rejected_after_is_expired()
    {
        Assert.Equal(PetLinkPhase.Rejected, Waiting().Pickup(Pickup.Closed, T0.AddMinutes(3)).Phase);
        Assert.Equal(PetLinkPhase.Expired, Waiting().Pickup(Pickup.Closed, Expires).Phase);
        Assert.Equal(PetLinkPhase.Expired, Waiting().Pickup(Pickup.Closed, Expires.AddMinutes(1)).Phase);
    }

    [Theory]
    [InlineData(Pickup.AlreadyIssued)]
    [InlineData(Pickup.NotFound)]
    public void Key_already_taken_or_unknown_request_is_rejected(Pickup result)
        => Assert.Equal(PetLinkPhase.Rejected, Waiting().Pickup(result, T0.AddMinutes(1)).Phase);

    [Fact]
    public void Cancel_goes_back_to_before_the_request()
    {
        Assert.Equal(PetLinkPhase.Off, Waiting().Cancelled().Phase);
        var waiting = Waiting(hadKey: true);
        var back = waiting.Cancelled();
        Assert.Equal(PetLinkPhase.Linked, back.Phase);
        Assert.Equal(T0.AddDays(-1), back.LinkedAt);
        Assert.Null(PetLinkState.Speech(waiting, back).Line);
        Assert.Null(PetLinkState.Speech(Waiting(), Waiting().Cancelled()).Line);
    }

    [Fact]
    public void Cancel_after_re_requesting_from_a_cut_link_stays_cut()
    {
        var revoked = PetLinkState.Initial(true).Checked(Link.Unlinked, T0);
        var back = revoked.Requested("X", Expires).Cancelled();
        Assert.Equal(PetLinkPhase.Revoked, back.Phase);
        Assert.Null(PetLinkState.Speech(revoked.Requested("X", Expires), back).Line);
    }

    [Fact]
    public void Heartbeat_updates_last_seen_and_401_revokes()
    {
        var s = PetLinkState.Initial(true, T0).Checked(Link.Connected, T0.AddMinutes(5));
        Assert.Equal(T0.AddMinutes(5), s.LastSeen);
        Assert.Equal(T0, s.LinkedAt);

        var revoked = s.Checked(Link.Unlinked, T0.AddMinutes(10));
        Assert.Equal(PetLinkPhase.Revoked, revoked.Phase);
        Assert.Equal("OFF", revoked.MenuWord);
        Assert.Equal("▧", revoked.Glyph);
    }

    [Fact]
    public void Chat_401_revokes_like_the_heartbeat()
        => Assert.Equal(PetLinkPhase.Revoked, PetLinkState.Initial(true).Unauthorized(T0).Phase);

    [Fact]
    public void Unreachable_keeps_the_key_and_comes_back()
    {
        var s = PetLinkState.Initial(true).Checked(Link.Connected, T0).Checked(Link.Unreachable, T0.AddMinutes(5));
        Assert.Equal(PetLinkPhase.Unreachable, s.Phase);
        Assert.True(s.IsLinked);
        Assert.Equal("OFF", s.MenuWord);
        Assert.Equal(T0, s.LastSeen);

        var back = s.Checked(Link.Connected, T0.AddMinutes(10));
        Assert.Equal(PetLinkPhase.Linked, back.Phase);
    }

    [Fact]
    public void Heartbeat_is_ignored_while_waiting_and_without_a_key()
    {
        Assert.Equal(PetLinkPhase.Waiting, Waiting().Checked(Link.Unlinked, T0).Phase);
        Assert.Equal(PetLinkPhase.Off, PetLinkState.Off.Checked(Link.Unlinked, T0).Phase);
        Assert.Equal(PetLinkPhase.Off, PetLinkState.Off.Checked(Link.Unreachable, T0).Phase);
        Assert.Equal(PetLinkPhase.Expired, Waiting().Pickup(Pickup.Closed, Expires).Checked(Link.Unlinked, T0).Phase);
    }

    [Fact]
    public void Unlink_turns_off()
        => Assert.Equal(PetLinkPhase.Off, PetLinkState.Initial(true).Unlinked().Phase);

    [Theory]
    [InlineData(PetLinkPhase.Off, "OFF", "░")]
    [InlineData(PetLinkPhase.Waiting, "WAITING", "▒")]
    [InlineData(PetLinkPhase.Linked, "LINKED", "▦")]
    [InlineData(PetLinkPhase.Expired, "OFF", "░")]
    [InlineData(PetLinkPhase.Rejected, "OFF", "▧")]
    [InlineData(PetLinkPhase.Revoked, "OFF", "▧")]
    [InlineData(PetLinkPhase.Unreachable, "OFF", "░")]
    public void Menu_words_and_dot_glyphs(PetLinkPhase phase, string word, string glyph)
    {
        var s = new PetLinkState { Phase = phase };
        Assert.Equal(word, s.MenuWord);
        Assert.Equal(glyph, s.Glyph);
    }

    [Fact]
    public void Status_line_shows_minutes_since_last_check()
    {
        var s = PetLinkState.Initial(true).Checked(Link.Connected, T0);

        Assert.Equal("ALFA ▦ LINKED · 2m", s.StatusLine(T0.AddMinutes(2).AddSeconds(30)));
        Assert.Equal("ALFA ▦ LINKED · 3h", s.StatusLine(T0.AddHours(3)));
        Assert.Equal(2, s.LastSeenMinutes(T0.AddMinutes(2)));
        Assert.Equal("ALFA ▦ LINKED", PetLinkState.Initial(true).StatusLine(T0));
        Assert.Null(PetLinkState.Initial(true).LastSeenMinutes(T0));
        Assert.Equal("ALFA ▒ WAITING · 07:00", Waiting().StatusLine(T0.AddMinutes(3)));
        Assert.Equal("ALFA ░ OFF", PetLinkState.Off.StatusLine(T0));
    }

    // ---------- C8 기록은 단계가 바뀔 때만 ----------

    [Fact]
    public void Heartbeats_that_only_move_last_seen_are_not_logged()
    {
        var a = PetLinkState.Initial(true).Checked(Link.Connected, T0);
        var b = a.Checked(Link.Connected, T0.AddMinutes(5));

        Assert.NotEqual(a, b);
        Assert.False(PetLinkState.ShouldLog(a, b));
        Assert.True(PetLinkState.ShouldLog(b, b.Checked(Link.Unreachable, T0.AddMinutes(10))));
        Assert.Equal("alfa-link Linked -> Revoked", PetLinkState.LogLine(b, b.Checked(Link.Unlinked, T0)));
    }

    [Fact]
    public void A_day_of_steady_heartbeats_writes_no_log_lines()
    {
        var s = PetLinkState.Initial(true);
        var lines = 0;
        for (var i = 0; i < 288; i++)
        {
            var next = s.Checked(Link.Connected, T0.AddMinutes(5 * i));
            if (PetLinkState.ShouldLog(s, next)) lines++;
            s = next;
        }
        Assert.Equal(0, lines);
    }

    // ---------- §14 말풍선·표정 ----------

    [Fact]
    public void Speech_for_each_transition()
    {
        var off = PetLinkState.Off;
        var waiting = Waiting();
        var linked = waiting.Pickup(Pickup.Issued, T0);

        Assert.Equal(new PetLinkSpeech("ALFA 에 확인 코드로 승인 부탁했어요!", PetLinkMood.None), PetLinkState.Speech(off, waiting));
        Assert.Equal(new PetLinkSpeech("ALFA 와 연결됐어요!", PetLinkMood.Happy), PetLinkState.Speech(waiting, linked));
        Assert.Equal(new PetLinkSpeech("연동이 안 됐어요. 다시 요청할까요?", PetLinkMood.Sad),
            PetLinkState.Speech(waiting, waiting.Pickup(Pickup.Closed, T0.AddMinutes(1))));
        Assert.Equal(new PetLinkSpeech("연동이 안 됐어요. 다시 요청할까요?", PetLinkMood.Sad),
            PetLinkState.Speech(waiting, waiting.Pickup(Pickup.Closed, Expires)));
        Assert.Equal(new PetLinkSpeech("ALFA 연결이 끊겼어요", PetLinkMood.Sad),
            PetLinkState.Speech(linked, linked.Checked(Link.Unlinked, T0)));
    }

    [Fact]
    public void Unreachable_and_steady_states_are_silent_and_revoked_is_said_once()
    {
        var linked = PetLinkState.Initial(true).Checked(Link.Connected, T0);
        var down = linked.Checked(Link.Unreachable, T0.AddMinutes(5));
        var revoked = down.Checked(Link.Unlinked, T0.AddMinutes(10));

        Assert.Null(PetLinkState.Speech(linked, down).Line);
        Assert.Null(PetLinkState.Speech(down, linked).Line);
        Assert.Null(PetLinkState.Speech(linked, linked.Checked(Link.Connected, T0.AddMinutes(5))).Line);
        Assert.NotNull(PetLinkState.Speech(down, revoked).Line);
        Assert.Null(PetLinkState.Speech(revoked, revoked.Checked(Link.Unlinked, T0.AddMinutes(15))).Line);
        Assert.Null(PetLinkState.Speech(revoked, revoked.Unauthorized(T0.AddMinutes(16))).Line);
    }

    [Fact]
    public void Startup_with_a_key_then_first_check_success_is_silent()
        => Assert.Null(PetLinkState.Speech(PetLinkState.Initial(true), PetLinkState.Initial(true).Checked(Link.Connected, T0)).Line);
}
