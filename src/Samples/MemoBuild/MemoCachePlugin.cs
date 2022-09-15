// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Utilities.Collections;
using MemoBuild.Caching;
using MemoBuild.FileAccess;
using MemoBuild.Fingerprinting;
using MemoBuild.Hashing;
using MemoBuild.Parsing;
using MemoBuild.SourceControl;
using Microsoft.Build.Execution;
using Microsoft.Build.Experimental.ProjectCache;
using Microsoft.Build.Graph;
using static BuildXL.Processes.IDetoursEventListener;

namespace MemoBuild
{
    public sealed class MemoCachePlugin : ProjectCachePluginBase
    {
        // Note: This is not in PluginSettings as that's configured through item metadata and thus makes it into MSBuild logs. This is a secret so that's not desirable.
        //private const string ConnectionStringEnvVar = "MEMOBUILD_CONNECTIONSTRING";

        private static readonly string PluginAssemblyDirectory = Path.GetDirectoryName(typeof(MemoCachePlugin).Assembly.Location);

        // Outer builds do not have GetTargetPath defined. Use GetTargetFrameworks as a good enough proxy because:
        // - on cmdline builds, outer builds only get called with Build when they are root projects, so it's safe to not have results
        // for the Build target (no other projects depend on them)
        // - on VS builds, projects do not build their references (they have BuildProjectReferences=false), so its safe again to not have
        // results for the Build target
        // Null value for proxy target means that MSBuild will build the proxy target but not assign its result to any other target.
        private static readonly ProxyTargets OuterBuildProxyTargets = new ProxyTargets(new Dictionary<string, string> { { "GetTargetFrameworks", null! } });

        // Inner builds and any other projects (TF agnostic projects) should always have GetTargetPath defined.
        private static readonly ProxyTargets InnerBuildProxyTargets = new ProxyTargets(new Dictionary<string, string> { { "GetTargetPath", "Build" } });

        private string? _repoRoot;
        private IContentHasher? _contentHasher;
        private IInputHasher? _inputHasher;
        private IOutputHasher? _outputHasher;
        private IReadOnlyDictionary<ProjectInstance, NodeContext>? _nodeContexts;
        private IFingerprintFactory? _fingerprintFactory;
        private IFileAccessRepository? _fileAccessRepository;
        private ICacheClient? _cacheClient;

        static MemoCachePlugin() =>
            AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
            {
                var assemblyName = new AssemblyName(args.Name);
                string candidateAssemblyPath = Path.Combine(PluginAssemblyDirectory, $"{assemblyName.Name}.dll");
                return File.Exists(candidateAssemblyPath)
                    ? Assembly.LoadFrom(candidateAssemblyPath)
                    : null;
            };

        public async override Task BeginBuildAsync(CacheContext context, PluginLoggerBase logger, CancellationToken cancellationToken)
        {
            _repoRoot = GetRepoRoot(context, logger);
            if (_repoRoot == null)
            {
                return;
            }

            //string connectionString = Environment.GetEnvironmentVariable(ConnectionStringEnvVar);
            //if (string.IsNullOrEmpty(connectionString))
            //{
            //    throw new ApplicationException($"Required environment variable '{ConnectionStringEnvVar}' not set");
            //}

            PluginSettings pluginSettings = PluginSettings.Create(context.PluginSettings, logger);

            string logDirectory = GetLogDirectory(pluginSettings);

            _contentHasher = HashInfoLookup.Find(pluginSettings.HashType).CreateContentHasher();

            IReadOnlyDictionary<string, byte[]> fileHashes = await GetSourceControlFileHashesAsync(logger, cancellationToken);

            _inputHasher = new InputHasher(_contentHasher, fileHashes);
            _outputHasher = new OutputHasher(_contentHasher);

            ProjectGraph graph = GetProjectGraph(context, logger);
            Parser parser = new(logger, _repoRoot, fileHashes);
            IReadOnlyDictionary<ProjectInstance, ParserInfo> parserInfoForProjects = parser.Parse(graph);

            var nodeContexts = new Dictionary<ProjectInstance, NodeContext>(graph.ProjectNodes.Count);
            foreach (ProjectGraphNode node in graph.ProjectNodes)
            {
                ProjectInstance projectInstance = node.ProjectInstance;
                if (!parserInfoForProjects.TryGetValue(projectInstance, out ParserInfo parserInfo))
                {
                    throw new ApplicationException($"Missing parser info for {projectInstance.FullPath}");
                }

                nodeContexts.Add(projectInstance, new NodeContext(node, parserInfo));
            }

            _nodeContexts = nodeContexts;
            _fingerprintFactory = new FingerprintFactory(logDirectory, _contentHasher, _inputHasher, _nodeContexts);
            _fileAccessRepository = new FileAccessRepository(logDirectory);
            _cacheClient = await CacheClient.CreateAsync(logger, _fingerprintFactory, pluginSettings.HashType, pluginSettings.CacheUniverse);
        }

