using System.Runtime.InteropServices;

namespace HoloRepository.Core.Daemon;

/// <summary>
/// Lightweight detection of acceleration hardware by looking for vendor runtime libraries.
/// The prototype only reports what it finds; rendering always falls back to the CPU path.
/// </summary>
public sealed class HardwareProbe
{
    private readonly Func<string, bool> _fileExists;
    private readonly Architecture _architecture;
    private readonly string _systemDirectory;

    public HardwareProbe()
        : this(File.Exists, RuntimeInformation.OSArchitecture, Environment.SystemDirectory)
    {
    }

    public HardwareProbe(Func<string, bool> fileExists, Architecture architecture, string systemDirectory)
    {
        _fileExists = fileExists;
        _architecture = architecture;
        _systemDirectory = systemDirectory;
    }

    public IReadOnlyList<BackendCapability> Probe(ComputeBackend active)
    {
        bool cuda = _fileExists(Path.Combine(_systemDirectory, "nvcuda.dll"));
        bool levelZero = _fileExists(Path.Combine(_systemDirectory, "ze_loader.dll"))
                         || _fileExists(Path.Combine(_systemDirectory, "igdrcl64.dll"));
        bool arm = _architecture == Architecture.Arm64;

        return
        [
            new BackendCapability(ComputeBackend.Cpu, true, active == ComputeBackend.Cpu,
                $"{Environment.ProcessorCount} logical cores · reference ray caster"),
            new BackendCapability(ComputeBackend.NvidiaCuda, cuda, false,
                cuda ? "NVIDIA driver found · CUDA kernels not yet implemented" : "No NVIDIA CUDA driver found"),
            new BackendCapability(ComputeBackend.IntelArc, levelZero, false,
                levelZero ? "Intel GPU runtime found · OpenVINO/oneAPI path not yet implemented" : "No Intel GPU runtime found"),
            new BackendCapability(ComputeBackend.RtxSparkArm, arm, false,
                arm ? "ARM64 host · RTX Spark build target" : $"Host is {_architecture}, not ARM64"),
        ];
    }
}
