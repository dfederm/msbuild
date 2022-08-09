// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using Microsoft.Build.Execution;

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
            Action<FileAccessData, BuildRequestData> fileAccessHandler,
            Action<ProcessData, BuildRequestData> processHandler);
    }
}
