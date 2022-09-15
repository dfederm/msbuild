// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Interfaces.Utils;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.MemoizationStore.Interfaces.Caches;
using BuildXL.Cache.MemoizationStore.Interfaces.Results;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using MemoBuild.Fingerprinting;
using Microsoft.Build.Experimental.ProjectCache;

namespace MemoBuild.Caching
{
    internal sealed class CacheClient : ICacheClient
    {
        private static readonly JsonSerializerOptions SerializerOptions = CreateJsonSerializerOptions();

        private readonly Tracer _tracer = new Tracer(nameof(CacheClient));

        private readonly Context _rootContext;

        private readonly HashType _hashType;

        private readonly Selector _emptySelector;

        private readonly ICache _cache;

        private readonly ICacheSession _cacheSession;

        private readonly IFingerprintFactory _fingerprintFactory;

        public CacheClient(
            Context rootContext,
            IFingerprintFactory fingerprintFactory,
            ICache cache,
            ICacheSession cacheSession,
            HashType hashType)
        {
            _rootContext = rootContext;
            _fingerprintFactory = fingerprintFactory;
            _cache = cache;
            _cacheSession = cacheSession;
            _hashType = hashType;
            _emptySelector = new(new ContentHash(hashType, new byte[33]), new byte[1]);
        }

        public static Task<ICacheClient> CreateAsync(
            PluginLoggerBase logger,
            IFingerprintFactory fingerprintFactory,
            HashType hashType,
            string cacheUniverse)
        {
            Context context = new(new CacheLoggerAdapter(logger));

            // var cacheConfig = new AzureBlobStorageCacheFactory.Configuration(
            //     Credentials: new AzureBlobStorageCredentials(connectionString),
            //     Universe: cacheUniverse,
            //     StorageInteractionTimeout: TimeSpan.FromHours(1),
            //     DownloadStrategyConfiguration: new BlobDownloadStrategyConfiguration(),
            //     MetadataPinElisionDuration: TimeSpan.FromDays(1));

            // ICache cache = AzureBlobStorageCacheFactory.Create(cacheConfig);

            // await cache.StartupAsync(context).ThrowIfFailure();

            // CreateSessionResult<ICacheSession> createSessionResult = cache
            //     .CreateSession(context, name: "Default", ImplicitPin.None)
            //     .ThrowIfFailure();

            // ICacheSession cacheSession = createSessionResult.Session!;

            // await cacheSession.StartupAsync(context).ThrowIfFailure();

            ICache cache = new GithubActionsCache();
            ICacheSession cacheSession = new GithubActionsCacheSession(cacheUniverse);
            ICacheClient client = new CacheClient(context, fingerprintFactory, cache, cacheSession, hashType);
            return Task.FromResult(client);
        }

        public async ValueTask DisposeAsync()
        {
            await _cacheSession.ShutdownAsync(_rootContext);

            GetStatsResult stats = await _cache.GetStatsAsync(_rootContext);
            if (stats.Succeeded)
            {
                foreach (KeyValuePair<string, long> stat in stats.CounterSet.ToDictionaryIntegral())
                {
                    _rootContext.Logger.Debug($"{stat.Key}={stat.Value}");
                }
            }

            await _cache.ShutdownAsync(_rootContext);
        }

        public async Task AddContentAsync(
            PluginLoggerBase logger,
            ContentHash contentHash,
            string filePath,
            CancellationToken cancellationToken)
        {
            Context context = new(new CacheLoggerAdapter(logger));

            // Use hardlinks for performance.
            const FileRealizationMode realizationMode = FileRealizationMode.HardLink;

            PutResult putResult = await _cacheSession.PutFileAsync(
                context,
                contentHash,
                new AbsolutePath(filePath),
                realizationMode,
                cancellationToken);

            if (!putResult.Succeeded)
            {
                throw new CacheException($"Add content failed for hash {contentHash.ToShortHash()} and path {filePath}: {putResult}");
            }
        }

        public async Task GetContentAsync(
            PluginLoggerBase logger,
            ContentHash contentHash,
            string filePath,
            CancellationToken cancellationToken)
        {
            Context context = new(new CacheLoggerAdapter(logger));

            // Use hardlinks for performance and ensure the files are unwriteable.
            const FileAccessMode accessMode = FileAccessMode.ReadOnly;
            const FileRealizationMode realizationMode = FileRealizationMode.HardLink;

            // The cache doesn't create the directory for us.
            // TODO dfederm: Optimize by deduping directory creation?
            Directory.CreateDirectory(Path.GetDirectoryName(filePath));

            PlaceFileResult placeResult = await _cacheSession.PlaceFileAsync(
                context,
                contentHash,
                new AbsolutePath(filePath),
                accessMode,
                FileReplacementMode.ReplaceExisting,
                realizationMode,
                cancellationToken);

            if (!placeResult.IsPlaced() && placeResult.Code != PlaceFileResult.ResultCode.NotPlacedAlreadyExists)
            {
                throw new CacheException($"Get content failed for hash {contentHash.ToShortHash()} and path {filePath}: {placeResult}");
            }
        }

