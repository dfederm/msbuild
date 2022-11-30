// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Threading;
using Microsoft.Build.BackEnd;

// TODO dfederm: Don't directly use BXL's since this will end up being exposed to project cache plugin implementations.
using static BuildXL.Processes.IDetoursEventListener;

namespace Microsoft.Build.FileAccesses
{
    internal interface IFileAccessManager
    {
        void ReportFileAccess(FileAccessData fileAccessData, int nodeId);

        void ReportProcess(ProcessData processData, int nodeId);

        // Note: HandlerRegistration is exposed directly instead of IDisposable to avoid boxing.
        FileAccessManager.HandlerRegistration RegisterHandlers(
            Action<BuildRequest, FileAccessData> fileAccessHandler,
            Action<BuildRequest, ProcessData> processHandler);

        void WaitForFileAccessReportCompletion(int globalRequestId, CancellationToken cancellationToken);
    }
}
