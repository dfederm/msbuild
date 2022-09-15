// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Interfaces.Utils;
using BuildXL.Cache.ContentStore.Synchronization;
using BuildXL.Cache.MemoizationStore.Interfaces.Results;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Utilities.Collections;
using Microsoft.VisualStudio.Services.Common;

namespace MemoBuild.Caching
{
    internal sealed class GithubActionsCacheSession : ICacheSession
    {
        private static readonly HttpClient s_httpClient = new HttpClient();
        private const long PutBlockSize = 8 * 1024 * 1024;
        private static readonly string UrlBase = $"{Environment.GetEnvironmentVariable("ACTIONS_CACHE_URL").TrimEnd('/')}/_apis/artifactcache";
        private static readonly string Token = Environment.GetEnvironmentVariable("ACTIONS_RUNTIME_TOKEN");
        private readonly string Version;
        private const string VersionPrefix = "v009";
        private readonly LockSet<ContentHash> perHashLock = new LockSet<ContentHash>();

        public GithubActionsCacheSession(string version) => Version = VersionPrefix + (string.IsNullOrEmpty(version) ? "DEFAULT" : version);

        public string Name => nameof(GithubActionsCacheSession);

        public bool StartupCompleted { get; private set; }

        public bool StartupStarted {get; private set;}

        public bool ShutdownCompleted {get; private set;}
        
        public bool ShutdownStarted { get; private set; }

        private static string ComputeKey(ContentHash contentHash) => $"content-{contentHash.HashType}-{contentHash.Serialize()}";
        private static string ComputeSelectorsKey(Fingerprint wfp) => $"selector-{wfp.Serialize()}";
        private static string ComputeKey(StrongFingerprint sfp) => $"contenthashlist-{sfp.WeakFingerprint.Serialize()}-{sfp.Selector.ContentHash.Serialize()}";

        private static string ForSession(string key) => $"{key}-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        private static string AnySession(string key) => $"{key}-";

        public async Task<AddOrGetContentHashListResult> AddOrGetContentHashListAsync(Context context, StrongFingerprint strongFingerprint, ContentHashListWithDeterminism contentHashListWithDeterminism, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            var existing = await GetContentHashListAsync(context, strongFingerprint, cts);
            if (existing.Succeeded)
            {
                return new AddOrGetContentHashListResult(existing.ContentHashListWithDeterminism);
            }

            // meh there is a race here
            var key = ComputeKey(strongFingerprint);
            var serialized = new ContentHashListSerialized()
            {
                determinism = contentHashListWithDeterminism.Determinism,
                hashes = contentHashListWithDeterminism.ContentHashList.Hashes.Where(ch => ch.IsValid).Select(ch => ch.Serialize()).ToArray(),
                payload = contentHashListWithDeterminism.ContentHashList.HasPayload
                            ? contentHashListWithDeterminism.ContentHashList.Payload.ToArray().ToHex()
                            : null,
            };
            var json = JsonSerializer.Serialize(serialized);
            var bytes = Encoding.UTF8.GetBytes(json);
            using (var ms = new MemoryStream(bytes))
            {
                await PostCacheAsync(key, ms, cts);
                await PutSelectorAsync(strongFingerprint, cts);
                return new AddOrGetContentHashListResult(contentHashListWithDeterminism);
            }
        }
        
        public void Dispose() { }
        
        public async Task<GetContentHashListResult> GetContentHashListAsync(Context context, StrongFingerprint strongFingerprint, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            var key = ComputeKey(strongFingerprint);
            var response = await QueryCacheAsync(new[] { key }, cts);
            if (response == null)
            {
                return new GetContentHashListResult($"nothing found for {key}.");
            }
            var bytes = await response.GetByteArrayAsync();
            var json = Encoding.UTF8.GetString(bytes);
            var chl = JsonSerializer.Deserialize<ContentHashListSerialized>(json);
            if (chl?.hashes == null)
            {
                throw new NullReferenceException();
            }
            return new GetContentHashListResult(new ContentHashListWithDeterminism(
                new ContentHashList(
                    chl.hashes.Select(ch => new ContentHash(ch)).ToArray(),
                    chl.payload == null ? null : HexUtilities.HexToBytes(chl.payload)),
                chl.determinism
                ));
        }
        
