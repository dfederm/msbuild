// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Build.Execution;

// TODO dfederm: Don't directly use BXL's since this will end up being exposed to project cache plugin implementations.
using static BuildXL.Processes.IDetoursEventListener;

namespace Microsoft.Build.Experimental.ProjectCache
{
    /// <summary>
    ///     Only one plugin instance can exist for a given BuildManager BeginBuild / EndBuild session.
    ///     Any exceptions thrown by the plugin will cause MSBuild to fail the build.
    /// </summary>
    public abstract class ProjectCachePluginBase
    {
        /// <summary>
        ///     Called once before the build, to have the plugin instantiate its state.
        ///     Errors are checked via <see cref="PluginLoggerBase.HasLoggedErrors" />.
        /// </summary>
        public abstract Task BeginBuildAsync(
            CacheContext context,
            PluginLoggerBase logger,
            CancellationToken cancellationToken);

        /// <summary>
        ///     Called once for each build request.
        ///     Operation needs to be atomic. Any side effects (IO, environment variables, etc) need to be reverted upon
        ///     cancellation.
        ///     MSBuild may choose to cancel this method and build the project itself.
        ///     Errors are checked via <see cref="PluginLoggerBase.HasLoggedErrors" />.
        /// </summary>
        public abstract Task<CacheResult> GetCacheResultAsync(
            BuildRequestData buildRequest,
            PluginLoggerBase logger,
            CancellationToken cancellationToken);

        /// <summary>
        ///     Called once after all the build to let the plugin do any post build operations (log metrics, cleanup, etc).
        ///     Errors are checked via <see cref="PluginLoggerBase.HasLoggedErrors" />.
        /// </summary>
        public abstract Task EndBuildAsync(PluginLoggerBase logger, CancellationToken cancellationToken);

        /// <summary>
        ///     Called for each file access from an MSBuild node or one of its children.
        /// </summary>
#pragma warning disable CS3001 // Argument type is not CLS-compliant.
        // TODO dfederm: Fix suppression
        public virtual void HandleFileAccess(FileAccessData fileAccessData, BuildRequestData buildRequest)
#pragma warning restore CS3001 // Argument type is not CLS-compliant
        {
        }

        /// <summary>
        ///     Called for each new child process created by an MSBuild node or one of its children.
        /// </summary>
#pragma warning disable CS3001 // Argument type is not CLS-compliant.
        // TODO dfederm: Fix suppression
        public virtual void HandleProcess(ProcessData processData, BuildRequestData buildRequest)
#pragma warning restore CS3001 // Argument type is not CLS-compliant
        {
        }

        /// <summary>
        ///     Called when a build request finishes execution. This provides an opportunity for the plugin to take action on the
        ///     aggregated file access reports from <see cref="HandleFileAccess(FileAccessData, BuildRequestData)"/>.
        ///     Errors are checked via <see cref="PluginLoggerBase.HasLoggedErrors" />.
        /// </summary>
        public virtual Task HandleProjectFinishedAsync(
            BuildRequestData buildRequest,
            BuildResult buildResult,
            PluginLoggerBase logger,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
