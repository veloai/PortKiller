using System.IO;
using System.Net.Http;
using System.Windows;
using PortKiller.Core;
using PortKiller.Platform;

namespace PortKiller.App;

/// <summary>
/// AI 열쇠와 모델을 등록하는 창.
///
/// <para>열쇠는 <see cref="System.Windows.Controls.PasswordBox"/> 로만 받는다 — 화면에 그대로
/// 뜨면 어깨너머와 화면 공유로 새고, 일반 TextBox 는 실행 취소 기록에도 남는다.</para>
///
/// <para><b>등록된 열쇠를 다시 보여 주지 않는다.</b> "등록됨"이라는 사실만 알린다.
/// 되읽어 화면에 채우는 순간, 암호화해서 보관한 의미가 없어진다.
/// 바꾸고 싶으면 새로 적으면 되고, 비워 두면 기존 것이 유지된다.</para>
/// </summary>
public partial class AiSettingsWindow : Window
{
    private readonly AiSettings _settings;
    private readonly ApiKeyStore _keys;
    private readonly AiSettingsStore _store;
    private readonly HttpClient _http;
    private readonly Func<(string Name, string Species)> _pet;

    /// <summary>연동 요청 뒤 승인을 기다리는 동안 키 받아 가기를 되풀이한다. 창을 닫으면 멈춘다.</summary>
    private CancellationTokenSource? _linking;

    /// <summary>저장했을 때. 물어보기 창의 안내줄을 다시 그리게 하려고 알린다.</summary>
    public event Action? Saved;

    public AiSettingsWindow(AiSettings settings, ApiKeyStore keys, AiSettingsStore store, HttpClient http,
                            Func<(string Name, string Species)> pet)
    {
        InitializeComponent();

        _settings = settings;
        _keys = keys;
        _store = store;
        _http = http;
        _pet = pet;

        Loaded += (_, _) => Fill();
        Closed += (_, _) => _linking?.Cancel();
    }

    private void Fill()
    {
        UseGemini.IsChecked = _settings.Provider == AiProvider.Gemini;
        UseOpenAi.IsChecked = _settings.Provider == AiProvider.OpenAi;
        UseAlfa.IsChecked = _settings.Provider == AiProvider.Alfa;
        AlfaBaseUrl.Text = _settings.AlfaBaseUrl;
        AlfaDevice.Text = string.IsNullOrWhiteSpace(_settings.AlfaDeviceName)
            ? AlfaChatClient.NormalizeDeviceName(Environment.MachineName)
            : _settings.AlfaDeviceName;

        GeminiModel.Text = _settings.ModelFor(AiProvider.Gemini);
        OpenAiModel.Text = _settings.ModelFor(AiProvider.OpenAi);
        Persona.Text = _settings.Persona;

        RefreshKeyState();

        KeyPathText.Text = "저장 위치: " + Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PortKillerPet", "keys.dat");
    }

