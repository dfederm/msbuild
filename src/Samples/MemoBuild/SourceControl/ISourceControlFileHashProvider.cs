// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MemoBuild.SourceControl
{
    /// <summary>
    /// The contract for getting the hashes of files under source control.
    /// </summary>
    internal interface ISourceControlFileHashProvider
    {
        /// <summary>
        /// Get files under source control and their hash values.
        /// </summary>
        /// <param name="repoRoot">The repository root</param>
        /// <returns>All files within repository root with their hash values. The file paths are relative to the repository root.</returns>
        Task<IReadOnlyDictionary<string, byte[]>> GetFileHashesAsync(string repoRoot, CancellationToken cancellationToken);
    }
}
