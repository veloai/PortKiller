using System.IO;
using System.Windows;
using PortKiller.Platform;

namespace PortKiller.App;

/// <summary>
/// Genian NAC 아이디·비밀번호를 등록하는 창.
///
/// <para>비밀번호는 <see cref="System.Windows.Controls.PasswordBox"/> 로만 받고, 저장한 뒤에는
/// 화면에 다시 채우지 않는다("등록됨"만 알린다). 비밀번호 칸을 비워 두고 저장하면 기존 비밀번호가 유지된다.</para>
/// </summary>
public partial class NacSettingsWindow : Window
{
    private readonly NacCredentialStore _store;

    public NacSettingsWindow(NacCredentialStore store)
    {
        InitializeComponent();
        _store = store;
        Loaded += (_, _) => Fill();
    }

    /// <summary>"지금 시도"를 눌렀을 때 실제 시도를 하는 쪽(펫 창)이 구독한다.</summary>
    public event Func<Task<NacLoginResult>>? TryNowRequested;

    private void Fill()
    {
        var cred = _store.Load();
        UserId.Text = cred?.UserId ?? string.Empty;
        Enabled.IsChecked = cred?.Enabled ?? true;
        Autostart.IsChecked = ReadAutostart();
        State.Text = cred is null
            ? "등록되지 않음"
            : "등록됨 (비밀번호를 바꾸려면 새로 적으세요. 비워 두면 그대로 둡니다)";
        PathText.Text = "저장 위치: " + Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PortKillerPet", "nac.dat");
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var id = UserId.Text.Trim();
            var existing = _store.Load();
            var pw = string.IsNullOrEmpty(Password.Password) ? existing?.Password : Password.Password;

            if (id.Length == 0 || string.IsNullOrEmpty(pw))
            {
                SaveState.Text = "아이디와 비밀번호를 모두 적어 주세요.";
                return;
            }

            _store.Save(new NacCredential(id, pw, Enabled.IsChecked == true));
            ApplyAutostart(Autostart.IsChecked == true);
            Password.Clear();
            Fill();
            SaveState.Text = "저장했어요.";
        }
        catch (Exception)
        {
            SaveState.Text = "저장하지 못했어요. 폴더 권한을 확인해 주세요.";
        }
    }

    // 저장 버튼을 누른 때에만 시작 프로그램 등록을 바꾼다. 사용자가 직접 체크한 값을 따른다.
    private static bool ReadAutostart()
    {
        try { return AutostartManager.IsEnabled(); }
        catch (Exception) { return false; }
    }

    private static void ApplyAutostart(bool enable)
    {
        try
        {
            if (enable) AutostartManager.Enable();
            else AutostartManager.Disable();
        }
        catch (Exception)
        {
            // 정책·권한으로 막히면 그대로 둔다. Fill() 이 실제 레지스트리 상태를 다시 보여 준다.
        }
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(
            "저장한 NAC 아이디·비밀번호를 지울까요?",
            "지우기",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);
        if (answer != MessageBoxResult.OK) return;

        _store.Clear();
        Password.Clear();
        Fill();
        SaveState.Text = "지웠어요.";
    }

    private async void TryNowButton_Click(object sender, RoutedEventArgs e)
    {
        var handler = TryNowRequested;
        if (handler is null) return;

        SaveState.Text = "시도하는 중…";
        TryNowButton.IsEnabled = false;
        try
        {
            var result = await handler();
            SaveState.Text = result.Message;
        }
        finally
        {
            TryNowButton.IsEnabled = true;
        }
    }

    private async void ProbeButton_Click(object sender, RoutedEventArgs e)
    {
        SaveState.Text = "확인하는 중…";
        var status = await Task.Run(NacAutoLogin.Probe);
        SaveState.Text = $"Genian 실행 중: {(status.Running ? "true" : "false")}"
                         + $" · 로그인 창: {(status.LoginWindow ? "있음" : "없음")}"
                         + (status.Processes.Count > 0 ? $" · {string.Join(", ", status.Processes)}" : string.Empty);
    }

    private void DumpButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "PortKillerPet", "nac-dump.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, NacAutoLogin.Dump());
            SaveState.Text = "저장했어요: " + path;
        }
        catch (Exception)
        {
            SaveState.Text = "저장하지 못했어요.";
        }
    }
}
