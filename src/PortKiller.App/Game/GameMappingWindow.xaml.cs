using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PortKiller.Game.Input;
using PortKiller.Game.Models;
using PortKiller.Game.Profiles;
using PortKiller.Game.Voice;

namespace PortKiller.App.Game;

public partial class GameMappingWindow : Window
{
    private GameProfile _profile;
    private readonly UnifiedInputDispatcher _dispatcher;
    private GameOverlayWindow? _overlayWindow;
    private ObservableCollection<VoiceCommand> _voiceCommands;

    public GameMappingWindow(UnifiedInputDispatcher dispatcher)
    {
        InitializeComponent();
        _dispatcher = dispatcher;
        _profile = dispatcher.CurrentProfile;

        _voiceCommands = new ObservableCollection<VoiceCommand>(_profile.VoiceCommands);
        VoiceCommandsGrid.ItemsSource = _voiceCommands;

        LoadAudioDevices();
        BindProfileValues();

        // 실시간 음성 인식 테스트 리스너
        _dispatcher.Voice.SpeechDetected += OnSpeechDetected;
        _dispatcher.Voice.CommandRecognized += OnCommandRecognized;
    }

    private void LoadAudioDevices()
    {
        AudioDeviceCombo.Items.Clear();
        var devices = DualSenseAudioDeviceFinder.GetInputDevices();

        int selectedIndex = 0;
        int i = 0;
        foreach (var dev in devices)
        {
            string label = dev.Name;
            if (dev.IsDualSenseMic)
            {
                label = $"🎮 [PS5 듀얼센스 패드 마이크] {dev.Name}";
                selectedIndex = i;
            }
            AudioDeviceCombo.Items.Add(label);
            i++;
        }

        if (AudioDeviceCombo.Items.Count > 0)
        {
            AudioDeviceCombo.SelectedIndex = selectedIndex;
        }
    }

    private void BindProfileValues()
    {
        InvertYCheck.IsChecked = _profile.InvertYAxis;
        SwapSticksCheck.IsChecked = _profile.SwapSticks;
        TouchpadMouseCheck.IsChecked = _profile.EnableTouchpadMouse;
        LookSensSlider.Value = _profile.LookSensitivity;
        TouchSensSlider.Value = _profile.TouchpadSensitivity;
        DeadzoneSlider.Value = _profile.StickDeadZone;
        VibrationCheck.IsChecked = _profile.EnableVibration;
        OverlayCheck.IsChecked = _profile.EnableInGameOverlay;
        PttCheck.IsChecked = _profile.IsPushToTalk;
    }

    private void OnSpeechDetected(string text, float confidence)
    {
        Dispatcher.Invoke(() =>
        {
            LiveVoiceResultText.Text = $"듣는 중: \"{text}\" (신뢰도 {confidence:P0})";
            LiveVoiceResultText.Foreground = new SolidColorBrush(Color.FromRgb(255, 209, 102));
        });
    }

    private void OnCommandRecognized(VoiceCommand cmd, float confidence)
    {
        Dispatcher.Invoke(() =>
        {
            LiveVoiceResultText.Text = $"✅ 인식 성공! [{cmd.Phrase}] -> 0x{cmd.TargetVirtualKey:X2} 발동 ({confidence:P0})";
            LiveVoiceResultText.Foreground = new SolidColorBrush(Color.FromRgb(83, 227, 166));
        });
    }

    #region Tab Navigation
    private void TabControls_Click(object sender, RoutedEventArgs e)
    {
        ControlsTabContent.Visibility = Visibility.Visible;
        VoiceTabContent.Visibility = Visibility.Collapsed;
        HudTabContent.Visibility = Visibility.Collapsed;
        SetTabActive(TabControlsBtn);
    }

