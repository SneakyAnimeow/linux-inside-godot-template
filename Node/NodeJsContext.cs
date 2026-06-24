// ReSharper disable MemberCanBePrivate.Global

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.JavaScript.NodeApi.Runtime;

namespace RealHackerEvolution.Node;

public class NodeJsContext : IDisposable, IAsyncDisposable
{
    private static readonly Architecture[] SupportedArchs = [Architecture.Arm64, Architecture.X64];
    private static readonly Os[] SupportedOs = [Os.Windows, Os.MacOs, Os.Linux];

    private static readonly Dictionary<Os, (string DirectoryPrefix, string Extension)> OsLibNodeBindings = new()
    {
        { Os.Windows, ("win", ".dll") },
        { Os.MacOs, ("osx", ".dylib") },
        { Os.Linux, ("linux", ".so") }
    };

    public NodeEmbeddingPlatform Platform { get; }
    public NodeEmbeddingThreadRuntime Runtime { get; }

    public NodeJsContext(string? workingDirectory=null)
    {
        workingDirectory ??= Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "js");

        Platform = new NodeEmbeddingPlatform(new NodeEmbeddingPlatformSettings()
        {
            LibNodePath = ResolveLibNodePath()
        });

        Runtime = Platform.CreateThreadRuntime(workingDirectory, new NodeEmbeddingRuntimeSettings()
        {
            MainScript = "globalThis.require = require('module').createRequire(process.execPath);\n",
            Args = ["--experimental-modules", "--input-type=module"]
        });
    }

    private string ResolveLibNodePath()
    {
        if (!Environment.Is64BitProcess)
            throw new NotSupportedException("NodeJs integration requires a 64 bit process in order to work");

        var os = ResolveOsPlatform();
        var arch = ResolveArchitecture();

        var (prefix, extension) = OsLibNodeBindings[os];
        var archString = arch.ToString().ToLowerInvariant();

        return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "runtimes", $"{prefix}-{archString}", "native",
            $"libnode{extension}");
    }

    private Os ResolveOsPlatform()
    {
        var os = Enum.GetValues<Os>().FirstOrDefault(os => OperatingSystem.IsOSPlatform(os.ToString()));

        return !SupportedOs.Contains(os)
            ? throw new NotSupportedException(
                $"NodeJs integration works only on following OS platforms: {string.Join(", ", SupportedOs)}")
            : os;
    }

    private Architecture ResolveArchitecture()
    {
        var architecture = RuntimeInformation.ProcessArchitecture;

        return !SupportedArchs.Contains(architecture)
            ? throw new NotSupportedException(
                $"NodeJs integration works only on following architectures: {string.Join(", ", SupportedArchs)}")
            : architecture;
    }

    public static explicit operator NodeEmbeddingThreadRuntime(NodeJsContext nodeJsContext)
    {
        return nodeJsContext.Runtime;
    }

    public static explicit operator NodeEmbeddingPlatform(NodeJsContext nodeJsContext)
    {
        return nodeJsContext.Platform;
    }

    public void Dispose()
    {
        Platform.Dispose();
        Runtime.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        await CastAndDispose(Platform);
        await CastAndDispose(Runtime);

        return;

        static async ValueTask CastAndDispose(IDisposable resource)
        {
            if (resource is IAsyncDisposable resourceAsyncDisposable)
                await resourceAsyncDisposable.DisposeAsync();
            else
                resource.Dispose();
        }
    }
}

public enum Os
{
    Android,
    Browser,
    Wasi,
    FreeBsd,
    Ios,
    Linux,
    MacCatalyst,
    MacOs,
    TvOs,
    WatchOs,
    Windows
}