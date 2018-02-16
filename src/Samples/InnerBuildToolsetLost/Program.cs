using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Framework;
using Microsoft.Build.Logging;

namespace InnerBuildToolsetLost
{
    class Program
    {
        static void Main(string [] args)
        {
            var msbuildToolsPath = args.Length > 0 ? args[0].Trim('"') : FindOnPath("msbuild.exe");
            if (string.IsNullOrEmpty(msbuildToolsPath))
            {
                Console.WriteLine("Could not find MsBuild. Ensure it is on the PATH or provide it as an argument.");
                return;
            }

            if (!Directory.Exists(msbuildToolsPath))
            {
                Console.WriteLine($"'{msbuildToolsPath}' does not exist.");
                return;
            }

            var projectCollection = ProjectCollection.GlobalProjectCollection;
            var msbuildToolsVersion = projectCollection.DefaultToolsVersion;
            projectCollection.RemoveAllToolsets();
            projectCollection.AddToolset(new Toolset(msbuildToolsVersion, msbuildToolsPath, projectCollection, msbuildToolsPath));
            projectCollection.DefaultToolsVersion = msbuildToolsVersion;

            var logLines = new List<string>();
            var consoleLogger = new ConsoleLogger(LoggerVerbosity.Minimal, message =>
            {
                var trimmedMessage = message.Trim();
                if (trimmedMessage.StartsWith("MsBuildToolsPath"))
                {
                    logLines.Add(trimmedMessage);
                }
            }, null, null);
            projectCollection.RegisterLogger(consoleLogger);

            var fileLogger = new FileLogger();
            fileLogger.Parameters = $"LOGFILE={Directory.GetCurrentDirectory()}\\msbuild.log";
            fileLogger.Verbosity = LoggerVerbosity.Diagnostic;
            projectCollection.RegisterLogger(fileLogger);

            var outerProject = projectCollection.LoadProject("OuterProject.proj");
            projectCollection.LoadProject("InnerProject.proj");

            var projectInstance = outerProject.CreateProjectInstance();
            if (projectInstance.Build("Build", projectCollection.Loggers))
            {
                Console.WriteLine("Build succeeded");
            }
            else
            {
                Console.WriteLine("Build failed");
                return;
            }

            projectCollection.Dispose();

            Console.WriteLine();
            Console.WriteLine("Log lines:");
            Console.WriteLine(string.Join(Environment.NewLine, logLines));
            Console.WriteLine();

            if (logLines.Select(line => line.Substring(line.IndexOf(":") + 1)).Distinct().Count() == 1)
            {
                Console.WriteLine("Toolsets were the same");
            }
            else
            {
                Console.WriteLine("Toolsets were not the same!");
            }
        }

        private static string FindOnPath(string file)
        {
            return (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                .Split(new[] { Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries)
                .Where(Directory.Exists)
                .Select(i => Path.Combine(i, file))
                .Where(File.Exists)
                .FirstOrDefault();
        }
    }
}
