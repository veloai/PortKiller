using System.Net;
using System.Text;
using System.Text.Json;
using PortKiller.Core;
using Xunit;

namespace PortKiller.Core.Tests;

/// <summary>
/// 계약 rev.3(A1·A2·A6·A7·A8) 응답 → 결과 값 대응. 가짜 응답기만 쓴다.
/// 받아 가기는 네트워크 오류에도 예외 없이 "다시"를 돌려주고, 끝은 200/409/410/404 뿐이다(C1).
/// </summary>
public class AlfaLinkContractTests
{
    private const string Key = "alfa_pet_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string Secret = "pickup-secret-do-not-print";

    private static readonly AlfaChatClient.LinkTicket Ticket =
        new(Guid.Parse("00000000-0000-0000-0000-0000000000b2"), Secret, "C0FFEE", DateTimeOffset.UtcNow.AddMinutes(10));

    private sealed class Handler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, int, HttpResponseMessage> _reply;
        public Handler(Func<HttpRequestMessage, int, HttpResponseMessage> reply) => _reply = reply;
        public Handler(Func<HttpRequestMessage, HttpResponseMessage> reply) : this((r, _) => reply(r)) { }
        public int Calls { get; private set; }
        public HttpRequestMessage? Last { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Last = request;
            return Task.FromResult(_reply(request, Calls++));
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body = "{}")
        => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static HttpClient Http(Handler h) => new(h);

    private static readonly Func<TimeSpan, CancellationToken, Task> NoWait = (_, _) => Task.CompletedTask;

