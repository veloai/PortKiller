using System.IO;
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

    /// <summary>저장했을 때. 물어보기 창의 안내줄을 다시 그리게 하려고 알린다.</summary>
    public event Action? Saved;

    public AiSettingsWindow(AiSettings settings, ApiKeyStore keys, AiSettingsStore store)
    {
        InitializeComponent();

        _settings = settings;
        _keys = keys;
        _store = store;

        Loaded += (_, _) => Fill();
    }

    private void Fill()
    {
        UseGemini.IsChecked = _settings.Provider == AiProvider.Gemini;
        UseOpenAi.IsChecked = _settings.Provider == AiProvider.OpenAi;

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
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _settings.Provider = UseOpenAi.IsChecked == true ? AiProvider.OpenAi : AiProvider.Gemini;
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
