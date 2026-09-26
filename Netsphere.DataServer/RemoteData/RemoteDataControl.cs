// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Netsphere.Crypto;

namespace RemoteDataServer;

public class RemoteDataControl
{
    // private const int ReadBufferSize = 1024 * 1024 * 4;

    public RemoteDataControl(UnitOptions unitOptions, ILogger<RemoteDataControl> logger)
    {
        this.logger = logger;
        this.baseDirectory = string.IsNullOrEmpty(unitOptions.DataDirectory) ?
            unitOptions.ProgramDirectory : unitOptions.DataDirectory;
        this.limitAreement = new ConnectionAgreement() with
        {
            MaxStreamLength = 100_000_000,
        };
    }

    #region FieldAndProperty

    public bool Initialized { get; private set; }

    public string DataDirectory { get; private set; } = string.Empty;

    public SignaturePublicKey RemotePublicKey { get; set; }

    private readonly ILogger logger;
    private readonly ConnectionAgreement limitAreement;
    private readonly string baseDirectory;

    #endregion

    public void Initialize(string directory)
    {
        this.DataDirectory = PathHelper.CombineDirectory(this.baseDirectory, directory);
        Directory.CreateDirectory(this.DataDirectory);

        this.Initialized = true;
    }

    public async Task<NetResult> UpdateAgreement(CertificateToken<ConnectionAgreement> token)
    {
        this.ThrowIfNotInitialized();

        var transmissionContext = TransmissionContext.Current;
        if (!transmissionContext.ServerConnection.ValidateAndVerifyWithSalt(token))
        {// Invalid token
            return NetResult.NotAuthenticated;
        }
        else if (!token.PublicKey.Equals(this.RemotePublicKey))
        {// Invalid public key
            return NetResult.NotAuthenticated;
        }

        if (!token.Target.IsInclusive(this.limitAreement))
        {
            return NetResult.Refused;
        }

        return NetResult.Success;
    }

    public async Task<ReceiveStream?> Get(string identifier)
    {
        this.ThrowIfNotInitialized();

        var transmissionContext = TransmissionContext.Current;
        var path = this.IdentifierToPath(identifier);
        if (path is null)
        {
            transmissionContext.Result = NetResult.NotFound;
            return default;
        }

        try
        {
            using var fileStream = File.OpenRead(path);
            (_, var sendStream) = transmissionContext.GetSendStream(fileStream.Length);
            if (sendStream is null)
            {
                transmissionContext.Result = NetResult.NotFound;
                return default;
                }

            this.logger.GetWriter(LogLevel.Information)?.Write($"Get: {identifier}");
            var result = await NetHelper.StreamToSendStream(fileStream, sendStream);
            this.logger.GetWriter(LogLevel.Information)?.Write($"Get ({result}): {identifier} {sendStream.SentLength} bytes");
        }
        catch
        {
            transmissionContext.Result = NetResult.NotFound;
            return default;
        }

        return default;
    }

    public async Task<SendStreamAndReceive<NetResult>?> Put(string identifier, long maxLength)
    {
        this.ThrowIfNotInitialized();

        var transmissionContext = TransmissionContext.Current;
        var path = this.IdentifierToPath(identifier);
        if (path is null)
        {
            transmissionContext.Result = NetResult.InvalidOperation;
            return default;
        }

        var result = NetResult.UnknownError;
        try
        {
            using var fileStream = File.Create(path);
            var receiveStream = transmissionContext.GetReceiveStream<NetResult>();

            this.logger.GetWriter(LogLevel.Information)?.Write($"Put: {identifier}");
            result = await NetHelper.ReceiveStreamToStream(receiveStream, fileStream);
            this.logger.GetWriter(LogLevel.Information)?.Write($"Put({result}): {identifier} {receiveStream.ReceivedLength} bytes");

            receiveStream.SendAndDispose(result);
        }
        catch
        {
            transmissionContext.Result = NetResult.InvalidOperation;
            return default;
        }

        return default;
    }

    private string? IdentifierToPath(string identifier)
    {
        if (string.IsNullOrEmpty(identifier) ||
            Path.IsPathRooted(identifier))
        {
            return null;
        }

        // Resolve the path and require it to stay inside the data directory; substring checks miss forms such as "a/.." or "a:b".
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(this.DataDirectory)) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(root, identifier));
        var isWindows = OperatingSystem.IsWindows();
        if (!path.StartsWith(root, isWindows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) ||
            path.Length == root.Length ||
            (isWindows && path.AsSpan(root.Length).Contains(':')))
        {// Outside the data directory, the directory itself, or an NTFS alternate data stream.
            return null;
        }

        return path;
    }

    private void ThrowIfNotInitialized()
    {
        if (!this.Initialized)
        {
            throw new InvalidOperationException();
        }
    }
}
