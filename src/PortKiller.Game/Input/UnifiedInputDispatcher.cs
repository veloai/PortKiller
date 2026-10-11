using PortKiller.Game.Controller;
using PortKiller.Game.Models;
using PortKiller.Game.Voice;
using PortKiller.Platform;

namespace PortKiller.Game.Input;

/// <summary>
/// PS5 패드 입력, 터치패드 마우스, 음성 명령을 수집하여
/// 활성화된 게임(와우 클래식)으로 가상 입력을 전달하는 중앙 조율기(Dispatcher).
/// </summary>
public sealed class UnifiedInputDispatcher : IDisposable
{
    private readonly DualSenseDevice _controller;
    private readonly SpeechRecognitionService _voice;
    private readonly TouchpadMouseTranslator _touchpadTranslator = new();

    private GameProfile _profile;
    private readonly HashSet<DualSenseButton> _lastPressedButtons = new();
    private readonly Dictionary<DualSenseButton, ushort> _currentlyHeldKeys = new();

    public event Action<VoiceCommand, float>? VoiceCommandExecuted;
    public event Action<DualSenseState>? StateUpdated;
    public event Action<string>? LogMessage;

    public DualSenseDevice Controller => _controller;
    public SpeechRecognitionService Voice => _voice;
    public GameProfile CurrentProfile => _profile;

    public UnifiedInputDispatcher(GameProfile profile)
    {
        _profile = profile;
        _controller = new DualSenseDevice();
        _voice = new SpeechRecognitionService();

        _controller.StateChanged += OnControllerStateChanged;
        _voice.CommandRecognized += OnVoiceCommandRecognized;

        _voice.LoadCommands(_profile.VoiceCommands);
        _voice.StartListening();
    }

    public void UpdateProfile(GameProfile newProfile)
    {
        _profile = newProfile;
        _voice.LoadCommands(_profile.VoiceCommands);
        LogMessage?.Invoke($"프로필 갱신됨: {newProfile.Name}");
    }

    private void OnControllerStateChanged(DualSenseState state)
    {
        StateUpdated?.Invoke(state);

        // 타겟 게임 프로세스가 활성화되어 있는지 확인
        if (!IsTargetProcessActive())
        {
            ReleaseAllHeldKeys();
            return;
        }

        // 1. PTT (Push-to-Talk) 상태 갱신
        if (_profile.IsPushToTalk)
        {
            bool isPttHeld = state.IsPressed(_profile.PushToTalkButton);
            _voice.SetPushToTalkActive(isPttHeld);
        }

        // 2. 터치패드 마우스 커서 이동
        if (_profile.EnableTouchpadMouse && state.Touch1.IsActive)
        {
            var (dx, dy) = _touchpadTranslator.ProcessTouch(state.Touch1, _profile.TouchpadSensitivity);
            if (dx != 0 || dy != 0)
            {
                InputSimulator.MoveMouseRelative(dx, dy);
            }
        }
        else
        {
            _touchpadTranslator.Reset();
        }

        // 3. 버튼 상태 비교 및 키보드/마우스 입력 분배
        HandleButtonInputs(state);
    }

    private void HandleButtonInputs(DualSenseState state)
    {
        bool isL2Held = state.IsPressed(DualSenseButton.L2);
        bool isR2Held = state.IsPressed(DualSenseButton.R2);

        // 새로 눌린 버튼 처리
        foreach (var btn in state.PressedButtons)
        {
            if (!_lastPressedButtons.Contains(btn))
            {
                OnButtonPressed(btn, isL2Held, isR2Held);
            }
        }

        // 뗀 버튼 처리
        foreach (var btn in _lastPressedButtons)
        {
            if (!state.PressedButtons.Contains(btn))
            {
                OnButtonReleased(btn);
            }
        }

        _lastPressedButtons.Clear();
        foreach (var btn in state.PressedButtons)
        {
            _lastPressedButtons.Add(btn);
        }
    }

    private void OnButtonPressed(DualSenseButton button, bool isL2, bool isR2)
    {
        if (!_profile.Bindings.TryGetValue(button, out var binding)) return;

        // 마우스 클릭 처리
        if (binding.MouseClickType == 1)
        {
            InputSimulator.MouseLeftClick(true);
            return;
        }
        if (binding.MouseClickType == 2)
        {
            InputSimulator.MouseRightClick(true);
            return;
        }

        // 조합키 우선순위
        ushort keyToSend = binding.NormalKey;
        if (isL2 && binding.L2ModifierKey != 0)
        {
            keyToSend = binding.L2ModifierKey;
        }
        else if (isR2 && binding.R2ModifierKey != 0)
        {
            keyToSend = binding.R2ModifierKey;
        }

        if (keyToSend != 0)
        {
            InputSimulator.SendKey(keyToSend, true);
            _currentlyHeldKeys[button] = keyToSend;
        }
    }

    private void OnButtonReleased(DualSenseButton button)
    {
        if (!_profile.Bindings.TryGetValue(button, out var binding)) return;

        if (binding.MouseClickType == 1)
        {
            InputSimulator.MouseLeftClick(false);
            return;
        }
        if (binding.MouseClickType == 2)
        {
            InputSimulator.MouseRightClick(false);
            return;
        }

        if (_currentlyHeldKeys.TryGetValue(button, out var sentKey))
        {
            InputSimulator.SendKey(sentKey, false);
            _currentlyHeldKeys.Remove(button);
        }
    }

    private void OnVoiceCommandRecognized(VoiceCommand cmd, float confidence)
    {
        if (!IsTargetProcessActive()) return;

        // 단발성 키 탭 (블리자드 정책 준수: 1 발화 = 1 회 입력)
        InputSimulator.TapKey(cmd.TargetVirtualKey, cmd.ModifierVirtualKey);

        VoiceCommandExecuted?.Invoke(cmd, confidence);
        LogMessage?.Invoke($"보이스 실행: [{cmd.Phrase}] -> 0x{cmd.TargetVirtualKey:X2} (신뢰도: {confidence:P0})");
    }

    private bool IsTargetProcessActive()
    {
        var proc = InputSimulator.GetForegroundProcessName();
        if (string.IsNullOrEmpty(proc)) return false;

        // 와우 클래식 프로세스 또는 와우 일반 프로세스
        return proc.Equals(_profile.TargetProcessName, StringComparison.OrdinalIgnoreCase) ||
               proc.Equals("Wow", StringComparison.OrdinalIgnoreCase) ||
               proc.Equals("WowClassic", StringComparison.OrdinalIgnoreCase) ||
               proc.Equals("PortKillerPet", StringComparison.OrdinalIgnoreCase); // 테스트 편의를 위해 자사 앱 포함
    }

    private void ReleaseAllHeldKeys()
    {
        foreach (var key in _currentlyHeldKeys.Values)
        {
            InputSimulator.SendKey(key, false);
        }
        _currentlyHeldKeys.Clear();
        _lastPressedButtons.Clear();
    }

    public void Dispose()
    {
        ReleaseAllHeldKeys();
        _controller.Dispose();
        _voice.Dispose();
    }
}
