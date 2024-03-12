using System.Diagnostics;
using System.Text;
using Tomlyn;

namespace SimpleAccountUtils;

internal static class Program
{
    public static int Main(string[] args)
    {   
        if (args.Length == 0)
        {
            Console.WriteLine("Please provide a command and it's arguments.");
            PrintHelp();

            return 1;
        }

        string command = args[0];

        switch (command)
        {
            case "account" when args.Length < 2: return PrintMissingSubCommand();
            case "account":
            {
                string subCommand = args[1];

                return subCommand switch
                {
                    "create" => CreateAccount(),
                    "list" => ListAccounts(),
                    "import" when args.Length < 3 => PrintMissingAccountName(),
                    "import" => ImportAccount(args[2]),
                
                    _ => PrintUnknownSubCommand(subCommand)
                };
            }
            case "repository" when args.Length < 2: return PrintMissingSubCommand();
            case "repository":
            {
                string subCommand = args[1];

                return subCommand switch
                {
                    "set" when args.Length < 3 => PrintMissingAccountName(),
                    "set" => SetRepositoryAccount(args[2]),
                    "get" => GetRepositoryAccount(),
                    
                    _ => PrintUnknownSubCommand(subCommand)
                };
            }
            default: return PrintUnknownCommand(command);
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine("List of commands: ");
        Console.WriteLine("account create");
        Console.WriteLine("account list");
        Console.WriteLine("account import <account name>");
        Console.WriteLine("repository get");
        Console.WriteLine("repository set <account name>");
    }

    private static int PrintUnknownCommand(string command)
    {
        Console.WriteLine($"Unknown command: {command}.");
        PrintHelp();

        return 1;
    }
    
    private static int PrintUnknownSubCommand(string command)
    {
        Console.WriteLine($"Unknown subcommand: {command}.");

        return 1;
    }

    private static int PrintMissingSubCommand()
    {
        Console.WriteLine("Please provide a subcommand and it's arguments.");
        PrintHelp();

        return 1;
    }

    private static int PrintMissingAccountName()
    {
        Console.WriteLine("Please provide an account name.");

        return 1;
    }

    private static int CreateAccount()
    {
        return 0;
    }

    private static int ListAccounts()
    {
        string accountsDir = AppContext.BaseDirectory + "/accounts/";

        if (Directory.Exists(accountsDir))
        {
            string[] accounts = Directory.GetDirectories(accountsDir).Select(Path.GetFileName).ToArray()!;
            
            Console.WriteLine("Accounts: ");
            foreach (string name in accounts)
            {
                string output = name;
                var account = LoadAccountSettings(name);

                if (account != null)
                {
                    bool isImported = IsAccountImported(name, account);

                    if (!isImported)
                    {
                        output += " [NOT IMPORTED]";
                    }
                }
                else
                {
                    output += " [NOT VALID]";
                }
                
                Console.WriteLine(output);
            }
        }
        else
        {
            Console.WriteLine("No accounts exist. Please create some or place some into the account directory.");
        }

        return 0;
    }

    private static int ImportAccount(string name)
    {
        if (!IsAccountName(name))
        {
            Console.WriteLine("Please provide a valid account name, run [account list] to display all accounts.");
            return 1;
        }
        
        AccountSettings? account = LoadAccountSettings(name);

        if (account == null)
        {
            Console.WriteLine("Account is missing a settings file, please fix this account before continuing.");
            return 1;
        }

        if (IsAccountImported(name, account))
        {
            Console.WriteLine("Account is already imported.");
            return 1;
        }
        
        Console.WriteLine("Importing account, this may hang for a few seconds whilst importing the gpg key.");
        
        var process = new Process();
        process.StartInfo.FileName = @"C:\Program Files\Git\bin\bash.exe";
        process.StartInfo.WorkingDirectory = AppContext.BaseDirectory + "/accounts/" + name;
        process.StartInfo.Arguments = $"-c \"gpg --import {account.GpgFileName}\"";
        process.Start();
        
        // todo: bash stdout is outputted to our stdout

        process.WaitForExit();
        
        if (process.ExitCode != 0)
        {
            Console.WriteLine("Unexpected error when importing gpg key.");
            return 1;
        }
        
        string? profile = Environment.GetEnvironmentVariable("USERPROFILE");

        if (profile == null)
        {
            throw new UnreachableException("Failed to fetch home directory...");
        }
        
        Console.WriteLine("Now importing the ssh keys.");

        Directory.CreateDirectory($"{profile}/.ssh/");
        
        File.Copy(
            $"{AppContext.BaseDirectory}/accounts/{name}/{account.SshFileName}",
            $"{profile}/.ssh/{account.SshFileName}_{name}",
            true
            );
        
        File.Copy(
            $"{AppContext.BaseDirectory}/accounts/{name}/{account.SshFileName}.pub",
            $"{profile}/.ssh/{account.SshFileName}_{name}.pub",
            true
            );
        
        Console.WriteLine("Successfully imported account.");
        
        return 0;
    }

    private static int GetRepositoryAccount()
    {
        string? directory = GetTopLevelGitDirectory();
        if (directory == null)
        {
            Console.WriteLine("This command only works inside of a git repository.");
            return 1;
        }

        string? name = GetLocalConfigValue("user.name");
        string? email = GetLocalConfigValue("user.email");
        
        Console.WriteLine($"Currently logged in as {name} - {email}");
        
        return 0;
    }

    private static int SetRepositoryAccount(string name)
    {
        if (!IsAccountName(name))
        {
            Console.WriteLine("Please provide a valid account name, run [account list] to display all accounts.");
            return 1;
        }

        AccountSettings? account = LoadAccountSettings(name);
        
        if (account == null)
        {
            Console.WriteLine("Account is missing a settings file, please fix this account before continuing.");
            return 1;
        }

        bool isImported = IsAccountImported(name, account);
        
        if (!isImported)
        {
            Console.WriteLine($"Please import this account first with: [account import {name}].");
            return 1;
        }
        
        SetLocalConfigValue("user.name", account.Name);
        SetLocalConfigValue("user.email", account.Email);
        SetLocalConfigValue("user.signingkey", account.GpgKey);
        SetLocalConfigValue("commit.gpgsign", "true");
        SetLocalConfigValue("core.sshCommand", $"ssh -i ~/.ssh/{account.SshFileName}_{name}");
        
        return 0;
    }

    private static bool IsAccountName(string name)
    {
        return Directory.Exists($"{AppContext.BaseDirectory}/accounts/{name}");
    }

    private static bool IsAccountImported(string name, AccountSettings account)
    {
        return SshKeyExist(name, account.SshFileName) && IsGpGKeyImported(account.GpgKey);
    }
    
    private static AccountSettings? LoadAccountSettings(string name)
    {
        string accountsFile = AppContext.BaseDirectory + "/accounts/" + name + "/account.toml";

        return File.Exists(accountsFile) ? Toml.ToModel<AccountSettings>(File.ReadAllText(accountsFile, Encoding.UTF8)) : null;
    }

    private static bool SshKeyExist(string name, string sshFileName)
    {
        string? profile = Environment.GetEnvironmentVariable("USERPROFILE");

        if (profile == null)
        {
            throw new UnreachableException("Failed to fetch home directory...");
        }

        return File.Exists($"{profile}/.ssh/{sshFileName}_{name}") && File.Exists($"{profile}/.ssh/{sshFileName}_{name}.pub");
    }
    
    private static bool IsGpGKeyImported(string key)
    {
        var process = new Process();
        process.StartInfo.FileName = @"C:\Program Files\Git\bin\bash.exe";
        process.StartInfo.Arguments = "-c \"gpg -k\"";
        process.StartInfo.RedirectStandardOutput = true;
        process.Start();

        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new UnreachableException("Listing of GPG keys errored?");
        }

        while (process.StandardOutput.ReadLine() is { } line)
        {
            if (line.Contains(key))
            {
                return true;
            }
        }

        return false;
    }
    