    private void RefreshKeyState()
    {
        GeminiState.Text = _keys.Has(AiProvider.Gemini)
            ? "등록됨 (바꾸려면 새로 적으세요. 비워 두면 그대로 둡니다)"
            : "등록되지 않음";
        OpenAiState.Text = _keys.Has(AiProvider.OpenAi)
            ? "등록됨 (바꾸려면 새로 적으세요. 비워 두면 그대로 둡니다)"
            : "등록되지 않음";
        AlfaState.Text = (_keys.Get(AiProvider.Alfa) ?? "").StartsWith(AlfaChatClient.KeyPrefix, StringComparison.Ordinal)
            ? "연동됨 — 이 펫 전용 키가 있어요 (ALFA 설정에서 해제하면 바로 끊겨요)"
            : "연동 안 됨";
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _settings.Provider = UseAlfa.IsChecked == true ? AiProvider.Alfa
                : UseOpenAi.IsChecked == true ? AiProvider.OpenAi
                : AiProvider.Gemini;
            if (!TryReadAlfa(out var alfaUrl, out var device)) return;
            _settings.AlfaBaseUrl = alfaUrl;
            _settings.AlfaDeviceName = device;
            _settings.GeminiModel = GeminiModel.Text.Trim();
            _settings.OpenAiModel = OpenAiModel.Text.Trim();
            _settings.Persona = string.IsNullOrWhiteSpace(Persona.Text)
                ? AiSettings.DefaultPersona
                : Persona.Text.Trim();
            _store.Save(_settings);

            // 빈 칸은 "지우기"가 아니라 "그대로 두기"다. 모델 이름만 고치러 들어왔다가
            // 열쇠가 지워지면 왜 안 되는지 알 길이 없다. 지우기는 아래 버튼이 따로 있다.
            SaveKeyIfTyped(AiProvider.Gemini, GeminiKey.Password);
            SaveKeyIfTyped(AiProvider.OpenAi, OpenAiKey.Password);

            GeminiKey.Clear();
            OpenAiKey.Clear();
            RefreshKeyState();

            SaveState.Text = "저장했어요.";
            Saved?.Invoke();
        }
        catch (Exception)
        {
            SaveState.Text = "저장하지 못했어요. 폴더 권한을 확인해 주세요.";
        }
    }

    /// <summary>ALFA 주소·기기 이름 칸을 읽는다. 키를 보내면 안 되는 주소면 저장도 요청도 하지 않는다.</summary>
    private bool TryReadAlfa(out string url, out string device)
    {
        url = AlfaBaseUrl.Text.Trim();
        if (url.Length == 0) url = AlfaChatClient.DefaultBaseUrl;
        device = AlfaChatClient.NormalizeDeviceName(AlfaDevice.Text);
        if (!AlfaChatClient.IsAllowedBase(url))
        {
            SaveState.Text = "ALFA 주소는 허브(https://…uziseignk.com)나 이 PC 만 돼요.";
            return false;
        }
        return true;
    }

    /// <summary>
    /// ALFA 에 연동을 요청하고 확인 코드를 보여 준 뒤, 승인될 때까지 5초마다 키 받아 가기를 시도한다.
    /// 받은 키는 바로 DPAPI 로 저장하고, 받아 가기 비밀값은 메모리에만 둔다.
    /// </summary>
    private async void AlfaLinkButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadAlfa(out var url, out var device)) return;
        if (device.Length == 0)
        {
            SaveState.Text = "기기 이름을 적어 주세요(예: windows-office).";
            return;
        }

        _linking?.Cancel();
        var cts = new CancellationTokenSource();
        _linking = cts;
        AlfaLinkButton.IsEnabled = false;
        AlfaLinkStatus.Text = "ALFA 에 연동을 요청하는 중…";
        try
        {
            var pet = _pet();
            var ticket = await AlfaChatClient.RequestLinkAsync(_http, url, device, pet.Name, pet.Species, "windows", cts.Token);
            _settings.AlfaBaseUrl = url;
            _settings.AlfaDeviceName = device;
            _store.Save(_settings);

            AlfaCode.Text = ticket.ConfirmationCode;
            AlfaCode.Visibility = Visibility.Visible;
            AlfaLinkStatus.Text = $"ALFA 설정 › 기기 › 펫 연동에서 '{device}' 의 코드가 {ticket.ConfirmationCode} 인지 보고 승인하세요. "
                                  + $"{ticket.ExpiresAt.ToLocalTime():HH:mm} 까지 기다립니다.";

            while (!cts.IsCancellationRequested && DateTimeOffset.UtcNow < ticket.ExpiresAt)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), cts.Token);
                var result = await AlfaChatClient.PickupAsync(_http, url, ticket, cts.Token);
                if (result.State == AlfaChatClient.PickupState.Pending) continue;
                if (result.State == AlfaChatClient.PickupState.Issued)
                {
                    _keys.Set(AiProvider.Alfa, result.Key);
                    AlfaLinkStatus.Text = "연동됐어요! 위에서 'ALFA'를 고르고 저장하면 물어보기가 ALFA 로 갑니다.";
                    Saved?.Invoke();
                }
                else
                {
                    AlfaLinkStatus.Text = result.State == AlfaChatClient.PickupState.Closed
                        ? "ALFA 에서 거절됐거나 시간이 지났어요. 다시 요청해 주세요."
                        : "이 요청으로는 키를 받을 수 없어요. 다시 요청해 주세요.";
                }
                break;
            }
            if (DateTimeOffset.UtcNow >= ticket.ExpiresAt && AlfaCode.Visibility == Visibility.Visible && !_keys.Has(AiProvider.Alfa))
                AlfaLinkStatus.Text = "승인 시간이 지났어요. 다시 요청해 주세요.";
        }
        catch (OperationCanceledException)
        {
            // 창을 닫았거나 다시 요청했다.
        }
        catch (AiException ex)
        {
            AlfaLinkStatus.Text = ex.Message;
        }
        catch (Exception)
        {
            AlfaLinkStatus.Text = "ALFA 에 연동을 요청하지 못했어요.";
        }
        finally
        {
            if (ReferenceEquals(_linking, cts))
            {
                AlfaCode.Visibility = Visibility.Collapsed;
                AlfaLinkButton.IsEnabled = true;
                RefreshKeyState();
            }
        }
    }

    private void SaveKeyIfTyped(AiProvider provider, string typed)
    {
        if (string.IsNullOrWhiteSpace(typed)) return;
        _keys.Set(provider, typed);
    }

    private void ClearKeysButton_Click(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(
            "등록한 AI 열쇠를 전부 지울까요?\n지운 뒤에는 다시 등록해야 물어볼 수 있어요.",
            "열쇠 지우기",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);
        if (answer != MessageBoxResult.OK) return;

        _keys.ClearAll();
        GeminiKey.Clear();
        OpenAiKey.Clear();
        RefreshKeyState();
        SaveState.Text = "열쇠를 지웠어요.";
        Saved?.Invoke();
    }
}