        public async Task AddNodeAsync(
            PluginLoggerBase logger,
            NodeContext nodeContext,
            PathSet? pathSet,
            NodeBuildResult nodeBuildResult,
            CancellationToken cancellationToken)
        {
            Context context = new(new CacheLoggerAdapter(logger));

            // Add the metadata content.
            ContentHash? nodeBuildResultHash = await SerializeAndPutToCacheAsync(context, nodeBuildResult, cancellationToken);
            if (!nodeBuildResultHash.HasValue)
            {
                throw new CacheException($"Putting NodeBuildResult content failed for {nodeContext.Id}");
            }

            _tracer.Debug(context, $"Put metadata file {nodeBuildResultHash.Value.ToShortString()} to the cache for {nodeContext.Id}");

            var contentHashes = new ContentHash[nodeBuildResult.Outputs.Count + 1];

            // metadata blob is blob0
            contentHashes[0] = nodeBuildResultHash.Value;

            // Copy the rest of the content hashes
            int i = 1;
            foreach (KeyValuePair<string, ContentHash> output in nodeBuildResult.Outputs)
            {
                contentHashes[i] = output.Value;
            }

            var contentHashList = new ContentHashList(contentHashes);

            byte[]? weakFingerprintBytes = _fingerprintFactory.GetWeakFingerprint(nodeContext);
            if (weakFingerprintBytes is null)
            {
                throw new CacheException($"Weak fingerprint is null for {nodeContext.Id}");
            }

            Fingerprint weakFingerprint = new(weakFingerprintBytes);

            Selector selector;
            if (pathSet != null)
            {
                // Add the PathSet to the ContentStore
                ContentHash? pathSetHash = await SerializeAndPutToCacheAsync(context, pathSet, cancellationToken);
                if (!pathSetHash.HasValue)
                {
                    throw new CacheException($"Putting PathSet content failed for {nodeContext.Id}");
                }

                _tracer.Debug(context, $"Put PathSet {pathSetHash.Value.ToShortString()} to the cache for {nodeContext.Id}");

                byte[]? strongFingerprintBytes = _fingerprintFactory.GetStrongFingerprint(pathSet);
                selector = strongFingerprintBytes is null
                    ? _emptySelector
                    : new Selector(pathSetHash.Value, strongFingerprintBytes);
            }
            else
            {
                // If the PathSet is null that means all observed inputs were predicted or not hash-impacting.
                // This means the weak fingerprint is sufficient as a cache key and we can use the empty selector.
                _tracer.Debug(context, $"PathSet was null. Using empty selector for {nodeContext.Id}");
                selector = _emptySelector;
            }

            StrongFingerprint strongFingerprint = new(weakFingerprint, selector);

            AddOrGetContentHashListResult addResult = await _cacheSession.AddOrGetContentHashListAsync(
                context,
                strongFingerprint,
                new ContentHashListWithDeterminism(contentHashList, CacheDeterminism.None),
                cancellationToken);

            if (!addResult.Succeeded)
            {
                throw new CacheException($"{nameof(_cacheSession.AddOrGetContentHashListAsync)} failed for {nodeContext.Id}: {addResult}");
            }

            // TODO dfederm: Handle CHL races
        }

        public async Task<NodeBuildResult?> GetNodeAsync(
            PluginLoggerBase logger,
            NodeContext nodeContext,
            CancellationToken cancellationToken)
        {
            Context context = new(new CacheLoggerAdapter(logger));

            byte[]? weakFingerprintBytes = _fingerprintFactory.GetWeakFingerprint(nodeContext);
            if (weakFingerprintBytes == null)
            {
                _tracer.Debug(context, "Weak fingerprint is null");
                return null;
            }

            Fingerprint weakFingerprint = new(weakFingerprintBytes);

            Selector? selector = await GetMatchingSelectorAsync(context, weakFingerprint, cancellationToken);
            if (!selector.HasValue)
            {
                // GetMatchingSelectorAsync logs sufficiently
                return null;
            }

            StrongFingerprint strongFingerprint = new(weakFingerprint, selector.Value);

            GetContentHashListResult getContentHashListResult = await _cacheSession.GetContentHashListAsync(context, strongFingerprint, cancellationToken);
            if (!getContentHashListResult.Succeeded)
            {
                _tracer.Debug(context, $"{nameof(_cacheSession.GetContentHashListAsync)} failed for {strongFingerprint}: {getContentHashListResult}");
                return null;
            }

            ContentHashList? contentHashList = getContentHashListResult.ContentHashListWithDeterminism.ContentHashList;
            if (contentHashList is null)
            {
                _tracer.Debug(context, $"ContentHashList is null for {strongFingerprint}: {getContentHashListResult}");
                return null;
            }

            // The first file is special: it is a serialized NodeBuildResult file.
            ContentHash nodeBuildResultHash = contentHashList.Hashes[0];
            NodeBuildResult? nodeBuildResult = await FetchAndDeserializeFromCacheAsync<NodeBuildResult>(context, nodeBuildResultHash, cancellationToken);
            if (nodeBuildResult is null)
            {
                _tracer.Debug(context, $"Failed to fetch TargetData with content hash {nodeBuildResultHash} for {strongFingerprint}");
                return null;
            }

            return nodeBuildResult;
        }

