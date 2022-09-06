// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using Microsoft.Build.Execution;
using Microsoft.Build.Prediction;

namespace MemoBuild.Parsing
{
    internal sealed class ParserInfo : IProjectPredictionCollector
    {
        private static char[] DirectorySeparatorChars = { Path.DirectorySeparatorChar };
        private readonly string _projectDirectory;
        private readonly string _repoRoot;
        private readonly PathTree _repoPathTree;
        private readonly ConcurrentDictionary<string, string> _normalizedFileCache;

        // A cache of paths (relative or absolute) to their absolute form. Used for normalization.
        // This is scoped per build file since the relative paths are different.
        // See the NormalizedFileCache for a parser-wide cache of absolute paths to their normalized forms.
        private readonly Dictionary<string, string> _absolutePathFileCache = new(StringComparer.OrdinalIgnoreCase);

        private readonly ConcurrentDictionary<string, PredictedInput> _inputs = new(StringComparer.OrdinalIgnoreCase);

        public ParserInfo(
            string projectFilePath,
            string repoRoot,
            PathTree repoPathTree,
            ConcurrentDictionary<string, string> normalizedFileCache)
        {
            _repoRoot = repoRoot;
            _repoPathTree = repoPathTree;
            _normalizedFileCache = normalizedFileCache;

            _projectDirectory = Path.GetDirectoryName(projectFilePath);
            NormalizedProjectFilePath = NormalizePath(projectFilePath)!;
        }

        public string NormalizedProjectFilePath { get; }

        public IEnumerable<PredictedInput> Inputs
        {
            get
            {
                foreach (KeyValuePair<string, PredictedInput> kvp in _inputs)
                {
                    yield return kvp.Value;
                }
            }
        }

        public void AddInputFile(string path, ProjectInstance projectInstance, string predictorName)
        {
            string? normalizedPath = NormalizePath(path);
            if (normalizedPath == null)
            {
                return;
            }

            AddInput(normalizedPath, predictorName);
        }

        public void AddInputDirectory(string path, ProjectInstance projectInstance, string predictorName)
        {
            string? normalizedDirPath = NormalizePath(path);
            if (normalizedDirPath == null)
            {
                return;
            }

            foreach (string normalizedFilePath in _repoPathTree.EnumerateFiles(normalizedDirPath, SearchOption.TopDirectoryOnly))
            {
                AddInput(normalizedFilePath, predictorName);
            }
        }

        public void AddOutputFile(string path, ProjectInstance projectInstance, string predictorName)
        {
            // No need to track these
        }

        public void AddOutputDirectory(string path, ProjectInstance projectInstance, string predictorName)
        {
            // No need to track these
        }

        private void AddInput(string normalizedPath, string predictorName)
        {
            PredictedInput input = _inputs.GetOrAdd(normalizedPath, static path => new PredictedInput(path));
            input.AddPredictorName(predictorName);
        }

        /// <summary>
        /// Normalizes an absolute file path into an repository-relative path, if the path is under the repository root. Otherwise, null is returned.
        /// </summary>
        /// <param name="path">An absolute or project-relative file path</param>
        /// <returns>The normalized file path.</returns>
        private string? NormalizePath(string path)
        {
            // Make the path absolute if needed.
            if (!_absolutePathFileCache.TryGetValue(path, out string absolutePath))
            {
                absolutePath = path;

                // If the path contains forward slash, normalize it with backward slash
                // Note that Replace returns the same string instance if there was no replacement, so there is no need for a Contains call to avoid allocating.
                absolutePath = absolutePath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);

                // Make the path absolute
                if (!Path.IsPathRooted(absolutePath))
                {
                    absolutePath = Path.Combine(_projectDirectory, absolutePath);
                }

                // Remove any \.\ or \..\ stuff
                absolutePath = Path.GetFullPath(absolutePath);

                // Always trim trailing slashes
                absolutePath = absolutePath.TrimEnd(DirectorySeparatorChars);

                _absolutePathFileCache.Add(path, absolutePath);
            }

            // Only consider files in the repository
            if (!absolutePath.StartsWith(_repoRoot))
            {
                return null;
            }

            // Normalize the path
            if (!_normalizedFileCache.TryGetValue(absolutePath, out string normalizedPath))
            {
                // Make repository-relative
                normalizedPath = absolutePath.Equals(_repoRoot, StringComparison.OrdinalIgnoreCase)
                    ? string.Empty
                    : absolutePath.Substring(_repoRoot.Length + 1);

                _normalizedFileCache.TryAdd(absolutePath, normalizedPath);
            }

            return normalizedPath;
        }
    }
}
