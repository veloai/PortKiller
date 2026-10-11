using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using PortKiller.Game.Models;

namespace PortKiller.Game.Controller;

/// <summary>
/// 윈도우 HID 서브시스템을 통해 PS5 듀얼센스 하드웨어와 직접 통신하는 디바이스 관리자.
/// </summary>
public sealed class DualSenseDevice : IDisposable
{
    private readonly Thread _readThread;
    private readonly CancellationTokenSource _cts = new();
    private SafeFileHandle? _deviceHandle;
    private FileStream? _deviceStream;

    public bool IsConnected { get; private set; }
    public DualSenseState CurrentState { get; private set; } = new();

    public event Action<DualSenseState>? StateChanged;

    public DualSenseDevice()
    {
        _readThread = new Thread(ReadLoop)
        {
            IsBackground = true,
            Name = "DualSense-HidReadThread"
        };
        _readThread.Start();
    }

    private void ReadLoop()
    {
        var buffer = new byte[128];

        while (!_cts.IsCancellationRequested)
        {
            if (_deviceStream == null)
            {
                // 디바이스 연결 시도
                if (!TryOpenDevice())
                {
                    IsConnected = false;
                    Thread.Sleep(1500); // 디바이스 없을 땐 1.5초 대기
                    continue;
                }
                IsConnected = true;
            }

            try
            {
                int bytesRead = _deviceStream.Read(buffer, 0, buffer.Length);
                if (bytesRead > 0)
                {
                    var newState = DualSenseHidParser.Parse(buffer.AsSpan(0, bytesRead));
                    CurrentState = newState;
                    StateChanged?.Invoke(newState);
                }
            }
            catch (Exception)
            {
                // 연결 해제됨
                CloseDevice();
                IsConnected = false;
                Thread.Sleep(1000);
            }
        }

        CloseDevice();
    }

    private bool TryOpenDevice()
    {
        try
        {
            var hidGuid = GetHidGuid();
            var devInfo = SetupDiGetClassDevs(ref hidGuid, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
            if (devInfo == IntPtr.Zero || devInfo == new IntPtr(-1)) return false;

            try
            {
                var ifData = new SP_DEVICE_INTERFACE_DATA();
                ifData.cbSize = Marshal.SizeOf(ifData);

                for (uint index = 0; SetupDiEnumDeviceInterfaces(devInfo, IntPtr.Zero, ref hidGuid, index, ref ifData); index++)
                {
                    var path = GetDevicePath(devInfo, ref ifData);
                    if (string.IsNullOrEmpty(path)) continue;

                    // Sony Vendor ID (054C) 및 Product ID (0CE6 or 0DF2) 확인
                    var lower = path.ToLowerInvariant();
                    if (lower.Contains("vid_054c") && (lower.Contains("pid_0ce6") || lower.Contains("pid_0df2")))
                    {
                        var handle = CreateFile(
                            path,
                            GENERIC_READ | GENERIC_WRITE,
                            FILE_SHARE_READ | FILE_SHARE_WRITE,
                            IntPtr.Zero,
                            OPEN_EXISTING,
                            0,
                            IntPtr.Zero);

                        if (!handle.IsInvalid)
                        {
                            _deviceHandle = handle;
                            _deviceStream = new FileStream(_deviceHandle, FileAccess.ReadWrite, 128, false);
                            return true;
                        }
                    }
                }
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(devInfo);
            }
        }
        catch
        {
            // HID 접근 권한 또는 장치 열기 오류 시 안전하게 false 반환
        }

        return false;
    }

    private static string? GetDevicePath(IntPtr devInfo, ref SP_DEVICE_INTERFACE_DATA ifData)
    {
        uint requiredSize = 0;
        SetupDiGetDeviceInterfaceDetail(devInfo, ref ifData, IntPtr.Zero, 0, ref requiredSize, IntPtr.Zero);
        if (requiredSize == 0) return null;

        var detail = Marshal.AllocHGlobal((int)requiredSize);
        try
        {
            Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
            if (SetupDiGetDeviceInterfaceDetail(devInfo, ref ifData, detail, requiredSize, ref requiredSize, IntPtr.Zero))
            {
                var pathPtr = IntPtr.Add(detail, 4);
                return Marshal.PtrToStringAuto(pathPtr);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(detail);
        }

        return null;
    }

    private void CloseDevice()
    {
        _deviceStream?.Dispose();
        _deviceStream = null;
        _deviceHandle?.Dispose();
        _deviceHandle = null;
    }

    public void Dispose()
    {
        _cts.Cancel();
        CloseDevice();
    }

    #region Win32 P/Invoke
    private const uint GENERIC_READ = 0x80000000;
    private const uint GENERIC_WRITE = 0x40000000;
    private const uint FILE_SHARE_READ = 0x00000001;
    private const uint FILE_SHARE_WRITE = 0x00000002;
    private const uint OPEN_EXISTING = 3;
    private const uint DIGCF_PRESENT = 0x00000002;
    private const uint DIGCF_DEVICEINTERFACE = 0x00000010;

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVICE_INTERFACE_DATA
    {
        public int cbSize;
        public Guid interfaceClassGuid;
        public int flags;
        public IntPtr reserved;
    }

    [DllImport("hid.dll", SetLastError = true)]
    private static extern void HidD_GetHidGuid(out Guid hidGuid);

    private static Guid GetHidGuid()
    {
        HidD_GetHidGuid(out var guid);
        return guid;
    }

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, IntPtr enumerator, IntPtr hwndParent, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInterfaces(IntPtr deviceInfoSet, IntPtr deviceInfoData, ref Guid interfaceClassGuid, uint memberIndex, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr deviceInfoSet, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData, IntPtr deviceInterfaceDetailData, uint deviceInterfaceDetailDataSize, ref uint requiredSize, IntPtr deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern SafeFileHandle CreateFile(string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);
    #endregion
}