    // ---------- A1 ----------

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, """{"error":"INVALID_REQUEST"}""", AlfaChatClient.RequestOutcome.Invalid)]
    [InlineData(HttpStatusCode.TooManyRequests, """{"error":"PET_LINK_LIMIT"}""", AlfaChatClient.RequestOutcome.Limit)]
    [InlineData(HttpStatusCode.TooManyRequests, """{"error":"PET_LINK_RATE_LIMIT"}""", AlfaChatClient.RequestOutcome.RateLimited)]
    [InlineData(HttpStatusCode.Conflict, """{"error":"PET_LINK_CONFLICT"}""", AlfaChatClient.RequestOutcome.Conflict)]
    [InlineData(HttpStatusCode.BadGateway, "", AlfaChatClient.RequestOutcome.Unreachable)]
    [InlineData(HttpStatusCode.Created, """{"linkId":"nope"}""", AlfaChatClient.RequestOutcome.Failed)]
    public async Task Request_errors_become_typed_outcomes(HttpStatusCode status, string body, AlfaChatClient.RequestOutcome expected)
    {
        var result = await AlfaChatClient.RequestLinkAsync(Http(new Handler(_ => Json(status, body))),
            AlfaChatClient.DefaultBaseUrl, "windows-office", "레서판다", "red_panda", "windows");

        Assert.Equal(expected, result.Outcome);
        Assert.Null(result.Ticket);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
    }

    [Fact]
    public async Task Request_network_failure_is_unreachable_not_an_exception()
    {
        var h = new Handler(_ => throw new HttpRequestException("down"));

        var result = await AlfaChatClient.RequestLinkAsync(Http(h), AlfaChatClient.DefaultBaseUrl, "windows-office", "펫", "cat", "windows");

        Assert.Equal(AlfaChatClient.RequestOutcome.Unreachable, result.Outcome);
    }

    [Theory]
    [InlineData("-")]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Request_with_an_unusable_device_name_sends_nothing(string device)
    {
        var h = new Handler(_ => Json(HttpStatusCode.Created));

        var result = await AlfaChatClient.RequestLinkAsync(Http(h), AlfaChatClient.DefaultBaseUrl, device, "펫", "cat", "windows");

        Assert.Equal(AlfaChatClient.RequestOutcome.Invalid, result.Outcome);
        Assert.Equal(0, h.Calls);
    }

    // ---------- 기기 이름 규칙 ^[a-z0-9][a-z0-9._-]{0,59}$ ----------

    [Theory]
    [InlineData("windows-office", true)]
    [InlineData("a", true)]
    [InlineData("0mac.air_2", true)]
    [InlineData("-office", false)]
    [InlineData(".office", false)]
    [InlineData("Windows", false)]
    [InlineData("windows office", false)]
    [InlineData("", false)]
    public void Device_name_rule_matches_the_hub(string name, bool expected)
        => Assert.Equal(expected, AlfaChatClient.IsValidDeviceName(name));

    [Fact]
    public void Device_names_are_cut_at_60_characters()
    {
        Assert.True(AlfaChatClient.IsValidDeviceName(new string('a', 60)));
        Assert.False(AlfaChatClient.IsValidDeviceName(new string('a', 61)));

        var cut = AlfaChatClient.NormalizeDeviceName(new string('b', 70));
        Assert.Equal(60, cut.Length);
        Assert.True(AlfaChatClient.IsValidDeviceName(cut));

        Assert.Equal("office", AlfaChatClient.NormalizeDeviceName("--Office"));
        Assert.True(AlfaChatClient.IsValidDeviceName(AlfaChatClient.DefaultDeviceName));
    }

    // ---------- A2 ----------

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task Pickup_server_trouble_means_try_again(HttpStatusCode status)
    {
        var result = await AlfaChatClient.PickupAsync(Http(new Handler(_ => Json(status))), AlfaChatClient.DefaultBaseUrl, Ticket);

        Assert.Equal(AlfaChatClient.PickupState.Retry, result.State);
        Assert.False(result.IsFinal);
    }

    [Fact]
    public async Task Pickup_network_failure_means_try_again()
    {
        var result = await AlfaChatClient.PickupAsync(Http(new Handler(_ => throw new HttpRequestException("down"))),
            AlfaChatClient.DefaultBaseUrl, Ticket);

        Assert.Equal(AlfaChatClient.PickupState.Retry, result.State);
    }

    [Fact]
    public async Task Polling_keeps_going_through_pending_and_network_errors_until_the_key_arrives()
    {
        var h = new Handler((_, n) => n switch
        {
            0 => Json(HttpStatusCode.Accepted, """{"status":"pending"}"""),
            1 => throw new HttpRequestException("wifi dropped"),
            2 => Json(HttpStatusCode.BadGateway),
            3 => Json(HttpStatusCode.Accepted, """{"status":"pending"}"""),
            _ => Json(HttpStatusCode.OK, $$"""{"key":"{{Key}}"}"""),
        });
        var seen = new List<AlfaChatClient.PickupState>();

        var result = await AlfaChatClient.PollPickupAsync(Http(h), AlfaChatClient.DefaultBaseUrl, Ticket,
            TimeSpan.FromSeconds(5), NoWait, onAttempt: r => seen.Add(r.State));

        Assert.Equal(AlfaChatClient.PickupState.Issued, result.State);
        Assert.Equal(Key, result.Key);
        Assert.Equal(5, h.Calls);
        Assert.Equal(new[]
        {
            AlfaChatClient.PickupState.Pending, AlfaChatClient.PickupState.Retry, AlfaChatClient.PickupState.Retry,
            AlfaChatClient.PickupState.Pending, AlfaChatClient.PickupState.Issued,
        }, seen);
    }

    [Theory]
    [InlineData(HttpStatusCode.Gone, AlfaChatClient.PickupState.Closed)]
    [InlineData(HttpStatusCode.Conflict, AlfaChatClient.PickupState.AlreadyIssued)]
    [InlineData(HttpStatusCode.NotFound, AlfaChatClient.PickupState.NotFound)]
    public async Task Polling_stops_only_when_the_server_closes(HttpStatusCode final, AlfaChatClient.PickupState expected)
    {
        var h = new Handler((_, n) => n < 3 ? Json(HttpStatusCode.Accepted) : Json(final));

        var result = await AlfaChatClient.PollPickupAsync(Http(h), AlfaChatClient.DefaultBaseUrl, Ticket, TimeSpan.FromSeconds(5), NoWait);

        Assert.Equal(expected, result.State);
        Assert.Equal(4, h.Calls);
    }

    [Fact]
    public async Task Polling_does_not_stop_by_itself_after_the_expiry_time()
    {
        var expired = Ticket with { ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-5) };
        var h = new Handler((_, n) => n < 2 ? Json(HttpStatusCode.Accepted) : Json(HttpStatusCode.OK, $$"""{"key":"{{Key}}"}"""));

        var result = await AlfaChatClient.PollPickupAsync(Http(h), AlfaChatClient.DefaultBaseUrl, expired, TimeSpan.FromSeconds(5), NoWait);

        Assert.Equal(AlfaChatClient.PickupState.Issued, result.State);
    }

    [Fact]
    public async Task Polling_waits_the_interval_and_stops_on_cancel()
    {
        using var cts = new CancellationTokenSource();
        var waits = new List<TimeSpan>();
        var h = new Handler((_, n) =>
        {
            if (n == 1) cts.Cancel();
            return Json(HttpStatusCode.Accepted);
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => AlfaChatClient.PollPickupAsync(Http(h),
            AlfaChatClient.DefaultBaseUrl, Ticket, TimeSpan.FromSeconds(5),
            (t, ct) => { waits.Add(t); ct.ThrowIfCancellationRequested(); return Task.CompletedTask; },
            cancellationToken: cts.Token));

        Assert.All(waits, t => Assert.Equal(TimeSpan.FromSeconds(5), t));
        Assert.Equal(2, h.Calls);
    }

    // ---------- 비밀값이 글자로 새지 않음 ----------

    [Fact]
    public void Secrets_never_appear_when_results_are_printed()
    {
        var ticket = Ticket.ToString();
        var pickup = new AlfaChatClient.PickupResult(AlfaChatClient.PickupState.Issued, Key).ToString();
        var request = new AlfaChatClient.RequestResult(AlfaChatClient.RequestOutcome.Created, Ticket).ToString();

        Assert.DoesNotContain(Secret, ticket);
        Assert.Contains("C0FFEE", ticket);
        Assert.DoesNotContain(Secret, request);
        Assert.DoesNotContain(Key, pickup);
        Assert.DoesNotContain(AlfaChatClient.KeyPrefix, pickup);
    }

    // ---------- A6 ----------

    [Fact]
    public async Task Unsupported_reply_gets_a_friendly_line()
    {
        var h = new Handler(_ => Json(HttpStatusCode.OK, """{"reply":"알림은 여기서 못 맞춰요.","source":"alorys","status":"unsupported"}"""));
        var client = new AlfaChatClient(Http(h), () => Key, new AiSettings());

        var answer = await client.AskAsync("내일 9시 알림");
        var reply = await client.ChatAsync("내일 9시 알림");

        Assert.StartsWith("알림은 여기서 못 맞춰요.", answer);
        Assert.Contains(AlfaChatClient.UnsupportedNote, answer);
        Assert.Equal("unsupported", reply.Status);
        Assert.Equal("alorys", reply.Source);
    }

    [Fact]
    public async Task Completed_reply_is_left_as_it_is()
    {
        var h = new Handler(_ => Json(HttpStatusCode.OK, """{"reply":"오늘 할 일 3개예요.","source":"alorys","status":"completed"}"""));
        var client = new AlfaChatClient(Http(h), () => Key, new AiSettings());

        Assert.Equal("오늘 할 일 3개예요.", await client.AskAsync("알파 오늘 할 일"));
    }

    [Fact]
    public async Task Chat_401_tells_the_link_it_was_cut()
    {
        var cut = 0;
        var h = new Handler(_ => Json(HttpStatusCode.Unauthorized, """{"error":"PET_KEY_REQUIRED"}"""));
        var client = new AlfaChatClient(Http(h), () => Key, new AiSettings(), () => cut++);

        var ex = await Assert.ThrowsAsync<AiException>(() => client.AskAsync("안녕"));

        Assert.Equal(1, cut);
        Assert.Contains("끊겼어요", ex.Message);
    }

    [Fact]
    public async Task Chat_other_errors_do_not_cut_the_link()
    {
        var cut = 0;
        var h = new Handler(_ => Json(HttpStatusCode.ServiceUnavailable, """{"error":"ALORYS_UNAVAILABLE"}"""));
        var client = new AlfaChatClient(Http(h), () => Key, new AiSettings(), () => cut++);

        await Assert.ThrowsAsync<AiException>(() => client.AskAsync("안녕"));

        Assert.Equal(0, cut);
    }

    // ---------- A7 ----------

    [Fact]
    public async Task Me_returns_the_device_name_the_hub_knows()
    {
        var h = new Handler(_ => Json(HttpStatusCode.OK,
            """{"linkId":"00000000-0000-0000-0000-0000000000b2","deviceName":"windows-office","petName":"레서판다","status":"approved"}"""));

        var me = await AlfaChatClient.MeAsync(Http(h), AlfaChatClient.DefaultBaseUrl, Key);

        Assert.Equal(AlfaChatClient.LinkState.Connected, me.State);
        Assert.Equal("windows-office", me.DeviceName);
        Assert.Equal(HttpMethod.Get, h.Last!.Method);
    }

    [Fact]
    public async Task Me_network_failure_is_unreachable()
    {
        var me = await AlfaChatClient.MeAsync(Http(new Handler(_ => throw new HttpRequestException("down"))),
            AlfaChatClient.DefaultBaseUrl, Key);

        Assert.Equal(AlfaChatClient.LinkState.Unreachable, me.State);
    }

    // ---------- A8 ----------

    [Theory]
    [InlineData(HttpStatusCode.OK, """{"status":"revoked"}""", AlfaChatClient.UnlinkOutcome.Revoked)]
    [InlineData(HttpStatusCode.Unauthorized, """{"error":"PET_KEY_REQUIRED"}""", AlfaChatClient.UnlinkOutcome.AlreadyUnlinked)]
    [InlineData(HttpStatusCode.ServiceUnavailable, "", AlfaChatClient.UnlinkOutcome.Unreachable)]
    [InlineData(HttpStatusCode.NotFound, "", AlfaChatClient.UnlinkOutcome.Failed)]
    public async Task Unlink_maps_the_answer(HttpStatusCode status, string body, AlfaChatClient.UnlinkOutcome expected)
    {
        var h = new Handler(_ => Json(status, body));

        var outcome = await AlfaChatClient.UnlinkAsync(Http(h), AlfaChatClient.DefaultBaseUrl, Key);

        Assert.Equal(expected, outcome);
        Assert.Equal(HttpMethod.Post, h.Last!.Method);
        Assert.Equal("https://alfa.uziseignk.com/api/pet/unlink", h.Last.RequestUri!.ToString());
        Assert.Equal(Key, h.Last.Headers.Authorization!.Parameter);
    }

    [Fact]
    public async Task Unlink_without_a_key_sends_nothing()
    {
        var h = new Handler(_ => Json(HttpStatusCode.OK));

        Assert.Equal(AlfaChatClient.UnlinkOutcome.AlreadyUnlinked, await AlfaChatClient.UnlinkAsync(Http(h), AlfaChatClient.DefaultBaseUrl, null));
        Assert.Equal(0, h.Calls);
    }

    [Fact]
    public async Task Unlink_network_failure_is_unreachable()
    {
        Assert.Equal(AlfaChatClient.UnlinkOutcome.Unreachable,
            await AlfaChatClient.UnlinkAsync(Http(new Handler(_ => throw new HttpRequestException("down"))), AlfaChatClient.DefaultBaseUrl, Key));
    }

    // ---------- C4 옛 주소 이전 ----------

    [Theory]
    [InlineData("http://127.0.0.1:7701")]
    [InlineData("http://127.0.0.1:7701/")]
    [InlineData("HTTP://LOCALHOST:7701")]
    [InlineData("")]
    public void Old_local_default_moves_to_the_hub(string old)
    {
        var s = new AiSettings { AlfaBaseUrl = old };

        s.MigrateLegacy();

        Assert.Equal(AlfaChatClient.DefaultBaseUrl, s.AlfaBaseUrl);
    }

    [Theory]
    [InlineData("https://alfa.uziseignk.com")]
    [InlineData("http://127.0.0.1:7800")]
    [InlineData("https://dev.uziseignk.com")]
    public void Other_addresses_are_kept(string url)
    {
        var s = new AiSettings { AlfaBaseUrl = url };

        Assert.False(s.MigrateLegacy());
        Assert.Equal(url, s.AlfaBaseUrl);
    }

    [Fact]
    public void Saved_11_03_settings_file_loads_with_the_hub_address()
    {
        const string saved = """
            { "Provider": 2, "AlfaBaseUrl": "http://127.0.0.1:7701", "AlfaDeviceName": "windows-office", "TimeoutSeconds": 60 }
            """;
        var s = JsonSerializer.Deserialize<AiSettings>(saved)!;

        Assert.True(s.MigrateLegacy());
        Assert.Equal(AlfaChatClient.DefaultBaseUrl, s.AlfaBaseUrl);
        Assert.Equal("windows-office", s.AlfaDeviceName);
        Assert.Equal(AiProvider.Alfa, s.Provider);
        Assert.Null(s.AlfaLinkedAt);
    }
}