        public IAsyncEnumerable<GetSelectorResult> GetSelectors(Context context, Fingerprint weakFingerprint, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            return GetSelectors(weakFingerprint, cts);
        }
        
        private async IAsyncEnumerable<GetSelectorResult> GetSelectors(Fingerprint weakFingerprint, [EnumeratorCancellation] CancellationToken cts)
        {
            var key = ComputeSelectorsKey(weakFingerprint);
            var response = await QueryCacheAsync(new[] { key }, cts);
            if (response == null)
            {
                yield break;
            }
            var bytes = await response.GetByteArrayAsync();
            var json = Encoding.UTF8.GetString(bytes);
            var selectors = JsonSerializer.Deserialize<SelectorsBucket>(json);
            if (selectors?.selectors == null)
            {
                throw new NullReferenceException();
            }
            
            foreach (var selector in selectors.selectors)
            {
                yield return new GetSelectorResult(selector.Deserialize());
            }
        }

        private async Task PutSelectorAsync(StrongFingerprint strongFingerprint, CancellationToken cts)
        {
            IEnumerable<GetSelectorResult> existing = await GetSelectors(strongFingerprint.WeakFingerprint, cts).ToListAsync(cts);

            if (existing.Any(s => s.Selector == strongFingerprint.Selector))
            {
                return;
            }
            
            existing = existing.Prepend(new GetSelectorResult(strongFingerprint.Selector));
            
            var bucket = new SelectorsBucket()
            {
                selectors = existing.Take(SelectorsBucket.MaxSelectors).Select(s => new SelectorSerialized(s.Selector)).ToArray()
            };
            var json = JsonSerializer.Serialize(bucket);
            var bytes = Encoding.UTF8.GetBytes(json);

            var key = ComputeSelectorsKey(strongFingerprint.WeakFingerprint);
            using (var ms = new MemoryStream(bytes))
            {
                await PostCacheAsync(key, ms, cts);
            }
        }
        
        public Task<BoolResult> IncorporateStrongFingerprintsAsync(Context context, IEnumerable<Task<StrongFingerprint>> strongFingerprints, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal) => throw new NotImplementedException();

        public async Task<OpenStreamResult> OpenStreamAsync(Context context, ContentHash contentHash, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            MemoryStream stream;

            if (contentHash.IsEmptyHash())
            {
                stream = new MemoryStream();
            }
            else
            {
                var key = ComputeKey(contentHash);
                var response = await QueryCacheAsync(new[] { key }, cts);
                if (response == null)
                {
                    return new OpenStreamResult(null);
                }

                stream = new MemoryStream(await response.GetByteArrayAsync());
            }
            return new OpenStreamResult(stream);
        }
        
        public async Task<PinResult> PinAsync(Context context, ContentHash contentHash, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            var key = ComputeKey(contentHash);
            var response = await QueryCacheAsync(new[] { key }, cts);
            if (response == null)
            {
                return PinResult.ContentNotFound;
            }
            else
            {
                return PinResult.Success;
            }
        }
        
        public Task<IEnumerable<Task<Indexed<PinResult>>>> PinAsync(Context context, IReadOnlyList<ContentHash> contentHashes, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            return Task.FromResult(contentHashes.WithIndices().Select(async ch => (await PinAsync(context, ch.value, cts)).WithIndex(ch.index)));
        }
        
        public Task<IEnumerable<Task<Indexed<PinResult>>>> PinAsync(Context context, IReadOnlyList<ContentHash> contentHashes, PinOperationConfiguration config)
        {
            return PinAsync(context, contentHashes, config.CancellationToken);
        }
        
