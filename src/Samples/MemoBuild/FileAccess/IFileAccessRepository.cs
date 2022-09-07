// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.Build.Execution;
using static BuildXL.Processes.IDetoursEventListener;

namespace MemoBuild.FileAccess
{
    internal interface IFileAccessRepository
    {
        void AddFileAccess(ProjectInstance projectInstance, FileAccessData fileAccessData);

        void AddProcess(ProjectInstance projectInstance, ProcessData processData);

        FileAccesses FinishProject(ProjectInstance projectInstance);
    }
}
