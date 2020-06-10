using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.IO;
using System.Threading.Tasks;
using LibGit2Sharp;
using Meziantou.Framework.Win32;
using Microsoft.Extensions.Configuration;

namespace LifeLogr
{
    class Program
    {
        private static IConfiguration _configuration;

        static async Task<int> Main(string[] args)
        {
            _configuration = new ConfigurationBuilder()
                            .SetBasePath(Directory.GetCurrentDirectory())
                            .AddJsonFile("appsettings.json", true, true)
                            .AddEnvironmentVariables()
                            .Build();

            var rootCommand = new RootCommand("config")
            {
                //new Option<string>(new[] {"add-message", "-a"}, () => null, "Shortcut command to add a new message to the log"),
                //new Option<bool>(new[] {"--push", "-p"}, () => false, "When adding to the log, flags to push")
            };

            //rootCommand.Handler = CommandHandler.Create<string, bool>(RootCommandHandler);

            SetupConfigCommand(rootCommand);
            SetupAddCommand(rootCommand);

            // Parse the incoming args and invoke the handler
            return await rootCommand.InvokeAsync(args);
        }

        private static async Task AddMessage(string message, bool push)
        {
            if (String.IsNullOrWhiteSpace(message))
            {
                throw new Exception("Cannot add empty message.");
            }

            using (var repository = new Repository(UserSettings.RepositoryPath))
            {
                var status = repository.RetrieveStatus();
                if (status.IsDirty)
                {
                    throw new Exception("Cannot add log entry whilst repository has pending changes.");
                }

                var dateTime = DateTime.Now;
                var filePath = await WriteLogFile(message, dateTime);

                Commands.Stage(repository, filePath);

                var signature = repository.Config.BuildSignature(dateTime);

                repository.Commit(message, signature, signature, new CommitOptions());

                if (push || UserSettings.PushByDefault)
                {
                    PushBranch(repository);
                }
            }

            Console.WriteLine(message);
        }

        private static void PushBranch(Repository repository)
        {
            var remoteName = repository.Head.RemoteName;

            if (String.IsNullOrWhiteSpace(remoteName))
            {
                throw new Exception("The current branch is not tracking a remote branch");
            }

            var remote = repository.Network.Remotes[remoteName]; // remote.origin.url
            var uri = new Uri(remote.Url);
            var credentials = GetCredentialsFromWindowsStore(uri);

            WriteOutInfo($"Pushing commits...");
            var pushOptions = new PushOptions()
            {
                CredentialsProvider = (url, user, cred) => new UsernamePasswordCredentials
                {
                    Username = credentials.Username,
                    Password = credentials.Password
                },
            };
            repository.Network.Push(repository.Head, pushOptions);
        }

        private static UsernamePassword GetCredentialsFromWindowsStore(Uri repositoryUri)
        {
            var credential = CredentialManager.ReadCredential($"git:{repositoryUri.Scheme}://{repositoryUri.Host}");
            return new UsernamePassword()
            {
                Username = credential.UserName,
                Password = credential.Password
            };
        }

        private static async Task<string> WriteLogFile(string message, DateTime dateTime)
        {
            var basePath = UserSettings.BasePath;
            var monthFolderName = dateTime.ToString("yyyy-MM");
            var fileName = $"{dateTime:yyyy-MM-dd hh-mm-ss}.txt";
            var monthFolderPath = Path.Combine(basePath, monthFolderName);

            var filePath = Path.Combine(monthFolderPath, fileName);
            if (File.Exists(filePath))
            {
                throw new Exception("Cannot add log that already exists.");
            }

            if (!Directory.Exists(monthFolderPath))
            {
                Directory.CreateDirectory(monthFolderPath);
            }

            await File.WriteAllTextAsync(filePath, message);

            return filePath;
        }

        private static void WriteOutError(string errorMessage)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(errorMessage);
            Console.ResetColor();
        }

        private static void WriteOutInfo(string errorMessage)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine(errorMessage);
            Console.ResetColor();
        }

        private static async Task<int> RootCommandHandler(string addMessage, bool push)
        {
            try
            {
                await AddMessage(addMessage, push);
                return 0;
            }
            catch (Exception exc)
            {
                WriteOutError(exc.Message);
                return 1;
            }
        }
    

        private static void SetupAddCommand(RootCommand rootCommand)
        {
            var messageOption = new Option<string>(new[] {"--message", "-m"}, () => null, "Message to log")
            {
                Required = true
            };

            var pushOption = new Option<bool>(new[] {"--push", "-p"}, () => false, "When adding to the log, flags to push");

            var command = new Command("add", "Adds a new message to the log")
            {
                messageOption,
                pushOption
            };

            rootCommand.Add(command);
            command.Handler = CommandHandler.Create<string, bool>(AddCommandHandler);
        }

        private static async Task<int> AddCommandHandler(string message, bool push)
        {
            try
            {
                await AddMessage(message, push);
                return 0;
            }
            catch (Exception exc)
            {
                WriteOutError(exc.Message);
                return 1;
            }
        }

        private static void SetupConfigCommand(RootCommand rootCommand)
        {
            var configCommand = new Command("config")
            {
                new Option<DirectoryInfo>("--path", () => null, "Path of repository to make logs to"),
                new Option<bool?>("--push-by-default", () => null, "Flag to indicate to immediately push all commits to the remote")
            };
            rootCommand.Add(configCommand);
            configCommand.Handler = CommandHandler.Create<DirectoryInfo, bool?>(ConfigCommandHandler);
        }

        private static int ConfigCommandHandler(DirectoryInfo path, bool? pushByDefault)
        {
            try
            {
                if (path != null)
                {
                    UserSettings.RepositoryPath = path.ToString();
                }

                if (pushByDefault.HasValue)
                {
                    UserSettings.PushByDefault = pushByDefault.Value;
                }

                Console.WriteLine($"Repository path: {UserSettings.RepositoryPath}");
                Console.WriteLine($"Push by default: {UserSettings.PushByDefault}");

                return 0;
            }
            catch (Exception exc)
            {
                WriteOutError(exc.Message);
                return 1;
            }
        }
    }

    public static class UserSettings
    {
        public static string BasePath => RepositoryPath;

        public static string RepositoryPath
        {
            get => Environment.GetEnvironmentVariable("lifeLogRepositoryPath", EnvironmentVariableTarget.User);
            set => Environment.SetEnvironmentVariable("lifeLogRepositoryPath", value, EnvironmentVariableTarget.User);
        }

        public static bool PushByDefault
        {
            get => bool.TryParse(Environment.GetEnvironmentVariable("lifeLogPushByDefault", EnvironmentVariableTarget.User), out bool result) && result;
            set => Environment.SetEnvironmentVariable("lifeLogPushByDefault", value.ToString(), EnvironmentVariableTarget.User);
        }
    }

    public class UsernamePassword
    {
        public string Username { get; set; }
        public string Password { get; set; }
    }
}