        public async Task<PlaceFileResult> PlaceFileAsync(Context context, ContentHash contentHash, AbsolutePath path, FileAccessMode accessMode, FileReplacementMode replacementMode, FileRealizationMode realizationMode, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            var streamResult = await OpenStreamAsync(context, contentHash, cts);
            if (streamResult.StreamWithLength == null)
            {
                return PlaceFileResult.CreateContentNotFound(null);
            }

            switch (replacementMode)
            {
                case FileReplacementMode.None:
                    break;
                case FileReplacementMode.FailIfExists:
                    if (File.Exists(path.Path))
                    {
                        return PlaceFileResult.AlreadyExists;
                    }
                    break;
                case FileReplacementMode.ReplaceExisting:
                    if (File.Exists(path.Path))
                    {
                        File.Delete(path.Path);
                    }
                    break;
                case FileReplacementMode.SkipIfExists:
                    if (File.Exists(path.Path))
                    {
                        return PlaceFileResult.AlreadyExists;
                    }
                    break;
            }

            using (var stream = streamResult.StreamWithLength.Value.Stream)
            using (var file = File.OpenWrite(path.Path))
            {
                file.SetLength(streamResult.StreamWithLength.Value.Length);
                await stream.CopyToAsync(file);
                return PlaceFileResult.CreateSuccess(PlaceFileResult.ResultCode.PlacedWithCopy, file.Length, PlaceFileResult.Source.BackingStore);
            }
        }
        
        public Task<IEnumerable<Task<Indexed<PlaceFileResult>>>> PlaceFileAsync(Context context, IReadOnlyList<ContentHashWithPath> hashesWithPaths, FileAccessMode accessMode, FileReplacementMode replacementMode, FileRealizationMode realizationMode, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            return Task.FromResult(hashesWithPaths.WithIndices().Select(async ch => (await PlaceFileAsync(context, ch.value.Hash, ch.value.Path, accessMode, replacementMode, realizationMode, cts)).WithIndex(ch.index)));
        }

        public Task<PutResult> PutFileAsync(Context context, HashType hashType, AbsolutePath path, FileRealizationMode realizationMode, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            throw new NotImplementedException("hash it first");
        }
        
        public async Task<PutResult> PutFileAsync(Context context, ContentHash contentHash, AbsolutePath path, FileRealizationMode realizationMode, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            using(var file = File.OpenRead(path.Path))
            {
                return await PutStreamAsync(context, contentHash, file, cts);
            }
        }
        
        public async Task<PutResult> PutStreamAsync(Context context, HashType hashType, Stream stream, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            var hasher = HashInfoLookup.GetContentHasher(hashType);
            using (var hashingStream = hasher.CreateReadHashingStream(stream.Length, stream))
            using (var ms = new MemoryStream())
            {
                await hashingStream.CopyToAsync(ms);
                ms.Position = 0;
                return await PutStreamAsync(context, await hashingStream.GetContentHashAsync(), ms, cts);
            }
        }

        public async Task<PutResult> PutStreamAsync(Context context, ContentHash contentHash, Stream stream, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            if (stream.Length == 0)
            {
                return new PutResult(contentHash, stream.Length);
            }

            using (await perHashLock.AcquireAsync(contentHash))
            {
                var key = ComputeKey(contentHash);

                var response = await QueryCacheAsync(new[] { key }, cts);
                if (response == null)
                {
                    await PostCacheAsync(key, stream, cts);
                }
                return new PutResult(contentHash, stream.Length);
            }
        }
        
        public Task<BoolResult> ShutdownAsync(Context context)
        {
            ShutdownStarted = true;
            ShutdownCompleted = true;
            return BoolResult.SuccessTask;
        }

        public Task<BoolResult> StartupAsync(Context context) {
            StartupStarted = true;
            StartupCompleted = true;
            return BoolResult.SuccessTask;
        }

        private async Task<QueryCacheResponse?> QueryCacheAsync(string[] keys, CancellationToken token)
        {
            var builder = new UriBuilder($"{UrlBase}/cache");
            builder.AppendQueryEscapeUriString("keys", string.Join(",", keys.SelectArray(k => AnySession(k))));
            builder.AppendQueryEscapeUriString("version", Version);
            var request = AddHeaders(new HttpRequestMessage(HttpMethod.Get, builder.Uri));
            var response = await s_httpClient.SendAsync(request, token);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
            response.EnsureSuccessStatusCode();
            return JsonSerializer.Deserialize<QueryCacheResponse>(await response.Content.ReadAsStringAsync());
        }

