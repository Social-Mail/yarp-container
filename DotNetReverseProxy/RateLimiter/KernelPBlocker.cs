namespace DotNetReverseProxy.RateLimiter;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

public class KernelIpBlocker
{
    private const string SetName = "temp_ban";
    private static int DefaultTimeoutSeconds = 900; // 15 minutes

    /// <summary>
    /// Initializes the ipset hash table, flushes old entries, and attaches the iptables DROP rule.
    /// Call this once in Program.cs before app.Run().
    /// </summary>
    public KernelIpBlocker()
    {

        // on testing we will set only 15 seconds...
        if(LocalTesting.IsTesting)
        {
            DefaultTimeoutSeconds = 15;
        }


        // 1. Create the ipset with a 900s default timeout (-exist avoids errors if already created)
        int ipsetCreate = RunCommand("ipset", ["create", SetName, "hash:ip", "timeout", DefaultTimeoutSeconds.ToString(), "-exist"]);
        if (ipsetCreate != 0)
        {
            FailStartup("Failed to initialize ipset.");
        }

        // 2. Flush all entries to guarantee a 100% clean slate on container restart
        RunCommand("ipset", ["flush", SetName]);

        // 3. Check if the iptables rule already exists (-C returns exit code 0 if found)
        var checkExitCode = RunCommand("iptables", ["-C", "INPUT", "-m", "set", "--match-set", SetName, "src", "-j", "DROP"]);

        // 4. Insert the DROP rule at the top of INPUT if it is not already present
        if (checkExitCode != 0)
        {
            RunCommand("iptables", ["-I", "INPUT", "-m", "set", "--match-set", SetName, "src", "-j", "DROP"]);
        }
    }

    /// <summary>
    /// Blocks an IPv4 address in the Linux kernel for 15 minutes (900 seconds).
    /// </summary>
    public bool BanFor15Minutes(string ipAddress)
    {
        if (!IPAddress.TryParse(ipAddress, out var validIp) ||
            validIp.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }

        // '-exist' resets the 900s timer if the IP is already blocked
        int exitCode = RunCommand("ipset", [
            "add", SetName, validIp.ToString(),
            "timeout", DefaultTimeoutSeconds.ToString(),
            "-exist"
        ]);

        return exitCode == 0;
    }

    /// <summary>
    /// Manually removes an IP address from the blocklist before the 15 minutes expire.
    /// </summary>
    public bool Unban(string ipAddress)
    {
        if (!IPAddress.TryParse(ipAddress, out var validIp) ||
            validIp.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }

        int exitCode = RunCommand("ipset", ["del", SetName, validIp.ToString(), "-exist"]);
        return exitCode == 0;
    }

    private int RunCommand(string fileName, IEnumerable<string> args)
    {

        Console.WriteLine($"{fileName} {string.Join(",", args)}");

        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = Process.Start(startInfo);
        if (process is null) return -1;

        process.WaitForExit(2000);
        return process.ExitCode;
    }

    private static void FailStartup(string reason)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.WriteLine("==================================================================");
        Console.Error.WriteLine($"FATAL STARTUP ERROR: {reason}");
        Console.Error.WriteLine("The container lacks network admin privileges.");
        Console.Error.WriteLine("Add 'cap_add: [NET_ADMIN]' to your docker-compose.yml and run as root.");
        Console.Error.WriteLine("==================================================================");
        Console.ResetColor();

        Environment.Exit(1);
    }
}
