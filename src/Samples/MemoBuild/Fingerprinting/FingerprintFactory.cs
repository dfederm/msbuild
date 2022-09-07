// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using MemoBuild.Hashing;
using MemoBuild.Parsing;
using Microsoft.Build.Execution;
using Microsoft.Build.Graph;

namespace MemoBuild.Fingerprinting
{
    internal sealed class FingerprintFactory : IFingerprintFactory
    {
        private readonly ConcurrentDictionary<string, byte[]> _stringHashCache = new(StringComparer.Ordinal);
        private readonly string _logDirectory;
        private readonly IContentHasher _contentHasher;
        private readonly IInputHasher _inputHasher;
        private readonly IReadOnlyDictionary<ProjectInstance, NodeContext> _nodeContexts;

        public FingerprintFactory(
            string logDirectory,
            IContentHasher contentHasher,
            IInputHasher inputHasher,
            IReadOnlyDictionary<ProjectInstance, NodeContext> nodeContexts)
        {
            _logDirectory = logDirectory;
            _contentHasher = contentHasher;
            _inputHasher = inputHasher;
            _nodeContexts = nodeContexts;
        }

        public byte[]? GetWeakFingerprint(NodeContext nodeContext)
        {
            List<byte[]?> hashes = new();

            // Add node information
            hashes.Add(GetStringHash(nodeContext.ParserInfo.NormalizedProjectFilePath));
            foreach (KeyValuePair<string, string> property in nodeContext.Node.ProjectInstance.GlobalProperties)
            {
                hashes.Add(GetStringHash($"{property.Key}={property.Value}"));
            }

            // Add predicted inputs
            SortAndAddInputFileHashes(hashes, nodeContext.ParserInfo.Inputs.Select(input => input.RelativePath));

            // Add dependency outputs
            foreach (ProjectGraphNode dependencyNode in nodeContext.Node.ProjectReferences)
            {
                if (!_nodeContexts.TryGetValue(dependencyNode.ProjectInstance, out NodeContext dependencyNodeContext))
                {
                    return null;
                }

                if (dependencyNodeContext.BuildResult?.Outputs == null)
                {
                    return null;
                }

                // Sort for consistent hash ordering
                foreach (KeyValuePair<string, ContentHash> dependencyOutput in dependencyNodeContext.BuildResult.Outputs.OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase))
                {
                    hashes.Add(dependencyOutput.Value.ToHashByteArray());
                }
            }

            return _contentHasher.CombineHashes(hashes)!;
        }

        public PathSet? GetPathSet(NodeContext nodeContext, IEnumerable<string> observedInputs)
        {
            List<string> pathSetIncludedInputs = new();
            List<string> pathSetExcludedInputs = new();

            HashSet<string> predictedInputsSet = new(StringComparer.OrdinalIgnoreCase);
            foreach (PredictedInput input in nodeContext.ParserInfo.Inputs)
            {
                predictedInputsSet.Add(input.RelativePath);
            }

            // As an optimization, only include non-predicted inputs. If a predicted input changes, the weak fingerprint
            // will not match and so the associated PathSets will never be used.
            foreach (string observedInput in observedInputs)
            {
                if (predictedInputsSet.Contains(observedInput))
                {
                    continue;
                }

                if (_inputHasher.ContainsPath(observedInput))
                {
                    pathSetIncludedInputs.Add(observedInput);
                }
                else
                {
                    pathSetExcludedInputs.Add(observedInput);
                }
            }

            // Sort the collections for consistent ordering
            pathSetIncludedInputs.Sort(StringComparer.OrdinalIgnoreCase);
            pathSetExcludedInputs.Sort(StringComparer.OrdinalIgnoreCase);

            // To help with debugging, dump the files which were included and excluded from the PathSet.
            string logDirectory = Path.Combine(_logDirectory, nodeContext.Id);
            Directory.CreateDirectory(logDirectory);
            File.WriteAllLines(Path.Combine(logDirectory, "pathSet_included.txt"), pathSetIncludedInputs);
            File.WriteAllLines(Path.Combine(logDirectory, "pathSet_excluded.txt"), pathSetExcludedInputs);

            // If the PathSet is effectively empty, return null instead.
            if (pathSetIncludedInputs.Count == 0)
            {
                return null;
            }

            return new PathSet
            {
                FilesRead = pathSetIncludedInputs,
            };
        }

        public byte[]? GetStrongFingerprint(PathSet? pathSet)
        {
            if (pathSet?.FilesRead == null || pathSet.FilesRead.Count == 0)
            {
                return null;
            }

            List<byte[]?> hashes = new();
            SortAndAddInputFileHashes(hashes, pathSet.FilesRead);

            if (hashes.Count == 0)
            {
                return null;
            }

            return _contentHasher.CombineHashes(hashes)!;
        }

        private void SortAndAddInputFileHashes(List<byte[]?> hashes, IEnumerable<string> files)
        {
            List<string> filteredfiles = new();
            foreach (string file in files)
            {
                if (_inputHasher.ContainsPath(file))
                {
                    filteredfiles.Add(file);
                }
            }

            if (filteredfiles.Count == 0)
            {
                return;
            }

            // Sort for consistent hash ordering
            filteredfiles.Sort(StringComparer.OrdinalIgnoreCase);

            foreach (string file in filteredfiles)
            {
                hashes.Add(_inputHasher.GetHash(file));
            }

        }

        private byte[] GetStringHash(string input)
            => _stringHashCache.GetOrAdd(input, str => _contentHasher.GetContentHash(Encoding.UTF8.GetBytes(str)).ToHashByteArray());
    }
}
