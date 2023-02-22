// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace Microsoft.Build.FileAccesses
{
    /// <summary>
    /// Process data.
    /// </summary>
    /// <param name="PipId">Pip id.</param>
    /// <param name="PipDescription">Pip description.</param>
    /// <param name="ProcessName">Process name.</param>
    /// <param name="ProcessId">Process id.</param>
    /// <param name="ParentProcessId">Parent process id.</param>
    /// <param name="CreationDateTime">Creation date time.</param>
    /// <param name="ExitDateTime">Exit date time.</param>
    /// <param name="KernelTime">Kernel time.</param>
    /// <param name="UserTime">User time.</param>
    /// <param name="ExitCode">Exit code.</param>
    /// <param name="IoCounters">IO counters.</param>
    public readonly record struct ProcessData(
        long PipId,
        string PipDescription,
        string ProcessName,

        // TODO dshepelev: Fix suppression.
#pragma warning disable CS3001, CS3003 // Argument type is not CLS-compliant; Type is not CLS-compliant.
        uint ProcessId,
        uint ParentProcessId,
#pragma warning restore CS3001, CS3003 // Argument type is not CLS-compliant; Type is not CLS-compliant.
        DateTime CreationDateTime,
        DateTime ExitDateTime,
        TimeSpan KernelTime,
        TimeSpan UserTime,

        // TODO dshepelev: Fix suppression.
#pragma warning disable CS3001, CS3003 // Argument type is not CLS-compliant; Type is not CLS-compliant.
        uint ExitCode,
#pragma warning restore CS3001, CS3003 // Argument type is not CLS-compliant; Type is not CLS-compliant.
        IOCounters IoCounters);
}
