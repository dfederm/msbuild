// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using Microsoft.Build.BackEnd;
using Microsoft.Build.Execution;
using Microsoft.Build.Shared;

// TODO dfederm: Don't directly use BXL's since this will end up being exposed to project cache plugin implementations.
using static BuildXL.Processes.IDetoursEventListener;

namespace Microsoft.Build.FileAccesses
{
    internal sealed class FileAccessManager : IFileAccessManager, IBuildComponent
    {
        private IScheduler? _scheduler;
        private IConfigCache? _configCache;

        // For simplicity, only one set of handlers is currently supported. If more are needed in the future, this can be made into a list.
        private Action<FileAccessData, BuildRequestData>? _fileAccessHandler;
        private Action<ProcessData, BuildRequestData>? _processHandler;

        public static IBuildComponent CreateComponent(BuildComponentType type)
        {
            ErrorUtilities.VerifyThrowArgumentOutOfRange(type == BuildComponentType.FileAccessManager, nameof(type));
            return new FileAccessManager();
        }

        public void InitializeComponent(IBuildComponentHost host)
        {
            _scheduler = host.GetComponent(BuildComponentType.Scheduler) as IScheduler;
            _configCache = host.GetComponent(BuildComponentType.ConfigCache) as IConfigCache;
        }

        public void ShutdownComponent()
        {
            _scheduler = null;
            _configCache = null;
            _fileAccessHandler = null;
            _processHandler = null;
        }

        public void ReportFileAccess(FileAccessData fileAccessData, int nodeId)
        {
            BuildRequestData? request = GetRequestData(nodeId);
            if (request != null)
            {
                _fileAccessHandler?.Invoke(fileAccessData, request);
            }
        }

        public void ReportProcess(ProcessData processData, int nodeId)
        {
            BuildRequestData? request = GetRequestData(nodeId);
            if (request != null)
            {
                _processHandler?.Invoke(processData, request);
            }
        }

        public HandlerRegistration RegisterHandlers(Action<FileAccessData, BuildRequestData> fileAccessHandler, Action<ProcessData, BuildRequestData> processHandler)
        {
            ErrorUtilities.VerifyThrow(
                _fileAccessHandler == null && _processHandler == null,
                "Handlers are already regsitered");

            _fileAccessHandler = fileAccessHandler;
            _processHandler = processHandler;
            return new HandlerRegistration(this);
        }

        private BuildRequestData? GetRequestData(int nodeId)
        {
            ErrorUtilities.VerifyThrow(
                _scheduler != null && _configCache != null,
                "Component has not been initialized");

            BuildRequest? buildRequest = _scheduler!.GetExecutingRequestByNode(nodeId);
            if (buildRequest == null)
            {
                // If the node isn't executing anything it may be accessing binaries required to run, eg. the MSBuild binaries
                return null;
            }

            // TODO dfederm: The BuildRequestData on the BuildSubmission would be better than creating a new object every time, but that's not easy to get currently.
            // TODO dfederm: ProjectCacheService currently ensures this is evaluated, but that seems like a brittle assumption.
            BuildRequestConfiguration configuration = _configCache![buildRequest.ConfigurationId];
            return new BuildRequestData(configuration.Project, buildRequest.Targets.ToArray());
        }

        internal struct HandlerRegistration : IDisposable
        {
            private readonly FileAccessManager _fileAccessManager;

            public HandlerRegistration(FileAccessManager fileAccessManager)
            {
                _fileAccessManager = fileAccessManager;
            }

            public void Dispose()
            {
                ErrorUtilities.VerifyThrow(
                    _fileAccessManager._fileAccessHandler != null && _fileAccessManager._processHandler != null,
                    "Handlers are already unregsitered");

                _fileAccessManager._fileAccessHandler = null;
                _fileAccessManager._processHandler = null;
            }
        }
    }
}
