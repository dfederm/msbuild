// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.Build.BackEnd;
using Microsoft.Build.Shared;

// TODO dfederm: Don't directly use BXL's since this will end up being exposed to project cache plugin implementations.
using static BuildXL.Processes.IDetoursEventListener;

namespace Microsoft.Build.FileAccesses
{
    internal sealed class FileAccessManager : IFileAccessManager, IBuildComponent
    {
        private IScheduler? _scheduler;
        private IConfigCache? _configCache;

        private readonly ReaderWriterLockSlim _handlersLock = new();
        private List<Action<BuildRequest, FileAccessData>> _fileAccessHandlers = new();
        private List<Action<BuildRequest, ProcessData>> _processHandlers = new();

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
        }

        public void ReportFileAccess(FileAccessData fileAccessData, int nodeId)
        {
            BuildRequest? buildRequest = GetBuildRequest(nodeId);
            if (buildRequest != null)
            {
                _handlersLock.EnterReadLock();
                try
                {
                    foreach (Action<BuildRequest, FileAccessData> handler in _fileAccessHandlers)
                    {
                        handler.Invoke(buildRequest, fileAccessData);
                    }
                }
                finally
                {
                    _handlersLock.ExitReadLock();
                }
            }
        }

        public void ReportProcess(ProcessData processData, int nodeId)
        {
            BuildRequest? buildRequest = GetBuildRequest(nodeId);
            if (buildRequest != null)
            {
                _handlersLock.EnterReadLock();
                try
                {
                    foreach (Action<BuildRequest, ProcessData> handler in _processHandlers)
                    {
                        handler.Invoke(buildRequest, processData);
                    }
                }
                finally
                {
                    _handlersLock.ExitReadLock();
                }
            }
        }

        public HandlerRegistration RegisterHandlers(Action<BuildRequest, FileAccessData> fileAccessHandler, Action<BuildRequest, ProcessData> processHandler)
        {
            _handlersLock.EnterWriteLock();
            try
            {
                _fileAccessHandlers.Add(fileAccessHandler);
                _processHandlers.Add(processHandler);
            }
            finally
            {
                _handlersLock.ExitWriteLock();
            }

            return new HandlerRegistration(() => UnregisterHandlers(fileAccessHandler, processHandler));
        }

        private void UnregisterHandlers(Action<BuildRequest, FileAccessData> fileAccessHandler, Action<BuildRequest, ProcessData> processHandler)
        {
            _handlersLock.EnterWriteLock();
            try
            {
                _fileAccessHandlers.Remove(fileAccessHandler);
                _processHandlers.Remove(processHandler);
            }
            finally
            {
                _handlersLock.ExitWriteLock();
            }
        }

        private BuildRequest? GetBuildRequest(int nodeId)
        {
            ErrorUtilities.VerifyThrow(
                _scheduler != null && _configCache != null,
                "Component has not been initialized");

            // Note: If the node isn't executing anything it may be accessing binaries required to run, eg. the MSBuild binaries
            return _scheduler!.GetExecutingRequestByNode(nodeId);
        }

        internal readonly struct HandlerRegistration : IDisposable
        {
            private readonly Action _unregisterAction;

            public HandlerRegistration(Action unregisterAction)
            {
                _unregisterAction = unregisterAction;
            }

            public void Dispose()
            {
                _unregisterAction();
            }
        }
    }
}