    private static string? GetTopLevelGitDirectory() {
        var gitProcess = new Process();
        gitProcess.StartInfo.FileName = "git";
        gitProcess.StartInfo.Arguments = "rev-parse --show-toplevel";
        gitProcess.StartInfo.RedirectStandardOutput = true;
        gitProcess.Start();

        gitProcess.WaitForExit();

        return gitProcess.ExitCode switch
        {
            0 => gitProcess.StandardOutput.ReadLine(),
            128 => null,
            _ => throw new Exception($"Unknown exit code for retrieving git repository location: {gitProcess.ExitCode}")
        };
    }

    
    private static string? GetLocalConfigValue(string key) {
        var gitProcess = new Process();
        gitProcess.StartInfo.FileName = "git";
        gitProcess.StartInfo.Arguments = $"config --local --get {key}";
        gitProcess.StartInfo.RedirectStandardOutput = true;
        gitProcess.Start();

        gitProcess.WaitForExit();

        return gitProcess.ExitCode switch
        {
            0 => gitProcess.StandardOutput.ReadLine(),
            1 or 128 => null,
            _ => throw new Exception($"Unknown exit code for retrieving a local config value: {gitProcess.ExitCode}")
        };
    }
    
    private static void SetLocalConfigValue(string key, string value) {
        var gitProcess = new Process();
        gitProcess.StartInfo.FileName = "git";
        gitProcess.StartInfo.Arguments = @$"config --local {key} ""{value}""";
        gitProcess.Start();

        gitProcess.WaitForExit();
    }
}