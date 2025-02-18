// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Reflection;

namespace Netsphere.Version;

public static class VersionHelper
{
    static VersionHelper()
    {
        var assembly = Assembly.GetEntryAssembly();
        if (assembly is not null)
        {
            Update(assembly);
        }
    }

    public static void SetAssembly(string assemblyName)
    {
        foreach (var x in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (x.ManifestModule.Name.Contains(assemblyName))
            {
                Update(x);
                break;
            }
        }
    }

    private static void Update(Assembly assembly)
    {
        var version = assembly.GetName()?.Version;
        if (version is not null)
        {
            MajorVersion = version.Major;
            MinorVersion = version.Minor;
            Build = version.Build;
        }

        /*try
        {
            AssemblyHash = (uint)Arc.Crypto.XxHash3.Hash64(File.ReadAllBytes(assembly.Location));
        }
        catch
        {
        }*/

        // VersionString = $"{MajorVersion}.{MinorVersion}.{Build}+{AssemblyHash:x8}";
        VersionString = $"{MajorVersion}.{MinorVersion}.{Build}";
        VersionInt = (MajorVersion << 24) + (MinorVersion << 16) + (Build << 8);
    }

    public static string VersionString { get; private set; } = "0.0.0";

    // public static uint AssemblyHash { get; private set; }

    public static int MajorVersion { get; private set; }

    public static int MinorVersion { get; private set; }

    public static int Build { get; private set; }

    public static int VersionInt { get; private set; }
}
