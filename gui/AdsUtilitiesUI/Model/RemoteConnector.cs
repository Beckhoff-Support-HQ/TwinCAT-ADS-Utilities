using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Formats.Asn1;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace AdsUtilitiesUI.Model
{
    internal static class RemoteConnector
    {
        public static async Task RdpConnect(string ipAddress)
        {
            // For Big Windows -> Start RDP w/ IP
            // HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Terminal Server -- fDenyTSConnections > 0
            // (runas) netsh advfirewall firewall set rule group=\"Remote Desktop\" new enable=Yes

            var processStartInfo = new ProcessStartInfo
            {
                FileName = "mstsc",
                Arguments = $"/v:{ipAddress}",
                UseShellExecute = true ,
                WindowStyle = ProcessWindowStyle.Normal
            };

            using var rdpProcess = new Process { StartInfo = processStartInfo, EnableRaisingEvents = true };

            var tcs = new TaskCompletionSource<bool>();

            rdpProcess.Exited += (sender, args) => tcs.TrySetResult(true);

            rdpProcess.Start();

            await tcs.Task;
        }

        public static async Task CerhostConnect(string ipAddress)
        {
            string cerhostPath = await GetCerhostPathAsync();

            if (string.IsNullOrEmpty(cerhostPath))
                return;

            using TcpClient tcpClient = new ();
            var connectTask = tcpClient.ConnectAsync(ipAddress, 987);

            if (await Task.WhenAny(connectTask, Task.Delay(250)) == connectTask)
            {
                // Connection successful
                bool cerhostEnabled = tcpClient.Connected;
            }
            else
            {
                // Timeout --> ToDo: Dialog "Enable CerHost and reboot?"
            }

            var processStartInfo = new ProcessStartInfo
            {
                FileName = cerhostPath,
                Arguments = ipAddress
            };

            using var cerhostProcess = new Process { StartInfo = processStartInfo, EnableRaisingEvents = true };

            var tcs = new TaskCompletionSource<bool>();

            cerhostProcess.Exited += (sender, args) => tcs.TrySetResult(true);

            cerhostProcess.Start();

            string output = await cerhostProcess.StandardOutput.ReadToEndAsync();
            string error = await cerhostProcess.StandardError.ReadToEndAsync();

            await tcs.Task;
        }

        public static async Task SshPowerShellConnect(string ipAddress, string targetName)
        {
            string username = string.Empty;

            SshDialog sshDialog = new("Enter Username", "Enter Username:", "Administrator");

            if (sshDialog.ShowDialog() == true)
            {
                username = sshDialog.ResponseText;
            }

            if (string.IsNullOrEmpty(username))
                return;

            bool addSshKey = sshDialog.AddSshKey;

            if (!addSshKey)
            {
                await SshConnectWithoutKey(username, ipAddress);
                return;
            }
            else
            {
                await GenerateAndDeploySSHKeyAsync(username, targetName, ipAddress);
                await SshConnectWithoutKey(username, ipAddress);
            }
        }

        public static async Task GenerateAndDeploySSHKeyAsync(string username, string targetName, string targetIp)
        {
            try
            {
                string sshDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh");
                string sshKeyName = $"{targetName}{DateTime.Now:yyyy-MM-dd_HH-mm-ss}";
                string keyPath = Path.Combine(sshDirectory, sshKeyName);
                string configPath = Path.Combine(sshDirectory, "config");

                string comment = $"{Environment.MachineName}_{Environment.UserName}";

                // generate SSH-Key
                string keyGenCommand = $"-t ed25519 -f \"{keyPath}\" -C \"{comment}\" -N \"\"";
                await ExecuteSshKeygenCommand(keyGenCommand);

                // copy public key to target system
                string publicKeyPath = $"{keyPath}.pub";
                string scpCommand = @$"
                    scp -o StrictHostKeyChecking=no '{publicKeyPath}' {username}@{targetIp}:~/.ssh ;
                    ssh {username}@{targetIp} -t 'cd ~/.ssh && cat {sshKeyName}.pub >> ./authorized_keys && rm ./{sshKeyName}.pub'
                ";
                await ExecuteShellCommandAsync(scpCommand);

                // update ssh configuration
                var configEntry = new StringBuilder()
                    .AppendLine()
                    .AppendLine($"Host {targetName}")
                    .AppendLine($"\tUser {username}")
                    .AppendLine($"\tHostName {targetName}")
                    .AppendLine($"\tIdentityFile \"{keyPath}\"")
                    .AppendLine()
                    .AppendLine($"Host {targetIp}")
                    .AppendLine($"\tUser {username}")
                    .AppendLine($"\tHostName {targetIp}")
                    .AppendLine($"\tIdentityFile \"{keyPath}\"")
                    .ToString();

                await File.AppendAllTextAsync(configPath, configEntry);
            }
            catch (Exception){}
        }

        public static async Task ExecuteShellCommandAsync(string command)
        {
            var processStartInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = command,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = false
            };

            try
            {
                using var process = new Process { StartInfo = processStartInfo, EnableRaisingEvents = true };

                var tcs = new TaskCompletionSource<bool>();

                process.Exited += (sender, args) => tcs.TrySetResult(true);

                process.Start();

                string output = await process.StandardOutput.ReadToEndAsync();
                string error = await process.StandardError.ReadToEndAsync();

                await tcs.Task;
            }
            catch (Exception) { }
        }

        private static async Task ExecuteSshKeygenCommand(string command)
        {
            ProcessStartInfo processStartInfo = new()
            {
                WindowStyle = ProcessWindowStyle.Hidden,
                FileName = "ssh-keygen",
                Arguments = command,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true
            };
            try
            {
                using var process = new Process { StartInfo = processStartInfo, EnableRaisingEvents = true };

                var tcs = new TaskCompletionSource<bool>();

                process.Exited += (sender, args) => tcs.TrySetResult(true);

                process.Start();

                string output = await process.StandardOutput.ReadToEndAsync();
                string error = await process.StandardError.ReadToEndAsync();

                await tcs.Task;
            }
            catch (Exception) { }
        }

        public static async Task SshConnectWithoutKey(string username, string ipAddress)
        {
            string sshCommand = $"ssh {username}@{ipAddress}";

            var processStartInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoExit -Command \"{sshCommand}\"",
                UseShellExecute = true,
                CreateNoWindow = false    
            };

            try
            {
                using var process = new Process { StartInfo = processStartInfo, EnableRaisingEvents = true };

                var tcs = new TaskCompletionSource<bool>();

                process.Exited += (sender, args) => tcs.TrySetResult(true);

                process.Start();

                string output = await process.StandardOutput.ReadToEndAsync();
                string error = await process.StandardError.ReadToEndAsync();

                await tcs.Task;
            }
            catch (Exception) { }
        }

        private static async Task<string> GetCerhostPathAsync()
        {
            if (!Directory.Exists(GlobalVars.AppFolder))
            {
                Directory.CreateDirectory(GlobalVars.AppFolder);
            }

            string configFilePath = Path.Combine(GlobalVars.AppFolder, GlobalVars.CerhostConfigFileName);

            // Check if config file exists and contains the correct path
            if (File.Exists(configFilePath))
            {
                string json = await File.ReadAllTextAsync(configFilePath);
                string cerhostPath = JsonSerializer.Deserialize<string>(json);
                if (File.Exists(cerhostPath))
                {
                    return cerhostPath;
                }
            }

            // Path not found --> Select file dialog
            OpenFileDialog openFileDialog = new()
            {
                Filter = "CERHOST.exe|cerhost.exe",
                Title = "Select cerhost.exe - path will be written to config file"
            };
            
            if (openFileDialog.ShowDialog() is true)
            {
                // Save path in config file
                string json = JsonSerializer.Serialize(openFileDialog.FileName);
                await File.WriteAllTextAsync(configFilePath, json);
                return openFileDialog.FileName;
            }

            return string.Empty;
        }

    }
}
