// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;
using Microsoft.Build.Execution;

namespace MemoBuild
{
    internal static class LoggingExtensions
    {
        public static string GetNodeId(this ProjectInstance projectInstance)
            => $"{Path.GetFileName(projectInstance.FullPath)}_{projectInstance.EvaluationId}";
    }
}
