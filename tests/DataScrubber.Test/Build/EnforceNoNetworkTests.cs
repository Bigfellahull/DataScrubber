namespace DataScrubber.Test.Build;

using System.Diagnostics;
using FluentAssertions;
using Xunit;

/// <summary>
///     Coverage for M5 AC7: the EnforceNoNetwork MSBuild target fails the
///     build whenever <c>DataScrubber.Cli</c> or <c>DataScrubber.Core</c>
///     references one of the forbidden network assemblies. The target is
///     exercised by spinning up a synthetic project that masquerades as the
///     name and pre-stages a fake offending <c>ReferencePath</c> item.
/// </summary>
public class EnforceNoNetworkTests : IDisposable
{
    private static readonly string _repoRoot = LocateRepoRoot();
    private static readonly string _targetsPath = Path.Combine(_repoRoot, "build", "EnforceNoNetwork.targets");

    private readonly string _workingDir = Path.Combine(Path.GetTempPath(), "scrub-m5-nonet-" + Guid.NewGuid());

    public EnforceNoNetworkTests()
    {
        Directory.CreateDirectory(_workingDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_workingDir))
            {
                Directory.Delete(_workingDir, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void TargetsFileImportsCleanlyAndPassesForCleanProject()
    {
        // A synthetic msbuild project with name DataScrubber.Cli that has no
        // forbidden references must succeed when the target runs.
        string projectPath = Path.Combine(_workingDir, "DataScrubber.Cli.proj");
        File.WriteAllText(projectPath, $"""
            <Project>
              <Import Project="{_targetsPath}" />
              <Target Name="Run" DependsOnTargets="EnforceNoNetwork" />
            </Project>
            """);

        (int code, _, string stderr) = RunMsbuild(projectPath, "Run");

        code.Should().Be(0, stderr);
    }

    [Fact]
    public void Ac7_TargetFailsWhenForbiddenReferenceIsPresent()
    {
        string projectPath = Path.Combine(_workingDir, "DataScrubber.Cli.proj");
        // Inject a fake ReferencePath whose FileName is System.Net.Http and
        // whose NuGetSourceType marks it as coming from a NuGet package.
        File.WriteAllText(projectPath, $"""
            <Project>
              <ItemGroup>
                <ReferencePath Include="C:\fake\System.Net.Http.dll">
                  <NuGetSourceType>Package</NuGetSourceType>
                  <NuGetPackageId>SomeForbiddenPackage</NuGetPackageId>
                </ReferencePath>
              </ItemGroup>
              <Import Project="{_targetsPath}" />
              <Target Name="Run" DependsOnTargets="EnforceNoNetwork" />
            </Project>
            """);

        (int code, string stdout, _) = RunMsbuild(projectPath, "Run");

        code.Should().NotBe(0);
        string combined = stdout;
        combined.Should().Contain("EnforceNoNetwork");
        combined.Should().Contain("System.Net.Http");
        combined.Should().Contain("SomeForbiddenPackage");
    }

    [Fact]
    public void Ac7_TargetFailsForRuntimeCopyLocalForbiddenItem()
    {
        string projectPath = Path.Combine(_workingDir, "DataScrubber.Core.proj");
        File.WriteAllText(projectPath, $"""
            <Project>
              <ItemGroup>
                <ReferenceCopyLocalPaths Include="C:\fake\System.Net.Sockets.dll">
                  <NuGetPackageId>SomeForbiddenPackage</NuGetPackageId>
                </ReferenceCopyLocalPaths>
              </ItemGroup>
              <Import Project="{_targetsPath}" />
              <Target Name="Run" DependsOnTargets="EnforceNoNetwork" />
            </Project>
            """);

        (int code, string stdout, _) = RunMsbuild(projectPath, "Run");

        code.Should().NotBe(0);
        stdout.Should().Contain("System.Net.Sockets");
    }

    [Fact]
    public void TargetIsExemptForTestProjectName()
    {
        // The DataScrubber.Test project must NOT trip the no-network gate
        // even with a forbidden reference present, since test fixtures may
        // legitimately use HTTP types.
        string projectPath = Path.Combine(_workingDir, "DataScrubber.Test.proj");
        File.WriteAllText(projectPath, $"""
            <Project>
              <ItemGroup>
                <ReferencePath Include="C:\fake\System.Net.Http.dll">
                  <NuGetSourceType>Package</NuGetSourceType>
                  <NuGetPackageId>SomeForbiddenPackage</NuGetPackageId>
                </ReferencePath>
              </ItemGroup>
              <Import Project="{_targetsPath}" />
              <Target Name="Run" DependsOnTargets="EnforceNoNetwork" />
            </Project>
            """);

        (int code, _, string stderr) = RunMsbuild(projectPath, "Run");

        code.Should().Be(0, stderr);
    }

    [Fact]
    public void Ac7_PackageReferenceWhoseAssemblyUsesHttpClientFailsBuildWithDsp0004()
    {
        // The package-IL scan must catch a NuGet package whose own DLL has
        // an AssemblyRef to a forbidden BCL networking assembly, even when
        // our own code does not call the forbidden type. This is the literal
        // AC7 scenario: developer adds a PackageReference to a package that
        // brings in System.Net.Http via its own implementation.
        //
        // Setup:
        //   1. Build a real stub library that uses HttpClient. Its compiled
        //      DLL will have an AssemblyRef to System.Net.Http.
        //   2. Build a synthetic DataScrubber.Cli project that references
        //      the stub via a staged ReferencePath marked as
        //      NuGetSourceType=Package.
        //   3. Run our EnforceNoNetwork target. Assert DSP0004 fires.

        string stubDir = Path.Combine(_workingDir, "NetworkUserStub");
        Directory.CreateDirectory(stubDir);
        File.WriteAllText(Path.Combine(stubDir, "NetworkUserStub.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
                <GenerateDocumentationFile>false</GenerateDocumentationFile>
              </PropertyGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(stubDir, "Stub.cs"), """
            using System.Net.Http;

            namespace NetworkUserStub;

            public class Stub
            {
                public HttpClient MakeClient() => new HttpClient();
            }
            """);

        (int stubCode, string stubStdout, _) = RunDotnet($"build \"{stubDir}\" -c Release /nologo");
        stubCode.Should().Be(0, stubStdout);

        string stubDll = Path.Combine(stubDir, "bin", "Release", "net10.0", "NetworkUserStub.dll");
        File.Exists(stubDll).Should().BeTrue("stub library must build before staging");

        string cliDir = Path.Combine(_workingDir, "DataScrubberCliProbe");
        Directory.CreateDirectory(cliDir);
        string projectPath = Path.Combine(cliDir, "DataScrubber.Cli.proj");
        File.WriteAllText(projectPath, $"""
            <Project>
              <ItemGroup>
                <ReferencePath Include="{stubDll}">
                  <NuGetSourceType>Package</NuGetSourceType>
                  <NuGetPackageId>NetworkUserStub</NuGetPackageId>
                </ReferencePath>
              </ItemGroup>
              <Import Project="{_targetsPath}" />
              <Target Name="Scan" DependsOnTargets="EnforceNoNetwork" />
            </Project>
            """);

        (int code, string stdout, _) = RunMsbuild(projectPath, "Scan");

        code.Should().NotBe(0, stdout);
        stdout.Should().Contain("DSP0004");
        stdout.Should().Contain("NetworkUserStub.dll");
        stdout.Should().Contain("System.Net.Http");
    }

    [Fact]
    public void Ac7_RealProjectWithDirectHttpClientUsageFailsBuildWithDsp0003()
    {
        // The IL-level scan must catch a developer who pulls in a forbidden
        // BCL type from the framework — the case that no package-graph check
        // can ever catch because System.Net.Http lives in Microsoft.NETCore.App,
        // not a NuGet package. We synthesise a real net10.0 SDK project named
        // DataScrubber.Cli (so the gate applies), have it use HttpClient, run
        // dotnet build, and assert it fails with the DSP0003 IL-scan error.
        string projDir = Path.Combine(_workingDir, "DataScrubber.Cli");
        Directory.CreateDirectory(projDir);

        string projectPath = Path.Combine(projDir, "DataScrubber.Cli.csproj");
        File.WriteAllText(projectPath, $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
                <GenerateDocumentationFile>false</GenerateDocumentationFile>
              </PropertyGroup>
              <Import Project="{_targetsPath}" />
            </Project>
            """);

        File.WriteAllText(Path.Combine(projDir, "Program.cs"), """
            using System.Net.Http;

            internal class Program
            {
                public static int Main()
                {
                    var client = new HttpClient();
                    return client.BaseAddress is null ? 0 : 1;
                }
            }
            """);

        (int code, string stdout, _) = RunDotnet($"build \"{projectPath}\" -c Release /nologo");

        code.Should().NotBe(0, stdout);
        stdout.Should().Contain("DSP0003");
        stdout.Should().Contain("System.Net.Http");
    }

    [Fact]
    public void TargetAllowsOnnxRuntimeProvenanceEvenForForbiddenFileName()
    {
        // The OnnxRuntime allow-list is the documented exemption: native
        // dependencies named like a forbidden BCL assembly are tolerated when
        // they come from the OnnxRuntime package.
        string projectPath = Path.Combine(_workingDir, "DataScrubber.Cli.proj");
        File.WriteAllText(projectPath, $"""
            <Project>
              <ItemGroup>
                <ReferencePath Include="C:\fake\System.Net.Http.dll">
                  <NuGetSourceType>Package</NuGetSourceType>
                  <NuGetPackageId>Microsoft.ML.OnnxRuntime</NuGetPackageId>
                </ReferencePath>
              </ItemGroup>
              <Import Project="{_targetsPath}" />
              <Target Name="Run" DependsOnTargets="EnforceNoNetwork" />
            </Project>
            """);

        (int code, _, string stderr) = RunMsbuild(projectPath, "Run");

        code.Should().Be(0, stderr);
    }

    private static (int Code, string Stdout, string Stderr) RunMsbuild(string projectPath, string target)
        => RunDotnet($"msbuild \"{projectPath}\" /t:{target} /nologo");

    private static (int Code, string Stdout, string Stderr) RunDotnet(string arguments)
    {
        ProcessStartInfo info = new("dotnet", arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using Process process = Process.Start(info)!;
        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
        Task<string> stderrTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit(180_000);
        return (process.ExitCode, stdoutTask.GetAwaiter().GetResult(), stderrTask.GetAwaiter().GetResult());
    }

    private static string LocateRepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "DataScrubber.sln")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate DataScrubber.sln above the test runner directory.");
    }
}
