using System.Runtime.InteropServices;

namespace PortKiller.Game.Voice;

/// <summary>
/// 윈도우 오디오 입력 장치(마이크)를 검색하고,
/// PS5 듀얼센스 패드의 내장 마이크("Wireless Controller")를 자동 식별합니다.
/// </summary>
public static class DualSenseAudioDeviceFinder
{
    public readonly record struct AudioDeviceInfo(int DeviceId, string Name, bool IsDualSenseMic);

    /// <summary>
    /// 현재 윈도우에 연결된 모든 마이크 입력 장치 목록을 반환합니다.
    /// </summary>
    public static List<AudioDeviceInfo> GetInputDevices()
    {
        var list = new List<AudioDeviceInfo>();
        int numDevs = waveInGetNumDevs();

        for (int i = 0; i < numDevs; i++)
        {
            var caps = new WAVEINCAPS();
            if (waveInGetDevCaps((IntPtr)i, ref caps, (uint)Marshal.SizeOf(caps)) == 0)
            {
                string name = caps.szPname;
                bool isDualSense = name.Contains("Wireless Controller", StringComparison.OrdinalIgnoreCase) ||
                                   name.Contains("DualSense", StringComparison.OrdinalIgnoreCase);

                list.Add(new AudioDeviceInfo(i, name, isDualSense));
            }
        }

        return list;
    }

    /// <summary>
    /// 듀얼센스 마이크 장치가 있으면 해당 장치명을, 없으면 기본 마이크 장치명을 반환합니다.
    /// </summary>
    public static string GetPreferredDeviceName()
    {
        var devices = GetInputDevices();
        var ds = devices.FirstOrDefault(d => d.IsDualSenseMic);
        if (!string.IsNullOrEmpty(ds.Name))
        {
            return ds.Name;
        }

        return devices.FirstOrDefault().Name ?? "기본 마이크";
    }

    #region WinMM P/Invoke
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct WAVEINCAPS
    {
        public ushort wMid;
        public ushort wPid;
        public uint vDriverVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szPname;
        public uint dwFormats;
        public ushort wChannels;
        public ushort wReserved1;
    }

    [DllImport("winmm.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern int waveInGetNumDevs();

    [DllImport("winmm.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern int waveInGetDevCaps(IntPtr uDeviceID, ref WAVEINCAPS pwic, uint cbwic);
    #endregion
}
