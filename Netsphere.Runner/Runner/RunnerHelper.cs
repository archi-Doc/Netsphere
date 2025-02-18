// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using Arc.Unit;

namespace Netsphere.Runner;

internal static class RunnerHelper
{
    public static void DispatchCommand(ILogger logger, string command)
    {
        string shellName;
        if (Environment.OSVersion.Platform == PlatformID.Win32NT)
        {
            shellName = "PowerShell.exe";
        }
        else
        {
            shellName = @"/bin/bash";
        }

        logger.TryGet()?.Log($"Dispatch: {command}");

        var startInfo = new ProcessStartInfo
        {
            FileName = shellName,
            Arguments = "-c \"" + command + "\"",
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };

        try
        {
            var process = new Process { StartInfo = startInfo };
            process.Start();
        }
        catch
        {
            logger.TryGet(LogLevel.Fatal)?.Log("A fatal error occurred during execution.");
        }
    }

    /*public static async Task<string?> ExecuteCommand(ILogger logger, string command)
    {
        string shellName;
        if (Environment.OSVersion.Platform == PlatformID.Win32NT)
        {
            shellName = "PowerShell.exe";
        }
        else
        {
            shellName = @"/bin/bash";
        }

        logger.TryGet()?.Log($"Command: {command}");
        if (string.IsNullOrEmpty(command))
        {
            command = "echo hello";
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = shellName,
            Arguments = "-c \"" + command + "\"", // "-c \"Start-Sleep -s 3; echo hello\"",
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };

        try
        {
            using (var process = new Process { StartInfo = startInfo })
            {
                process.Start();
                await process.WaitForExitAsync(ThreadCore.Root.CancellationToken);
                var result = await process.StandardOutput.ReadToEndAsync();

                return result;
            }
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch
        {
            logger.TryGet(LogLevel.Fatal)?.Log("A fatal error occurred during execution.");
            return null;
        }
    }*/
}
