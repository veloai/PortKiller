using System.Windows;
using System.Windows.Input;
using PortKiller.Core;

namespace PortKiller.App;

/// <summary>
/// 프롬프트를 적고 엔터를 누르면 AI 에게 묻고 답을 보여 준다.
///
/// <para>한 번 묻고 한 번 답받는 것이 전부다. 대화 이력을 쌓지 않고, 디스크에도 남기지 않는다.
/// 그래야 보안 검토에 "질문과 답은 저장하지 않는다"고 그대로 적을 수 있다.</para>
///
/// <para>답은 말풍선이 아니라 이 창에 나온다. 말풍선은 몇 글자짜리라 AI 답을 담으면
/// 화면을 덮어 버린다. 대신 펫이 짧게 한마디 하도록 <see cref="Answered"/> 로 알린다.</para>
/// </summary>
public partial class AskWindow : Window
{
    private readonly Func<IAiClient> _clientFactory;
    private readonly AiSettings _settings;
    private readonly Func<bool> _hasKey;

    private CancellationTokenSource? _inFlight;

    /// <summary>답을 받았을 때. 펫이 한마디 하게 하려고 바깥에 넘긴다.</summary>
    public event Action<string>? Answered;

    public AskWindow(Func<IAiClient> clientFactory, AiSettings settings, Func<bool> hasKey)
    {
        InitializeComponent();

        _clientFactory = clientFactory;
        _settings = settings;
        _hasKey = hasKey;

        Loaded += (_, _) =>
        {
            RefreshProviderLabel();
            PromptBox.Focus();
        };

        // 창을 닫으면 가던 요청도 끊는다. 안 그러면 답이 없는 창에 답을 쓰려다 터진다.
        Closed += (_, _) => CancelInFlight();
    }

    /// <summary>지금 어디로 묻는지, 열쇠는 있는지 한 줄로 알린다.</summary>
    public void RefreshProviderLabel()
    {
        var name = AiSettings.DisplayName(_settings.Provider);
        var model = _settings.ModelFor(_settings.Provider);

        if (_hasKey())
        {
            ProviderLabel.Text = $"{name} · {model}";
            StatusText.Text = "";
        }
        else
        {
            ProviderLabel.Text = $"{name} · {model}";
            StatusText.Text = $"{name} 열쇠가 아직 없어요. 트레이 메뉴의 'AI 설정'에서 등록해 주세요.";
        }
    }

    private void PromptBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter && e.Key != Key.Return) return;

        // Shift+Enter 는 줄바꿈으로 남긴다. 긴 질문에서 문단을 못 쓰면 쓰기가 불편하다.
        if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift) return;

        // 한글 조합 중에 눌린 엔터는 "글자 확정"이지 "전송"이 아니다.
        // 이걸 전송으로 받으면 마지막 글자가 빠진 채로 날아간다.
        if (e.ImeProcessedKey != Key.None) return;

        e.Handled = true;
        _ = SendAsync();
    }

    private void SendButton_Click(object sender, RoutedEventArgs e) => _ = SendAsync();

    private async Task SendAsync()
    {
        // 이미 묻고 있는 중이면 무시한다. 엔터를 연타해도 요금이 여러 번 나가지 않게.
        if (_inFlight is not null) return;

        var prompt = PromptBox.Text.Trim();
        if (prompt.Length == 0) return;

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Clamp(_settings.TimeoutSeconds, 5, 300)));
        _inFlight = cts;

        SetBusy(true);
        StatusText.Text = "";
        AnswerText.Text = "…";

        try
        {
            var answer = await _clientFactory().AskAsync(prompt, cts.Token);
            AnswerText.Text = answer;
            AnswerScroll.ScrollToTop();
            PromptBox.Clear();
            Answered?.Invoke(answer);
        }
        catch (OperationCanceledException)
        {
            // 창을 닫았거나 시간이 넘었다. 창이 이미 없으면 아무 말도 하지 않는다.
            if (IsLoaded) StatusText.Text = "시간이 넘어서 그만뒀어요. 다시 시도해 주세요.";
        }
        catch (AiException ex)
        {
            AnswerText.Text = "";
            StatusText.Text = ex.Message;
        }
        catch (Exception)
        {
            // 예상 못 한 것까지 여기서 잡는다. 물어보다 앱이 통째로 죽으면 펫도 같이 죽는다.
            AnswerText.Text = "";
            StatusText.Text = "알 수 없는 문제로 실패했어요. 잠시 뒤에 다시 시도해 주세요.";
        }
        finally
        {
            _inFlight = null;
            cts.Dispose();
            SetBusy(false);
            if (IsLoaded) PromptBox.Focus();
        }
    }

    private void SetBusy(bool busy)
    {
        SendButton.IsEnabled = !busy;
        SendButton.Content = busy ? "묻는 중…" : "보내기";
        PromptBox.IsEnabled = !busy;
    }

    private void CancelInFlight()
    {
        try
        {
            _inFlight?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // 이미 끝난 요청이다.
        }
    }
}
