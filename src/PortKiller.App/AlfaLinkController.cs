using System.Net.Http;
using System.Windows.Threading;
using PortKiller.Core;
using PortKiller.Platform;

namespace PortKiller.App;

/// <summary>
/// ALFA 연동을 펫 본체(주 프로세스)에서 돌린다. 창은 이 객체를 보여 주기만 한다.
/// <list type="bullet">
/// <item>W3 받아 가기: 요청 뒤 5초마다, 서버가 200/409/410/404 를 줄 때까지. 연동 창을 닫아도 계속(C1).</item>
/// <item>W5 연결 확인: 키가 있으면 5분마다 A7.</item>
/// <item>A8 연동 끊기.</item>
/// </list>
/// <para>받아 가기 비밀값은 이 객체의 메모리에만 있다(<see cref="_ticket"/>). 앱을 끄면 사라지고, 대기도 함께 끝난다.
/// 키는 받자마자 <see cref="ApiKeyStore"/>(DPAPI)에 넣고 여기에는 두지 않는다. 둘 다 로그·화면에 내보내지 않는다.</para>
/// <para>모든 상태 변경은 화면 스레드에서 일어난다. <see cref="Changed"/> 도 화면 스레드에서 올라온다.</para>
/// </summary>
internal sealed class AlfaLinkController : IDisposable
{
    public static readonly TimeSpan PickupInterval = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan FirstCheckDelay = TimeSpan.FromSeconds(20);

    /// <summary>요청 하나를 기다리는 한도. 공용 HttpClient 는 시간 제한이 없어서, 이게 없으면 걸린 요청 하나가 반복을 멈춘다.</summary>
    private static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(15);

    private readonly HttpClient _http;
    private readonly ApiKeyStore _keys;
    private readonly AiSettings _settings;
    private readonly AiSettingsStore _store;
    private readonly Dispatcher _ui;
    private readonly CancellationTokenSource _life = new();

    private CancellationTokenSource? _poll;
    private AlfaChatClient.LinkTicket? _ticket;
    private bool _busy;

    public AlfaLinkController(HttpClient http, ApiKeyStore keys, AiSettings settings, AiSettingsStore store, Dispatcher ui)
    {
        _http = http;
        _keys = keys;
        _settings = settings;
        _store = store;
        _ui = ui;
        State = PetLinkState.Initial(HasKey, settings.AlfaLinkedAt);
    }

    public PetLinkState State { get; private set; }

    /// <summary>요청·확인·끊기 중. 창이 단추를 잠그는 데 쓴다.</summary>
    public bool Busy => _busy;

    /// <summary>(앞, 뒤). 화면 스레드에서.</summary>
    public event Action<PetLinkState, PetLinkState>? Changed;

    /// <summary>단추 잠금처럼 상태 말고 바뀐 것이 있을 때.</summary>
    public event Action? BusyChanged;

    public bool HasKey => AlfaChatClient.LooksLikeKey(_keys.Get(AiProvider.Alfa));

    /// <summary>ALFA 목록에 보일 이 기기 이름. 비어 있으면 기본 <c>windows-office</c>.</summary>
    public string DeviceName => string.IsNullOrWhiteSpace(_settings.AlfaDeviceName) ? AlfaChatClient.DefaultDeviceName : _settings.AlfaDeviceName;

    public string BaseUrl => string.IsNullOrWhiteSpace(_settings.AlfaBaseUrl) ? AlfaChatClient.DefaultBaseUrl : _settings.AlfaBaseUrl;

    private void Set(PetLinkState next)
    {
        var before = State;
        State = next;
        Changed?.Invoke(before, next);
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        BusyChanged?.Invoke();
    }

    // ---------- A1 + W3 ----------

    /// <summary>연동을 요청하고 받아 가기 반복을 시작한다. 실패하면 사람이 읽을 한 줄을, 성공하면 null 을 돌려준다.</summary>
    public async Task<string?> RequestAsync(string deviceInput, string baseUrlInput, string petName, string species)
    {
        if (_busy) return null;
        var device = AlfaChatClient.NormalizeDeviceName(deviceInput);
        if (!AlfaChatClient.IsValidDeviceName(device))
            return "기기 이름은 영문 소문자·숫자·. - _ 로 60자 안이에요(예: windows-office).";
        var url = string.IsNullOrWhiteSpace(baseUrlInput) ? AlfaChatClient.DefaultBaseUrl : baseUrlInput.Trim();
        if (!AlfaChatClient.IsAllowedBase(url))
            return "ALFA 주소는 허브(https://…uziseignk.com)나 이 PC 만 돼요.";

        SetBusy(true);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_life.Token);
            timeout.CancelAfter(AttemptTimeout);
            AlfaChatClient.RequestResult result;
            try
            {
                result = await AlfaChatClient.RequestLinkAsync(_http, url, device, petName, species, "windows", timeout.Token);
            }
            catch (OperationCanceledException) when (!_life.IsCancellationRequested)
            {
                result = new(AlfaChatClient.RequestOutcome.Unreachable, null);
            }
            if (result.Ticket is not { } ticket) return result.Message;

            _settings.AlfaBaseUrl = url;
            _settings.AlfaDeviceName = device;
            TrySaveSettings();

