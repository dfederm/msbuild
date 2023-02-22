// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Build.FileAccesses
{
    /// <summary>
    /// Contains I/O accounting information for a process or process tree.
    /// </summary>
    /// <param name="ReadCounters">Counters for read operations.</param>
    /// <param name="WriteCounters">Counters for write operations.</param>
    /// <param name="OtherCounters">Counters for other operations (not classified as either read or write).</param>
    /// <remarks>
    /// For job object, this structure contains I/O accounting information for a process or a job object.
    /// These counters include all operations performed by all processes ever associated with the job.
    /// </remarks>
    public readonly record struct IOCounters(IOTypeCounters ReadCounters, IOTypeCounters WriteCounters, IOTypeCounters OtherCounters);

    /// <summary>
    /// Contains I/O accounting information for a process or process tree for a particular type of IO (e.g. read or write).
    /// </summary>
    /// <param name="OperationCount">Number of operations performed (independent of size).</param>
    /// <param name="TransferCount">Total bytes transferred (regardless of the number of operations used to transfer them).</param>
    /// <remarks>
    /// For job object, this structure contains I/O accounting information for a process or a job object, for a particular type of IO (e.g.read or write).
    /// These counters include all operations performed by all processes ever associated with the job.
    /// </remarks>
    // TODO dshepelev: Fix suppression.
#pragma warning disable CS3001, CS3003 // Argument type is not CLS-compliant; Type is not CLS-compliant.
    public readonly record struct IOTypeCounters(ulong OperationCount, ulong TransferCount);
#pragma warning restore CS3001, CS3003 // Argument type is not CLS-compliant; Type is not CLS-compliant.
}
