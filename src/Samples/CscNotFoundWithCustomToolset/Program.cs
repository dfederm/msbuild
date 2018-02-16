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

            projectCollection.SetGlobalProperty("RoslynTargetsPath", Path.Combine(msbuildToolsPath, "Roslyn"));

            var consoleLogger = new ConsoleLogger(LoggerVerbosity.Minimal);
            projectCollection.RegisterLogger(consoleLogger);

            var fileLogger = new FileLogger();
            fileLogger.Parameters = $"LOGFILE={Directory.GetCurrentDirectory()}\\msbuild.log";
            fileLogger.Verbosity = LoggerVerbosity.Diagnostic;
            projectCollection.RegisterLogger(fileLogger);

            var project = projectCollection.LoadProject("Test.csproj");
            var projectInstance = project.CreateProjectInstance();
            if (projectInstance.Build("Compile", projectCollection.Loggers))
            {
                Console.WriteLine("Compile succeeded");
            }
            else
            {
                Console.WriteLine("Compile failed");
                return;
            }

            projectCollection.Dispose();
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
