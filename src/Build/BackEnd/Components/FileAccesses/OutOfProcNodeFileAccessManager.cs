// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Threading;
using Microsoft.Build.BackEnd;
using Microsoft.Build.Framework.FileAccess;
using Microsoft.Build.Shared;

namespace Microsoft.Build.FileAccesses
{
    internal sealed class OutOfProcNodeFileAccessManager : IFileAccessManager, IBuildComponent
    {
        public static IBuildComponent CreateComponent(BuildComponentType type)
        {
            ErrorUtilities.VerifyThrowArgumentOutOfRange(type == BuildComponentType.FileAccessManager, nameof(type));
            return new OutOfProcNodeFileAccessManager();
        }

        public void InitializeComponent(IBuildComponentHost host)
        {
        }

        public void ShutdownComponent()
        {
        }

        public void ReportFileAccess(FileAccessData fileAccessData, int nodeId)
        {
            // TODO: Send the packet to the main node.
        }

        public void ReportProcess(ProcessData processData, int nodeId)
        {
            // TODO: Send the packet to the main node.
        }

        public FileAccessManager.HandlerRegistration RegisterHandlers(Action<BuildRequest, FileAccessData> fileAccessHandler, Action<BuildRequest, ProcessData> processHandler)
        {
            // This method should not be called in OOP nodes
            throw new NotImplementedException();
        }

        public void WaitForFileAccessReportCompletion(int globalRequestId, CancellationToken cancellationToken)
        {
            // This method should not be called in OOP nodes
            throw new NotImplementedException();
        }
    }
}
