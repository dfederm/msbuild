// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Build.Execution;
using Microsoft.Build.Experimental.ProjectCache;
using static BuildXL.Processes.IDetoursEventListener;

namespace MemoBuild
{
    public sealed class MemoCachePlugin : ProjectCachePluginBase
    {
        public override Task BeginBuildAsync(CacheContext context, PluginLoggerBase logger, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public override Task EndBuildAsync(PluginLoggerBase logger, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public override Task<CacheResult> GetCacheResultAsync(BuildRequestData buildRequest, PluginLoggerBase logger, CancellationToken cancellationToken)
        {
            return Task.FromResult(CacheResult.IndicateNonCacheHit(CacheResultType.CacheNotApplicable));
        }

        public override void HandleFileAccess(FileAccessData fileAccessData, BuildRequestData buildRequest)
        {
        }

        public override void HandleProcess(ProcessData processData, BuildRequestData buildRequest)
        {
        }

        public override Task HandleProjectFinishedAsync(BuildRequestData buildRequest, BuildResult buildResult, PluginLoggerBase logger, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
