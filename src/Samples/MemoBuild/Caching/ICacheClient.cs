// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using MemoBuild.Fingerprinting;
using Microsoft.Build.Experimental.ProjectCache;

namespace MemoBuild.Caching
{
    internal interface ICacheClient : IAsyncDisposable
    {
        Task AddContentAsync(PluginLoggerBase logger, ContentHash contentHash, string filePath, CancellationToken cancellationToken);

        Task GetContentAsync(PluginLoggerBase logger, ContentHash contentHash, string filePath, CancellationToken cancellationToken);

        Task AddNodeAsync(PluginLoggerBase logger, NodeContext nodeContext, PathSet? pathSet, NodeBuildResult nodeBuildResult, CancellationToken cancellationToken);

        Task<NodeBuildResult?> GetNodeAsync(PluginLoggerBase logger, NodeContext nodeContext, CancellationToken cancellationToken);
    }
}
