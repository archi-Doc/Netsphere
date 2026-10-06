// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Arc.Unit;

namespace Netsphere.Interfaces;

public static class RemoteDataHelper
{
    /// <summary>
    /// Uploads the current log file to the remote data server.
    /// </summary>
    /// <param name="netTerminal">The terminal used to connect.</param>
    /// <param name="fileLogger">The file logger whose current file is uploaded.</param>
    /// <param name="remoteNode">The remote node text, or null to skip.</param>
    /// <param name="remotePrivateKey">The private key that signs the agreement, or null to skip.</param>
    /// <param name="identifier">The identifier (file name) on the server.</param>
    /// <returns>The upload result: InvalidOperation when the parameters are missing, NoNetwork when no connection could be established,
    /// UnknownError when reading or sending failed, otherwise the result reported by the server.</returns>
    public static async Task<NetResult> SendLog(NetTerminal netTerminal, IFileLogOutput? fileLogger, string? remoteNode, string? remotePrivateKey, string identifier)
    {
        if (fileLogger is null ||
            string.IsNullOrEmpty(remoteNode) ||
            string.IsNullOrEmpty(remotePrivateKey))
        {
            return NetResult.InvalidOperation;
        }

        var r = await NetHelper.TryGetStreamService<IRemoteData>(netTerminal, remoteNode, remotePrivateKey, 100_000_000);
        if (r.Connection is null ||
            r.Service is null)
        {
            return NetResult.NoNetwork;
        }

        try
        {
            await fileLogger.FlushAsync(false);

            var path = fileLogger.GetCurrentPath();
            using var fileStream = File.OpenRead(path);
            var sendStream = await r.Service.Put(identifier, fileStream.Length);
            if (sendStream is null)
            {
                return NetResult.NoTransmission;
            }

            var r3 = await NetHelper.StreamToSendStream(fileStream, sendStream);
            return r3.IsSuccess ? r3.Value : r3.Result;
        }
        catch
        {
            return NetResult.UnknownError;
        }
        finally
        {
            r.Connection.Dispose();
        }
    }
}
