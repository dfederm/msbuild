// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Cache.ContentStore.Hashing;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace MemoBuild.Hashing
{
    internal interface IOutputHasher : IAsyncDisposable
    {
        Task<ContentHash> ComputeHashAsync(string filePath, CancellationToken cancellationToken);
    }
}
