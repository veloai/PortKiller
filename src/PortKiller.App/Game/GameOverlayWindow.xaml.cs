using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using PortKiller.Game.Input;
using PortKiller.Game.Models;

namespace PortKiller.App.Game;

public partial class GameOverlayWindow : Window
{
    private readonly UnifiedInputDispatcher _dispatcher;
    private readonly DispatcherTimer _toastTimer;

    public GameOverlayWindow(UnifiedInputDispatcher dispatcher)
    {
        InitializeComponent();
        _dispatcher = dispatcher;

        // 화면 상단 중앙 기본 배치
        Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
        Top = 20;

        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.0) };
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            VoiceToastText.Text = "🎙️ 보이스 대기 중...";
            VoiceToastText.Foreground = new SolidColorBrush(Color.FromRgb(122, 141, 159));
            VoiceToastBorder.Background = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255));
        };

        _dispatcher.VoiceCommandExecuted += OnVoiceCommandExecuted;
        _dispatcher.StateUpdated += OnStateUpdated;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.MarkAsToolWindow(hwnd);
    }

    private void OnVoiceCommandExecuted(VoiceCommand cmd, float confidence)
    {
        Dispatcher.Invoke(() =>
        {
            VoiceToastText.Text = $"⚡ [{cmd.Phrase}] -> 0x{cmd.TargetVirtualKey:X2} 발동! ({confidence:P0})";
            VoiceToastText.Foreground = new SolidColorBrush(Color.FromRgb(0, 229, 255));
            VoiceToastBorder.Background = new SolidColorBrush(Color.FromArgb(80, 0, 229, 255));

            _toastTimer.Stop();
            _toastTimer.Start();
        });
    }

    private void OnStateUpdated(DualSenseState state)
    {
        Dispatcher.Invoke(() =>
        {
            if (state.IsConnected)
            {
                PadStatusText.Text = "PS5 DualSense";
                PadStatusText.Foreground = new SolidColorBrush(Color.FromRgb(204, 214, 224));
                BatteryText.Text = $" ({state.BatteryPercentage}%)";

                if (state.IsMicMuted)
                {
                    MicIcon.Text = "🔇";
                    MicStatusText.Text = "패드 마이크 음소거";
                    MicStatusText.Foreground = new SolidColorBrush(Color.FromRgb(255, 152, 0));
                }
                else
                {
                    MicIcon.Text = "🎙️";
                    MicStatusText.Text = "패드 마이크 작동 중";
                    MicStatusText.Foreground = new SolidColorBrush(Color.FromRgb(83, 227, 166));
                }
            }
            else
            {
                PadStatusText.Text = "패드 미연결";
                PadStatusText.Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 150));
                BatteryText.Text = "";
            }
        });
    }

    private void HudCapsule_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void CloseHud_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        _dispatcher.VoiceCommandExecuted -= OnVoiceCommandExecuted;
        _dispatcher.StateUpdated -= OnStateUpdated;
        base.OnClosed(e);
    }
}
