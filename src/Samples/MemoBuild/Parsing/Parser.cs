// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Build.Execution;
using Microsoft.Build.Experimental.ProjectCache;
using Microsoft.Build.Graph;
using Microsoft.Build.Prediction;

namespace MemoBuild.Parsing
{
    internal sealed class Parser
    {
        private static readonly char[] InvalidPathChars = Path.GetInvalidPathChars();

        private readonly PluginLoggerBase _logger;
        private readonly string _repoRoot;
        private readonly IReadOnlyDictionary<string, byte[]> _fileHashes;

        public Parser(
            PluginLoggerBase logger,
            string repoRoot,
            IReadOnlyDictionary<string, byte[]> fileHashes)
        {
            _logger = logger;
            _repoRoot = repoRoot;
            _fileHashes = fileHashes;
        }

        public IReadOnlyDictionary<ProjectInstance, ParserInfo> Parse(ProjectGraph graph)
        {
            var repoPathTree = new PathTree();
            Stopwatch stopwatch = Stopwatch.StartNew();
            int parallelism = Math.Max(2, Environment.ProcessorCount * 3 / 4);
            Parallel.ForEach(_fileHashes, new ParallelOptions { MaxDegreeOfParallelism = parallelism }, pair => repoPathTree.AddFile(pair.Key));
            _logger.LogMessage($"PathTree filling of {_fileHashes.Count} entries took {stopwatch.ElapsedMilliseconds}ms at parallelism {parallelism}");

            var normalizedFileCache = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var parserInfoForProjects = new Dictionary<ProjectInstance, ParserInfo>();
            foreach (ProjectGraphNode node in graph.ProjectNodes)
            {
                ParserInfo parserInfo = new(node.ProjectInstance.FullPath, _repoRoot, repoPathTree, normalizedFileCache);
                parserInfoForProjects.Add(node.ProjectInstance, parserInfo);
            }

            stopwatch.Restart();
            var predictionExecutor = new ProjectGraphPredictionExecutor(ProjectPredictors.AllProjectGraphPredictors, ProjectPredictors.AllProjectPredictors);
            var collector = new DelegatingProjectPredictionCollector(_logger, parserInfoForProjects);
            predictionExecutor.PredictInputsAndOutputs(graph, collector);
            _logger.LogMessage($"Executed project prediction on {parserInfoForProjects.Count} build files in {stopwatch.Elapsed.TotalSeconds:F2}s.");

            return parserInfoForProjects;
        }

        private sealed class DelegatingProjectPredictionCollector : IProjectPredictionCollector
        {
            private readonly PluginLoggerBase _logger;

            private readonly Dictionary<ProjectInstance, ParserInfo> _collectorByProjectInstance;

            public DelegatingProjectPredictionCollector(PluginLoggerBase logger, Dictionary<ProjectInstance, ParserInfo> collectorByProjectInstance)
            {
                _logger = logger;
                _collectorByProjectInstance = collectorByProjectInstance;
            }

            public void AddInputFile(string path, ProjectInstance projectInstance, string predictorName)
            {
                if (path.IndexOfAny(InvalidPathChars) != -1)
                {
                    _logger.LogMessage($"Ignoring input file with invalid path '{path}'. Predictor: {predictorName}. Project: {projectInstance.FullPath}");
                    return;
                }

                GetProjectCollector(projectInstance).AddInputFile(path, projectInstance, predictorName);
            }

            public void AddInputDirectory(string path, ProjectInstance projectInstance, string predictorName)
            {
                if (path.IndexOfAny(InvalidPathChars) != -1)
                {
                    _logger.LogMessage($"Ignoring input directory with invalid path '{path}'. Predictor: {predictorName}. Project: {projectInstance.FullPath}");
                    return;
                }

                GetProjectCollector(projectInstance).AddInputDirectory(path, projectInstance, predictorName);
            }

            public void AddOutputFile(string path, ProjectInstance projectInstance, string predictorName)
            {
                if (path.IndexOfAny(InvalidPathChars) != -1)
                {
                    _logger.LogMessage($"Ignoring output file with invalid path '{path}'. Predictor: {predictorName}. Project: {projectInstance.FullPath}");
                    return;
                }

                GetProjectCollector(projectInstance).AddOutputFile(path, projectInstance, predictorName);
            }

            public void AddOutputDirectory(string path, ProjectInstance projectInstance, string predictorName)
            {
                if (path.IndexOfAny(InvalidPathChars) != -1)
                {
                    _logger.LogMessage($"Ignoring output directory with invalid path '{path}'. Predictor: {predictorName}. Project: {projectInstance.FullPath}");
                    return;
                }

                GetProjectCollector(projectInstance).AddOutputDirectory(path, projectInstance, predictorName);
            }

            private ParserInfo GetProjectCollector(ProjectInstance projectInstance)
            {
                if (!_collectorByProjectInstance.TryGetValue(projectInstance, out ParserInfo collector))
                {
                    throw new InvalidOperationException("Prediction collected for ProjectInstance not in the ProjectGraph");
                }

                return collector;
            }
        }
    }
}
