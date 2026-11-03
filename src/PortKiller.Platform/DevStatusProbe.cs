using System.ComponentModel;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Text.Json;
using PortKiller.Core;

namespace PortKiller.Platform;

/// <summary>
/// 개발 환경(Docker·컨테이너·로컬 서버 포트)이 지금 돌고 있는지 확인한다.
///
/// <para><b>무엇을 보나</b>: Docker Desktop 프로세스가 있는지(이름만), <c>docker ps</c> 가 돌려주는 컨테이너 이름·상태,
/// 이 PC 에서 열려 있는 TCP 대기 포트 목록. 포트는 <b>접속하지 않고</b> 윈도우가 알려 주는 목록만 본다.
/// 바깥으로 나가는 통신은 없다.</para>
/// </summary>
public static class DevStatusProbe
{
    private static readonly TimeSpan DockerTimeout = TimeSpan.FromSeconds(8);

    /// <summary>설정 파일. 없으면 기본 목록(<see cref="DevStatusRules.Defaults"/>)을 써서 만들어 둔다.</summary>
    public static string ConfigPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PortKillerPet", "devstatus.json");

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// <summary>확인 목록을 읽는다. 파일이 없거나 깨졌으면 기본 목록이다(없을 때는 기본 목록으로 파일을 만들어 고칠 수 있게 한다).</summary>
    public static IReadOnlyList<DevCheckSpec> LoadSpecs()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var specs = JsonSerializer.Deserialize<List<DevCheckSpec>>(File.ReadAllText(ConfigPath), Json);
                if (specs is { Count: > 0 }) return specs;
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
                File.WriteAllText(ConfigPath, JsonSerializer.Serialize(DevStatusRules.Defaults, Json));
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
        }

        return DevStatusRules.Defaults;
    }

    /// <summary>목록대로 한 번 확인한다. 블로킹(최대 몇 초)이므로 백그라운드에서 부른다.</summary>
    public static IReadOnlyList<DevCheck> Run(IReadOnlyList<DevCheckSpec> specs)
    {
        var needsDocker = specs.Any(s => s.Kind is "docker-engine" or "container");
        var docker = needsDocker ? DockerPs() : null;
        var ports = specs.Any(s => s.Kind == "port") ? ListeningPorts() : new HashSet<int>();

        var result = new List<DevCheck>();
        foreach (var spec in specs)
        {
            result.Add(spec.Kind switch
            {
                "docker-desktop" => ProcessCheck(spec),
                "docker-engine" => docker is null
                    ? new DevCheck(spec.Name, DevState.Unknown, "docker 명령을 찾지 못함")
                    : docker.Value.Ok
                        ? new DevCheck(spec.Name, DevState.Ok, string.Empty)
                        : new DevCheck(spec.Name, DevState.Down, "응답 없음"),
                "container" => ContainerCheck(spec, docker),
                "port" => int.TryParse(spec.Target, out var port)
                    ? new DevCheck(spec.Name, ports.Contains(port) ? DevState.Ok : DevState.Down, $":{port}")
                    : new DevCheck(spec.Name, DevState.Unknown, "포트 번호가 잘못됨"),
                _ => new DevCheck(spec.Name, DevState.Unknown, "모르는 종류"),
            });
        }

        return result;
    }

    private static DevCheck ProcessCheck(DevCheckSpec spec)
    {
        var procs = Process.GetProcessesByName(spec.Target);
        var running = procs.Length > 0;
        foreach (var p in procs) p.Dispose();
        return new DevCheck(spec.Name, running ? DevState.Ok : DevState.Down, string.Empty);
    }

    private static DevCheck ContainerCheck(DevCheckSpec spec, (bool Ok, IReadOnlyDictionary<string, (string State, string Status)> Map)? docker)
    {
        if (docker is not { Ok: true } d) return new DevCheck(spec.Name, DevState.Unknown, "Docker 엔진 응답 없음");
        if (!d.Map.TryGetValue(spec.Target, out var c)) return new DevCheck(spec.Name, DevState.Down, "컨테이너 없음");

        var ok = string.Equals(c.State, "running", StringComparison.OrdinalIgnoreCase);
        return new DevCheck(spec.Name, ok ? DevState.Ok : DevState.Down, c.Status);
    }

    /// <summary>docker ps 를 한 번 돌린다. docker 명령이 아예 없으면 null.</summary>
    private static (bool Ok, IReadOnlyDictionary<string, (string State, string Status)> Map)? DockerPs()
    {
        try
        {
            var psi = new ProcessStartInfo("docker", "ps -a --format \"{{.Names}}|{{.State}}|{{.Status}}\"")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi);
            if (p is null) return null;

            var output = p.StandardOutput.ReadToEndAsync();
            if (!p.WaitForExit(DockerTimeout))
            {
                try { p.Kill(entireProcessTree: true); } catch (Exception) { }
                return (false, new Dictionary<string, (string, string)>());
            }

            return p.ExitCode == 0
                ? (true, DevStatusRules.ParseDockerPs(output.Result))
                : (false, new Dictionary<string, (string, string)>());
        }
        catch (Win32Exception)
        {
            return null; // docker 가 설치돼 있지 않거나 PATH 에 없다
        }
    }

    private static HashSet<int> ListeningPorts()
    {
        try
        {
            return IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Select(e => e.Port).ToHashSet();
        }
        catch (NetworkInformationException)
        {
            return new HashSet<int>();
        }
    }
}