            StopPolling();
            _ticket = ticket;
            Set(State.Requested(ticket.ConfirmationCode, ticket.ExpiresAt));
            _ = PollAsync(ticket, url);
            return null;
        }
        catch (OperationCanceledException)
        {
            return null;   // 앱을 끄는 중.
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task PollAsync(AlfaChatClient.LinkTicket ticket, string url)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(_life.Token);
        _poll = cts;
        try
        {
            var result = await AlfaChatClient.PollPickupAsync(_http, url, ticket, PickupInterval,
                attemptTimeout: AttemptTimeout, cancellationToken: cts.Token);
            if (!ReferenceEquals(_ticket, ticket)) return;   // 그사이 다시 요청했거나 그만뒀다.
            _ticket = null;

            var now = DateTimeOffset.UtcNow;
            if (result.State == AlfaChatClient.PickupState.Issued)
            {
                _keys.Set(AiProvider.Alfa, result.Key);
                _settings.AlfaLinkedAt = now;
                TrySaveSettings();
            }
            Set(State.Pickup(result.State, now));
        }
        catch (OperationCanceledException)
        {
            // CANCEL·다시 요청·앱 종료.
        }
        catch (Exception ex)
        {
            // 키 저장이 막힌 경우 등. 비밀값은 메시지에 없다(예외 종류·메시지만).
            StartupLog.Crash("alfa-pickup", ex);
            if (ReferenceEquals(_ticket, ticket))
            {
                _ticket = null;
                Set(State.Pickup(AlfaChatClient.PickupState.NotFound, DateTimeOffset.UtcNow));
            }
        }
        finally
        {
            if (ReferenceEquals(_poll, cts)) _poll = null;
            cts.Dispose();
        }
    }

    private void StopPolling()
    {
        try { _poll?.Cancel(); } catch (ObjectDisposedException) { }
        _poll = null;
        _ticket = null;
    }

    /// <summary>CANCEL. 받아 가기를 멈추고 비밀값을 버린다.</summary>
    public void Cancel()
    {
        if (!State.IsWaiting) return;
        StopPolling();
        Set(State.Cancelled());
    }

    // ---------- W5 ----------

    /// <summary>지금 한 번 확인(A7). CHECK 단추와 5분 반복이 같이 쓴다.</summary>
    public async Task CheckAsync()
    {
        var key = _keys.Get(AiProvider.Alfa);
        if (!AlfaChatClient.LooksLikeKey(key) || State.IsWaiting || _busy) return;
        SetBusy(true);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_life.Token);
            timeout.CancelAfter(AttemptTimeout);
            AlfaChatClient.MeResult me;
            try
            {
                me = await AlfaChatClient.MeAsync(_http, BaseUrl, key, timeout.Token);
            }
            catch (OperationCanceledException) when (!_life.IsCancellationRequested)
            {
                me = new(AlfaChatClient.LinkState.Unreachable);
            }
            Set(State.Checked(me.State, DateTimeOffset.UtcNow));
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            SetBusy(false);
        }
    }

    /// <summary>앱이 떠 있는 동안 키가 있으면 5분마다 확인한다. 기록은 단계가 바뀔 때만 남긴다(바깥 <see cref="Changed"/> 에서, C8).</summary>
    public async Task RunChecksAsync()
    {
        try
        {
            await Task.Delay(FirstCheckDelay, _life.Token);
            while (!_life.IsCancellationRequested)
            {
                if (State.IsLinked) await CheckAsync();
                await Task.Delay(CheckInterval, _life.Token);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>묻기가 401 을 받았다(C9). 어느 스레드에서 불려도 된다.</summary>
    public void ReportUnauthorized()
        => _ui.BeginInvoke(() => Set(State.Unauthorized(DateTimeOffset.UtcNow)));

    // ---------- A8 ----------

    /// <summary>UNLINK. 서버가 끊었다고(200) 하거나 이미 끊겼다고(401) 하면 이 펫의 키도 지운다.</summary>
    public async Task<string?> UnlinkAsync()
    {
        if (_busy) return null;
        SetBusy(true);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_life.Token);
            timeout.CancelAfter(AttemptTimeout);
            AlfaChatClient.UnlinkOutcome outcome;
            try
            {
                outcome = await AlfaChatClient.UnlinkAsync(_http, BaseUrl, _keys.Get(AiProvider.Alfa), timeout.Token);
            }
            catch (OperationCanceledException) when (!_life.IsCancellationRequested)
            {
                outcome = AlfaChatClient.UnlinkOutcome.Unreachable;
            }
            switch (outcome)
            {
                case AlfaChatClient.UnlinkOutcome.Revoked:
                case AlfaChatClient.UnlinkOutcome.AlreadyUnlinked:
                    ForgetKey();
                    return null;
                case AlfaChatClient.UnlinkOutcome.Unreachable:
                    return "ALFA 에 닿지 않아 끊지 못했어요. 인터넷을 확인하거나 ALFA 설정 › 기기에서 [REVOKE] 해 주세요.";
                default:
                    return "연동을 끊지 못했어요. ALFA 설정 › 기기에서 [REVOKE] 해 주세요.";
            }
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        finally
        {
            SetBusy(false);
        }
    }

    /// <summary>키를 지웠다('열쇠 전부 지우기' 포함). 대기 중이면 대기는 그대로 둔다.</summary>
    public void KeysCleared()
    {
        _settings.AlfaLinkedAt = null;
        TrySaveSettings();
        if (!State.IsWaiting) Set(State.Unlinked());
    }

    private void ForgetKey()
    {
        _keys.Set(AiProvider.Alfa, null);
        KeysCleared();
    }

    private void TrySaveSettings()
    {
        try { _store.Save(_settings); }
        catch (Exception ex) { StartupLog.Crash("alfa-settings", ex); }
    }

    public void Dispose()
    {
        _life.Cancel();
        _ticket = null;
    }
}
