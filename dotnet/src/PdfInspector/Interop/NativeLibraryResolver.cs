#if NET6_0_OR_GREATER
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;

namespace PdfInspector.Interop;

/// <summary>
/// Locates the native <c>pdf_inspector_ffi</c> library.
/// </summary>
/// <remarks>
/// <para>
/// When the package is consumed from NuGet the runtime resolves
/// <c>runtimes/{rid}/native</c> on its own and this resolver simply defers to
/// it. The extra probing exists for the cases where it cannot:
/// </para>
/// <list type="bullet">
///   <item><description>
///     <c>PDF_INSPECTOR_NATIVE_LIBRARY</c> — an explicit path to the shared
///     library (or to the directory containing it). Takes precedence over
///     everything else, which is what the repo's own tests use to run against
///     a fresh <c>cargo build</c>.
///   </description></item>
///   <item><description>
///     The application base directory, for xcopy deployments where the native
///     library sits next to the managed assembly.
///   </description></item>
/// </list>
/// <para>
/// Returning <see cref="IntPtr.Zero"/> from the resolver hands the load back
/// to the default probing logic, so a failure here is never worse than not
/// having a resolver at all.
/// </para>
/// </remarks>
internal static class NativeLibraryResolver
{
    /// <summary>Environment variable holding an explicit library path.</summary>
    internal const string PathVariable = "PDF_INSPECTOR_NATIVE_LIBRARY";

    private static int _installed;

    /// <summary>Installs the resolver once per process.</summary>
    internal static void Install()
    {
        if (Interlocked.Exchange(ref _installed, 1) != 0)
        {
            return;
        }

        NativeLibrary.SetDllImportResolver(typeof(NativeLibraryResolver).Assembly, Resolve);
    }

    /// <summary>Platform-specific file name of the shared library.</summary>
    internal static string LibraryFileName
    {
        get
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return NativeMethods.LibraryName + ".dll";
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return "lib" + NativeMethods.LibraryName + ".dylib";
            }

            return "lib" + NativeMethods.LibraryName + ".so";
        }
    }

    private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (!string.Equals(libraryName, NativeMethods.LibraryName, StringComparison.Ordinal))
        {
            return IntPtr.Zero;
        }

        foreach (string candidate in Candidates())
        {
            if (NativeLibrary.TryLoad(candidate, out IntPtr handle))
            {
                return handle;
            }
        }

        // Fall back to the runtime's own probing (NuGet RID assets, the
        // system library path, …).
        return IntPtr.Zero;
    }

    private static IEnumerable<string> Candidates()
    {
        string fileName = LibraryFileName;

        string? configured = Environment.GetEnvironmentVariable(PathVariable);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            // Accept either the library itself or the directory holding it.
            if (Directory.Exists(configured))
            {
                yield return Path.Combine(configured!, fileName);
            }
            else
            {
                yield return configured!;
            }
        }

        string baseDirectory = AppContext.BaseDirectory;
        if (!string.IsNullOrEmpty(baseDirectory))
        {
            yield return Path.Combine(baseDirectory, fileName);
        }
    }
}
#endif
