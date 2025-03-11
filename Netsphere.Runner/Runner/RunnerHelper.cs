// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using Arc.Unit;
using Docker.DotNet;

namespace Netsphere.Runner;

internal static class RunnerHelper
{
    public static async Task<DockerClient?> CreateDockerClient()
    {
        var client = new DockerClientConfiguration().CreateClient();
        try
        {
            _ = await client.Containers.ListContainersAsync(new() { Limit = 10, });
        }
        catch
        {// No docker
            return default;
        }

        return client;
    }

    /*public static Task DispatchCommand(ILogger logger, string filename, string arguments)
    {
        logger.TryGet()?.Log($"Dispatch: {filename}");

        var startInfo = new ProcessStartInfo
        {
            FileName = filename,
            Arguments = arguments,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };

        try
        {
            using (var process = Process.Start(startInfo))
            {
                if (process is null)
                {
                    return Task.CompletedTask;
                }

                var output = process.StandardOutput.ReadToEnd();
                Console.WriteLine(output);
            }

            return Task.CompletedTask;
        }
        catch
        {
            logger.TryGet(LogLevel.Fatal)?.Log("A fatal error occurred during execution.");
            return Task.CompletedTask;
        }
    }*/

    public static Task DispatchCommand(ILogger logger, string command)
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

            return process.WaitForExitAsync();
        }
        catch
        {
            logger.TryGet(LogLevel.Fatal)?.Log("A fatal error occurred during execution.");
            return Task.CompletedTask;
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
