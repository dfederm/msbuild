// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.MemoizationStore.Interfaces.Caches;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;

namespace MemoBuild.Caching
{
    internal sealed class GithubActionsCache : ICache
    {
        public Guid Id => throw new NotImplementedException();

        public bool StartupCompleted => throw new NotImplementedException();

        public bool StartupStarted => throw new NotImplementedException();

        public bool ShutdownCompleted => throw new NotImplementedException();

        public bool ShutdownStarted => throw new NotImplementedException();

        public CreateSessionResult<IReadOnlyCacheSession> CreateReadOnlySession(Context context, string name, ImplicitPin implicitPin) => throw new NotImplementedException();
        public CreateSessionResult<ICacheSession> CreateSession(Context context, string name, ImplicitPin implicitPin) => throw new NotImplementedException();
        public void Dispose() => throw new NotImplementedException();
        public IAsyncEnumerable<StructResult<StrongFingerprint>> EnumerateStrongFingerprints(Context context) => throw new NotImplementedException();

        public Task<GetStatsResult> GetStatsAsync(Context context)
        {
            return Task.FromResult(new GetStatsResult(new NotImplementedException()));
        }
        
        public Task<BoolResult> ShutdownAsync(Context context)
        {
            return BoolResult.SuccessTask;
        }
        public Task<BoolResult> StartupAsync(Context context) => throw new NotImplementedException();
    }
}