        private async Task<string> PostCacheAsync(string key, Stream content, CancellationToken cancellationToken)
        {
            ulong cacheId;
            string finalKey = ForSession(key);
            {
                var builder = new UriBuilder($"{UrlBase}/caches");
                var request = AddHeaders(new HttpRequestMessage(HttpMethod.Post, builder.Uri));
                
                var jsonRequest = new ReserveCacheEntryRequest
                {
                    key = finalKey,
                    version = Version,
                };

                request.Content = new StringContent(JsonSerializer.Serialize(jsonRequest), Encoding.UTF8, "application/json");
                var response = await s_httpClient.SendAsync(request, cancellationToken);
                response.EnsureSuccessStatusCode();
                var cacheResponse = JsonSerializer.Deserialize<ReserveCacheEntryResponse>(await response.Content.ReadAsStringAsync());
                if (cacheResponse?.cacheId == null)
                {
                    throw new NullReferenceException();
                }
                cacheId = cacheResponse.cacheId.Value;
            }
            
            int blocks = (int)((content.Length + PutBlockSize - 1) / PutBlockSize);
            for (int i = 0; i < blocks; i++)
            {
                long startOffsetInclusive = PutBlockSize * i;
                long endOffsetExclusive = Math.Min(startOffsetInclusive + PutBlockSize, content.Length);
                int blockLength = (int)(endOffsetExclusive - startOffsetInclusive);
                
                var buffer = new byte[blockLength];
                await content.ReadRequiredRangeAsync(buffer, 0, blockLength);

                var builder = new UriBuilder($"{UrlBase}/caches/{cacheId}");
                var request = AddHeaders(new HttpRequestMessage(new HttpMethod("PATCH"), builder.Uri));
                request.Content = new ByteArrayContent(buffer);
                // inclusive https://developer.mozilla.org/en-US/docs/Web/HTTP/Headers/Content-Range
                request.Content.Headers.ContentRange = new ContentRangeHeaderValue(startOffsetInclusive, endOffsetExclusive - 1, content.Length);
                request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                var response = await s_httpClient.SendAsync(request, cancellationToken);
                response.EnsureSuccessStatusCode();
            }

            {
                var builder = new UriBuilder($"{UrlBase}/caches/{cacheId}");
                var request = AddHeaders(new HttpRequestMessage(HttpMethod.Post, builder.Uri));
                var jsonRequest = new SealCacheEntryRequest
                {
                    size = content.Length,
                };

                request.Content = new StringContent(JsonSerializer.Serialize(jsonRequest), Encoding.UTF8, "application/json");
                var response = await s_httpClient.SendAsync(request, cancellationToken);
                response.EnsureSuccessStatusCode();
            }

            return finalKey;
        }

        private static HttpRequestMessage AddHeaders(HttpRequestMessage request)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.RequestUri = request.RequestUri.AppendQuery("api-version", "6.0-preview.1");
            return request;
        }

        private class QueryCacheRequest
        {
            public string? keys { get; set; }
            public string? version { get; set; }
        }

        private class QueryCacheResponse
        {
            public string? cacheKey { get; set; }
            public string? scope { get; set; }
            public string? archiveLocation { get; set; }

            public Task<byte[]> GetByteArrayAsync()
            {
                return s_httpClient.GetByteArrayAsync(this.archiveLocation);
            }
        }

        private class ReserveCacheEntryRequest
        {
            public string? key { get; set; }
            public string? version { get; set; }
        }

        private class ReserveCacheEntryResponse
        {
            public ulong? cacheId { get; set; }
        }

        private class SealCacheEntryRequest
        {
            public long size { get; set; }
        }

        private class SelectorSerialized
        {
            [JsonConstructor]
            public SelectorSerialized() { }

            public SelectorSerialized(Selector s)
            {
                this.contentHash = s.ContentHash.Serialize();
                this.output = s.Output.ToHex();
            }

            public string? contentHash { get; set; }
            public string? output { get; set; }

            public Selector Deserialize()
            {
                if (this.contentHash == null)
                {
                    throw new NullReferenceException();
                }

                return new Selector(
                    new ContentHash(this.contentHash),
                    HexUtilities.HexToBytes(this.output));
            }
        }

        private class SelectorsBucket
        {
            public const int MaxSelectors = 100;
            public SelectorSerialized[]? selectors { get; set; }
        }

        private class ContentHashListSerialized
        {
            public CacheDeterminism determinism { get; set; }
            public string? payload { get; set; }
            public string[]? hashes { get; set; }
        }
    }
}