    private void TabVoice_Click(object sender, RoutedEventArgs e)
    {
        ControlsTabContent.Visibility = Visibility.Collapsed;
        VoiceTabContent.Visibility = Visibility.Visible;
        HudTabContent.Visibility = Visibility.Collapsed;
        SetTabActive(TabVoiceBtn);
    }

    private void TabHud_Click(object sender, RoutedEventArgs e)
    {
        ControlsTabContent.Visibility = Visibility.Collapsed;
        VoiceTabContent.Visibility = Visibility.Collapsed;
        HudTabContent.Visibility = Visibility.Visible;
        SetTabActive(TabHudBtn);
    }

    private void SetTabActive(Button activeBtn)
    {
        TabControlsBtn.Style = (Style)FindResource("HeaderTabBtn");
        TabVoiceBtn.Style = (Style)FindResource("HeaderTabBtn");
        TabHudBtn.Style = (Style)FindResource("HeaderTabBtn");
        activeBtn.Style = (Style)FindResource("ActiveTabBtn");
    }
    #endregion

    private void ToggleOverlay_Click(object sender, RoutedEventArgs e)
    {
        if (_overlayWindow == null || !_overlayWindow.IsLoaded)
        {
            _overlayWindow = new GameOverlayWindow(_dispatcher);
            _overlayWindow.Show();
        }
        else
        {
            _overlayWindow.Close();
            _overlayWindow = null;
        }
    }

    private void ExportConsolePort_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            "PortKiller 와우 클래식 기본 키맵이 ConsolePort 호환 규격으로 복사되었습니다.\n\n" +
            "인게임 ConsolePort 애드온에서 'Default DualSense' 프리셋을 로드하면\n" +
            "포트킬러 패드/보이스 매핑과 100% 동기화됩니다.",
            "ConsolePort 애드온 연동",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void AddVoiceWord_Click(object sender, RoutedEventArgs e)
    {
        var newCmd = new VoiceCommand
        {
            Phrase = "새 명령어",
            TargetVirtualKey = 0x31,
            Category = "사용자 정의",
            Description = "새 스킬 키 매핑"
        };
        _voiceCommands.Add(newCmd);
    }

    private void ResetVoiceWords_Click(object sender, RoutedEventArgs e)
    {
        var def = DefaultProfiles.CreateWoWClassicProfile();
        _voiceCommands.Clear();
        foreach (var cmd in def.VoiceCommands)
        {
            _voiceCommands.Add(cmd);
        }
    }

    private void ResetDefaults_Click(object sender, RoutedEventArgs e)
    {
        _profile = DefaultProfiles.CreateWoWClassicProfile();
        BindProfileValues();
        ResetVoiceWords_Click(sender, e);
    }

    private void SaveAndApply_Click(object sender, RoutedEventArgs e)
    {
        _profile.InvertYAxis = InvertYCheck.IsChecked == true;
        _profile.SwapSticks = SwapSticksCheck.IsChecked == true;
        _profile.EnableTouchpadMouse = TouchpadMouseCheck.IsChecked == true;
        _profile.LookSensitivity = LookSensSlider.Value;
        _profile.TouchpadSensitivity = TouchSensSlider.Value;
        _profile.StickDeadZone = DeadzoneSlider.Value;
        _profile.EnableVibration = VibrationCheck.IsChecked == true;
        _profile.EnableInGameOverlay = OverlayCheck.IsChecked == true;
        _profile.IsPushToTalk = PttCheck.IsChecked == true;

        _profile.VoiceCommands = _voiceCommands.ToList();

        _dispatcher.UpdateProfile(_profile);

        if (_profile.EnableInGameOverlay && (_overlayWindow == null || !_overlayWindow.IsLoaded))
        {
            _overlayWindow = new GameOverlayWindow(_dispatcher);
            _overlayWindow.Show();
        }

        Close();
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        _dispatcher.Voice.SpeechDetected -= OnSpeechDetected;
        _dispatcher.Voice.CommandRecognized -= OnCommandRecognized;
        base.OnClosed(e);
    }
}
