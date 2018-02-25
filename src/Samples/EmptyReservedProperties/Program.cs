using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Framework;
using Microsoft.Build.Logging;

namespace EmptyReservedProperties
{
    class Program
    {
        static void Main(string[] args)
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

            var projectFile = Path.GetFullPath("Test.csproj");
            var projectDocument = XDocument.Load(projectFile);
            using (var projectReader = projectDocument.CreateReader())
            {
                try
                {
                    projectCollection.LoadProject(projectReader);
                    Console.WriteLine("Project loaded");
                }
                catch (Exception e)
                {
                    Console.WriteLine("Exception!!!");
                    Console.WriteLine(e);
                }
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
