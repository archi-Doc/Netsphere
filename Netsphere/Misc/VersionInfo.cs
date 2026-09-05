// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Netsphere.Version;

/// <summary>
/// Describes a version kind, version number, and release timestamp.
/// </summary>
[TinyhandObject]
public sealed partial record class VersionInfo
{
    public VersionInfo()
    {
    }

    public VersionInfo(int versionIdentifier, Kind versionKind, long versionMics, int versionInt)
    {
        this.VersionIdentifier = versionIdentifier;
        this.VersionKind = versionKind;
        this.VersionMics = versionMics;
        this.VersionInt = versionInt;
    }

    /// <summary>
    /// Identifies the category of version information.
    /// </summary>
    public enum Kind : byte
    {
        Development,
        Release,
    }

    [Key(0)]
    public int VersionIdentifier { get; private set; }

    [Key(1)]
    public Kind VersionKind { get; private set; }

    [Key(2)]
    public long VersionMics { get; private set; }

    [Key(3)]
    public int VersionInt { get; private set; }
}
