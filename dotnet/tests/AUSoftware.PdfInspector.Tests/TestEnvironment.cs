using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace AUSoftware.PdfInspector.Tests;

/// <summary>
/// Points the binding at the locally built native library and locates the
/// repository's PDF fixtures.
/// </summary>
/// <remarks>
/// The tests run against <c>dotnet/native/target/{release,debug}</c> rather
/// than a packaged RID asset, so <c>cargo build</c> is the only prerequisite.
/// Setting <c>PDF_INSPECTOR_NATIVE_LIBRARY</c> externally overrides this and
/// lets the same suite run against a packaged build.
/// </remarks>
internal static class TestEnvironment
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PDF_INSPECTOR_NATIVE_LIBRARY")))
        {
            return;
        }

        string fileName = NativeLibraryFileName();
        foreach (string profile in new[] { "release", "debug" })
        {
            string candidate = Path.Combine(RepositoryRoot, "dotnet", "native", "target", profile, fileName);
            if (File.Exists(candidate))
            {
                Environment.SetEnvironmentVariable("PDF_INSPECTOR_NATIVE_LIBRARY", candidate);
                return;
            }
        }

        throw new InvalidOperationException(
            $"Could not find {fileName} under dotnet/native/target. "
                + "Run `cargo build --release` in dotnet/native, or set PDF_INSPECTOR_NATIVE_LIBRARY.");
    }

    /// <summary>Absolute path to a PDF in <c>tests/fixtures</c>.</summary>
    internal static string Fixture(string name) =>
        Path.Combine(RepositoryRoot, "tests", "fixtures", name);

    /// <summary>The bytes of a PDF in <c>tests/fixtures</c>.</summary>
    internal static byte[] FixtureBytes(string name) => File.ReadAllBytes(Fixture(name));

    /// <summary>Walks up from the test assembly to the repository root.</summary>
    internal static string RepositoryRoot { get; } = FindRepositoryRoot();

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "tests", "fixtures"))
                && File.Exists(Path.Combine(directory.FullName, "Cargo.toml")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            "Could not locate the pdf-inspector repository root from " + AppContext.BaseDirectory);
    }

    private static string NativeLibraryFileName()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return "pdf_inspector_ffi.dll";
        }

        return RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
            ? "libpdf_inspector_ffi.dylib"
            : "libpdf_inspector_ffi.so";
    }
}
