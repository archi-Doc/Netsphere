// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Netsphere.Crypto;
using Netsphere.Relay;
using Tinyhand;

namespace Netsphere.Version;

[TinyhandObject]
public partial record VersionData
{
    private const string Filename = "Version.tinyhand";

    public VersionData()
    {
    }

    public static VersionData Load()
    {
        try
        {
            var bin = File.ReadAllBytes(Filename);
            var data = TinyhandSerializer.DeserializeObjectFromUtf8<VersionData>(bin) ?? new();

            // The cached responses are not serialized; rebuild them so that loaded tokens are served after a restart.
            if (data.Development is { } development)
            {
                data.developmentResponse = new(development);
            }

            if (data.Release is { } release)
            {
                data.releaseResponse = new(release);
            }

            return data;
        }
        catch
        {
            return new();
        }
    }

    [Key(0)]
    public CertificateToken<VersionInfo>? Development { get; private set; }

    [Key(1)]
    public CertificateToken<VersionInfo>? Release { get; private set; }

    private GetVersionResponse developmentResponse = new();
    private GetVersionResponse releaseResponse = new();

    public void Update(CertificateToken<VersionInfo> token)
    {
        if (token.Target.VersionKind == VersionInfo.Kind.Development)
        {
            this.Development = token;
            this.developmentResponse = new(token);
        }
        else if (token.Target.VersionKind == VersionInfo.Kind.Release)
        {
            this.Release = token;
            this.releaseResponse = new(token);
        }

        _ = Task.Run(() => this.Save());
    }

    public void Save()
    {
        try
        {
            var bin = TinyhandSerializer.SerializeObjectToUtf8(this);
            File.WriteAllBytes(Filename, bin);
        }
        catch
        {
        }
    }

    public GetVersionResponse? GetVersionResponse(VersionInfo.Kind versionKind)
    {
        if (versionKind == VersionInfo.Kind.Development)
        {
            return this.developmentResponse;
        }
        else if (versionKind == VersionInfo.Kind.Release)
        {
            return this.releaseResponse;
        }
        else
        {
            return default;
        }
    }

    public long GetCurrentMics(VersionInfo.Kind versionKind)
    {
        CertificateToken<VersionInfo>? token = default;
        if (versionKind == VersionInfo.Kind.Development)
        {
            token = this.Development;
        }
        else if (versionKind == VersionInfo.Kind.Release)
        {
            token = this.Release;
        }

        if (token is null)
        {
            return 0;
        }

        return token.Target.VersionMics;
    }

    public void Log(ILogger logger)
    {
        logger.GetWriter()?.Write($"Development: {this.Development?.Target.ToString()}, Release: {this.Release?.Target.ToString()}");
    }
}