        public override async Task EndBuildAsync(PluginLoggerBase logger, CancellationToken cancellationToken)
        {
            _contentHasher?.Dispose();

            if (_outputHasher != null)
            {
                await _outputHasher.DisposeAsync();
            }

            if (_cacheClient != null)
            {
                await _cacheClient.DisposeAsync();
            }
        }

        public override async Task<CacheResult> GetCacheResultAsync(BuildRequestData buildRequest, PluginLoggerBase logger, CancellationToken cancellationToken)
        {
            if (_nodeContexts == null || _fingerprintFactory == null || _cacheClient == null)
            {
                // BeginBuild didn't finish successfully. It's expected to log sufficiently, so just bail.
                return CacheResult.IndicateNonCacheHit(CacheResultType.CacheNotApplicable);
            }

            ProjectInstance projectInstance = buildRequest.ProjectInstance;
            if (projectInstance == null)
            {
                logger.LogWarning($"Project instance was unexpectedly null for build request for project {buildRequest.ProjectFullPath}");
                return CacheResult.IndicateNonCacheHit(CacheResultType.CacheNotApplicable);
            }

            // Only projects called with the all default targets (usually just "Build") are cacheable. Other targets invocations (eg "GetTargetFrameworks") are
            // generally just querying properties and items from projects and not expensive nor output-producing.
            static bool AreTargetsCacheable(
                ICollection<string> targets,
                List<string> defaultTargets,
                string projectFullPath,
                PluginLoggerBase logger)
            {
                // By definition a request with no targets uses the default targets.
                if (targets.Count == 0)
                {
                    return true;
                }

                var targetNamesSet = new HashSet<string>(targets, StringComparer.OrdinalIgnoreCase);
                foreach (string target in defaultTargets)
                {
                    if (!targetNamesSet.Contains(target))
                    {
                        logger.LogMessage($"Targets not cacheable for project: {projectFullPath}. Missing default target '{target}' from targets list: {string.Join(";", targets)}");
                        return false;
                    }
                }

                return true;
            }

            if (!AreTargetsCacheable(
                buildRequest.TargetNames,
                projectInstance.DefaultTargets,
                buildRequest.ProjectFullPath,
                logger))
            {
                return CacheResult.IndicateNonCacheHit(CacheResultType.CacheNotApplicable);
            }

            if (!_nodeContexts.TryGetValue(projectInstance, out NodeContext nodeContext))
            {
                return CacheResult.IndicateNonCacheHit(CacheResultType.CacheNotApplicable);
            }

            NodeBuildResult? nodeBuildResult = await _cacheClient.GetNodeAsync(logger, nodeContext, cancellationToken);
            if (nodeBuildResult is null)
            {
                return CacheResult.IndicateNonCacheHit(CacheResultType.CacheMiss);
            }

            // Place all the files on disk
            List<Task> contentPlacementTasks = new(nodeBuildResult.Outputs.Count);
            foreach (KeyValuePair<string, ContentHash> kvp in nodeBuildResult.Outputs)
            {
                ContentHash contentHash = kvp.Value;
                string filePath = Path.Combine(_repoRoot, kvp.Key);
                contentPlacementTasks.Add(_cacheClient.GetContentAsync(logger, contentHash, filePath, cancellationToken));
            }

            await Task.WhenAll(contentPlacementTasks);

            // TODO dfederm: What about failures?
            nodeContext.AddBuildResult(nodeBuildResult);

            // This follows the logic of MSBuild's ProjectInterpretation.GetProjectType.
            // See: https://github.com/microsoft/msbuild/blob/master/src/Build/Graph/ProjectInterpretation.cs
            bool isInnerBuild = !string.IsNullOrWhiteSpace(projectInstance.GetPropertyValue(projectInstance.GetPropertyValue("InnerBuildProperty")));
            bool isOuterBuild = !isInnerBuild && !string.IsNullOrWhiteSpace(projectInstance.GetPropertyValue(projectInstance.GetPropertyValue("InnerBuildPropertyValues")));

            return CacheResult.IndicateCacheHit(isOuterBuild ? OuterBuildProxyTargets : InnerBuildProxyTargets);
        }

