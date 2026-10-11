using System.Globalization;
using System.Speech.Recognition;
using PortKiller.Game.Models;

namespace PortKiller.Game.Voice;

/// <summary>
/// 윈도우 내장 음성인식 엔진(System.Speech) 기반의 로컬 초저지연(50ms) 키워드 스포팅 서비스.
/// 
/// 클라우드 전송 없이 로컬에서 즉시 인식하며,
/// PS5 패드 마이크 또는 기본 마이크로부터 명령어를 감지합니다.
/// </summary>
public sealed class SpeechRecognitionService : IDisposable
{
    private SpeechRecognitionEngine? _engine;
    private readonly Dictionary<string, VoiceCommand> _commandLookup = new(StringComparer.OrdinalIgnoreCase);
    private bool _isListening;
    private bool _pttActive = true; // PTT 기본 상태

    public event Action<VoiceCommand, float>? CommandRecognized;
    public event Action<string, float>? SpeechDetected;
    public event Action<string>? StatusChanged;

    public bool IsAvailable { get; private set; }
    public bool IsListening => _isListening;

    public SpeechRecognitionService()
    {
        InitializeEngine();
    }

    private void InitializeEngine()
    {
        try
        {
            // 한국어 또는 영문 컬처 시도
            CultureInfo culture;
            try
            {
                culture = new CultureInfo("ko-KR");
                _engine = new SpeechRecognitionEngine(culture);
            }
            catch
            {
                culture = CultureInfo.CurrentCulture;
                _engine = new SpeechRecognitionEngine(culture);
            }

            _engine.SpeechRecognized += OnSpeechRecognized;
            _engine.SpeechHypothesized += OnSpeechHypothesized;
            _engine.SetInputToDefaultAudioDevice();
            IsAvailable = true;
            StatusChanged?.Invoke($"음성 인식 엔진 준비됨 ({culture.Name})");
        }
        catch (Exception ex)
        {
            IsAvailable = false;
            StatusChanged?.Invoke($"음성 인식 초기화 실패: {ex.Message}");
        }
    }

    /// <summary>
    /// 프로필의 음성 명령 목록으로 문법(Grammar)을 재구성합니다.
    /// </summary>
    public void LoadCommands(IEnumerable<VoiceCommand> commands)
    {
        if (_engine == null || !IsAvailable) return;

        _commandLookup.Clear();
        var choices = new Choices();
        int count = 0;

        foreach (var cmd in commands)
        {
            if (string.IsNullOrWhiteSpace(cmd.Phrase)) continue;
            choices.Add(cmd.Phrase);
            _commandLookup[cmd.Phrase] = cmd;
            count++;
        }

        if (count == 0) return;

        try
        {
            var gb = new GrammarBuilder(choices);
            var grammar = new Grammar(gb) { Name = "PortKillerGameVoiceGrammar" };

            _engine.UnloadAllGrammars();
            _engine.LoadGrammar(grammar);
            StatusChanged?.Invoke($"{count}개 음성 명령어 로드 완료");
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke($"문법 로드 실패: {ex.Message}");
        }
    }

    public void SetPushToTalkActive(bool isActive)
    {
        _pttActive = isActive;
    }

    public void StartListening()
    {
        if (_engine == null || !IsAvailable || _isListening) return;

        try
        {
            _engine.RecognizeAsync(RecognizeMode.Multiple);
            _isListening = true;
            StatusChanged?.Invoke("음성 청취 시작");
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke($"청취 시작 오류: {ex.Message}");
        }
    }

    public void StopListening()
    {
        if (_engine == null || !IsAvailable || !_isListening) return;

        try
        {
            _engine.RecognizeAsyncStop();
            _isListening = false;
            StatusChanged?.Invoke("음성 청취 중지");
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke($"청취 중지 오류: {ex.Message}");
        }
    }

    private void OnSpeechHypothesized(object? sender, SpeechHypothesizedEventArgs e)
    {
        SpeechDetected?.Invoke(e.Result.Text, e.Result.Confidence);
    }

    private void OnSpeechRecognized(object? sender, SpeechRecognizedEventArgs e)
    {
        if (e.Result == null || e.Result.Confidence < 0.60f) return;

        // PTT 가 켜져 있는데 비활성 상태면 무시
        if (!_pttActive) return;

        var text = e.Result.Text.Trim();
        if (_commandLookup.TryGetValue(text, out var cmd))
        {
            cmd.LastTriggered = DateTimeOffset.Now;
            CommandRecognized?.Invoke(cmd, e.Result.Confidence);
        }
    }

    public void Dispose()
    {
        StopListening();
        _engine?.Dispose();
        _engine = null;
    }
}