        private async Task<Selector?> GetMatchingSelectorAsync(
            Context context,
            Fingerprint weakFingerprint,
            CancellationToken cancellationToken)
        {
            context = new(context);

            await foreach (GetSelectorResult getSelectorResult in _cacheSession.GetSelectors(context, weakFingerprint, cancellationToken))
            {
                if (!getSelectorResult.Succeeded)
                {
                    _tracer.Debug(context, $"{nameof(_cacheSession.GetSelectors)} failed for weak fingerprint {weakFingerprint}: {getSelectorResult}");
                    return null;
                }

                Selector selector = getSelectorResult.Selector;
                if (selector == _emptySelector)
                {
                    // Special-case for the empty selector, which always matches.
                    _tracer.Debug(context, $"Matched empty selector for weak fingerprint {weakFingerprint}");
                    return selector;
                }

                ContentHash pathSetHash = selector.ContentHash;
                byte[] selectorStrongFingerprint = selector.Output;

                PathSet? pathSet = await FetchAndDeserializeFromCacheAsync<PathSet>(context, pathSetHash, cancellationToken);

                if (pathSet is null)
                {
                    _tracer.Debug(context, $"Skipping selector. Failed to fetch PathSet with content hash {pathSetHash} for weak fingerprint {weakFingerprint}");
                    continue;
                }

                // Create a strong fingerprint from the PathSet and see if it matches the selector's strong fingerprint.
                byte[]? possibleStrongFingerprint = _fingerprintFactory.GetStrongFingerprint(pathSet);
                if (possibleStrongFingerprint != null && ByteArrayComparer.ArraysEqual(possibleStrongFingerprint, selectorStrongFingerprint))
                {
                    _tracer.Debug(context, $"Matched matching selector with PathSet hash {pathSetHash} for weak fingerprint {weakFingerprint}");
                    return selector;
                }
            }

            _tracer.Debug(context, $"No matching selectors for weak fingerprint {weakFingerprint}");
            return null;
        }

        private async Task<ContentHash?> SerializeAndPutToCacheAsync<T>(Context context, T data, CancellationToken cancellationToken)
            where T : class
        {
            using (var memoryStream = new MemoryStream())
            {
                await JsonSerializer.SerializeAsync(memoryStream, data, SerializerOptions, cancellationToken);

                // Rewind the stream
                memoryStream.Position = 0;

                PutResult putResult = await _cacheSession.PutStreamAsync(context, _hashType, memoryStream, cancellationToken);
                if (!putResult.Succeeded)
                {
                    _tracer.Debug(context, $"{nameof(_cacheSession.PutStreamAsync)} failed for content: {putResult}");
                    return null;
                }

                return putResult.ContentHash;
            }
        }

        private async Task<T?> FetchAndDeserializeFromCacheAsync<T>(Context context, ContentHash contentHash, CancellationToken cancellationToken)
            where T : class
        {
            context = new(context);

            OpenStreamResult streamResult = await _cacheSession.OpenStreamAsync(context, contentHash, cancellationToken);
            if (!streamResult.Succeeded)
            {
                _tracer.Debug(context, $"{nameof(_cacheSession.OpenStreamAsync)} failed for content {contentHash.ToShortHash()}: {streamResult}");
                return null;
            }

            Stream stream = streamResult.Stream;

            T? data = await JsonSerializer.DeserializeAsync<T>(stream, SerializerOptions, cancellationToken);
            if (data is null)
            {
                _tracer.Debug(context, $"Content {contentHash.ToShortHash()} deserialized as null");
            }

            return data;
        }

        private static JsonSerializerOptions CreateJsonSerializerOptions()
        {
            JsonSerializerOptions options = new()
            {
                WriteIndented = true,
            };
            options.Converters.Add(new ContentHashJsonConverter());
            return options;
        }
    }
}