        public override void HandleFileAccess(FileAccessData fileAccessData, BuildRequestData buildRequest)
            => _fileAccessRepository!.AddFileAccess(buildRequest.ProjectInstance, fileAccessData);

        public override void HandleProcess(ProcessData processData, BuildRequestData buildRequest)
            => _fileAccessRepository!.AddProcess(buildRequest.ProjectInstance, processData);

        public override async Task HandleProjectFinishedAsync(BuildRequestData buildRequest, BuildResult buildResult, PluginLoggerBase logger, CancellationToken cancellationToken)
        {
            if (_fileAccessRepository is null
                || _fingerprintFactory is null
                || _cacheClient is null
                || _nodeContexts is null
                || !_nodeContexts.TryGetValue(buildRequest.ProjectInstance, out NodeContext nodeContext))
            {
                return;
            }

            // TODO dfederm: Handle this better?
            if (nodeContext.BuildResult != null)
            {
                // The node came from cache. Don't process it as if it produced outputs.
                return;
            }

            Dictionary<string, string> normalizedFilesRead = new(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, string> normalizedOutputPaths = new(StringComparer.OrdinalIgnoreCase);
            
            {
                FileAccesses fileAccesses = _fileAccessRepository.FinishProject(buildRequest.ProjectInstance);
                foreach (string input in fileAccesses.Inputs)
                {
                    string? normalizedFilePath = PathHelper.MakePathRelative(input, _repoRoot!);
                    if (normalizedFilePath != null)
                    {
                        normalizedFilesRead.Add(input, normalizedFilePath);
                    }
                }

                foreach (string output in fileAccesses.Outputs)
                {
                    string? normalizedFilePath = PathHelper.MakePathRelative(output, _repoRoot!);
                    if (normalizedFilePath != null)
                    {
                        normalizedOutputPaths.Add(output, normalizedFilePath);
                    }
                    else
                    {
                        logger.LogMessage($"Ignoring output outside of the repo root: {output}");
                    }
                }
            }

            // TODO dfederm: Detect reads of non dependencies
            // TODO dfederm: Duplicate binplace detection

            ConcurrentDictionary<string, ContentHash> outputs = new(StringComparer.OrdinalIgnoreCase);
            var outputProcessingTasks = new Task[normalizedOutputPaths.Count];
            int i = 0;
            foreach (var output in normalizedOutputPaths.Keys)
            {
                outputProcessingTasks[i++] = Task.Run(
                    async () =>
                    {
                        string normalizedOutputPath = normalizedOutputPaths[output];
                        ContentHash hash = await _outputHasher!.ComputeHashAsync(output, cancellationToken);
                        await _cacheClient!.AddContentAsync(logger, hash, output, cancellationToken);
                        outputs.TryAdd(normalizedOutputPath, hash);
                    },
                    cancellationToken);
            }

            await Task.WhenAll(outputProcessingTasks);

            PathSet? pathSet = _fingerprintFactory.GetPathSet(nodeContext, normalizedFilesRead.Keys);
            NodeBuildResult nodeBuildResult = new(outputs, creationTimeUtc: DateTime.UtcNow);

            // TODO dfederm: Handle CHL races
            await _cacheClient.AddNodeAsync(logger, nodeContext, pathSet, nodeBuildResult, cancellationToken);

            nodeContext.AddBuildResult(nodeBuildResult);

            // TODO dfederm: Allow add failures to be just warnings?
        }

        private string? GetRepoRoot(CacheContext context, PluginLoggerBase logger)
        {
            IEnumerable<string> projectFilePaths = context.Graph != null
                ? context.Graph.EntryPointNodes.Select(node => node.ProjectInstance.FullPath)
                : context.GraphEntryPoints != null
                    ? context.GraphEntryPoints.Select(entryPoint => entryPoint.ProjectFile)
                    : throw new ApplicationException($"{nameof(CacheContext)} did not contain a {nameof(context.Graph)} or {nameof(context.GraphEntryPoints)}");

            HashSet<string> repoRoots = new(StringComparer.OrdinalIgnoreCase);
            foreach (string projectFilePath in projectFilePaths)
            {
                string? repoRoot = GetRepoRootInternal(Path.GetDirectoryName(projectFilePath));

                // Tolerate projects which aren't under any git repo.
                if (repoRoot != null)
                {
                    repoRoots.Add(repoRoot);
                }
            }

            if (repoRoots.Count == 0)
            {
                logger.LogWarning("No projects are under git source control. Disabling the cache.");
                return null;
            }

            if (repoRoots.Count == 1)
            {
                string repoRoot = repoRoots.First();
                logger.LogMessage($"Repo root: {repoRoot}");
                return repoRoot;
            }

            logger.LogWarning($"Graph contains projects from multiple git repositories. Disabling the cache. Repo roots: {string.Join(", ", repoRoots)}");
            return null;

            static string? GetRepoRootInternal(string path)
            {
                string gitDir = Path.Combine(path, ".git");
                if (Directory.Exists(gitDir))
                {
                    return path;
                }

                string parentDir = Path.GetDirectoryName(path);
                return parentDir != null ? GetRepoRootInternal(parentDir) : null;
            }
        }

        private string GetLogDirectory(PluginSettings pluginSettings)
        {
            string logDirectory = Path.Combine(_repoRoot, pluginSettings.LogDirectory);
            if (Directory.Exists(logDirectory))
            {
                Directory.Delete(logDirectory, recursive: true);
            }

            Directory.CreateDirectory(logDirectory);

            return logDirectory;
        }

        private async Task<IReadOnlyDictionary<string, byte[]>> GetSourceControlFileHashesAsync(PluginLoggerBase logger, CancellationToken cancellationToken)
        {
            if (_repoRoot == null)
            {
                throw new ApplicationException($"{nameof(_repoRoot)} was unexpectedly null");
            }

            logger.LogMessage("Source Control: Getting hashes");
            Stopwatch stopwatch = Stopwatch.StartNew();

            GitFileHashProvider hashProvider = new(logger);
            IReadOnlyDictionary<string, byte[]> fileHashes = await hashProvider.GetFileHashesAsync(_repoRoot, cancellationToken);
            logger.LogMessage($"Source Control: File hashes query took {stopwatch.ElapsedMilliseconds} ms");

            return fileHashes;
        }

        private ProjectGraph GetProjectGraph(CacheContext context, PluginLoggerBase logger)
        {
            if (context.Graph != null)
            {
                logger.LogMessage($"Project graph with {context.Graph.ProjectNodes.Count} nodes provided.");
                return context.Graph;
            }

            if (context.GraphEntryPoints != null)
            {
                logger.LogMessage($"Constructing project graph using {context.GraphEntryPoints.Count} entry points.");
                Stopwatch stopwatch = Stopwatch.StartNew();
                ProjectGraph graph = new(context.GraphEntryPoints);
                logger.LogMessage($"Constructed project graph with {graph.ProjectNodes.Count} nodes in {stopwatch.Elapsed.TotalSeconds:F2}s.");
                return graph;
            }

            throw new ApplicationException($"{nameof(CacheContext)} did not contain a {nameof(context.Graph)} or {nameof(context.GraphEntryPoints)}");
        }
    }
}
