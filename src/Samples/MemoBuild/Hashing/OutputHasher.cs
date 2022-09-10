// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;

namespace MemoBuild.Hashing
{
    internal sealed class OutputHasher : IOutputHasher
    {
        private static readonly int HashingParallelism = Environment.ProcessorCount;

        private readonly IContentHasher _contentHasher;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private readonly Channel<HashOperationContext> _hashingChannel;
        private readonly Task[] _channelWorkerTasks;

        public OutputHasher(IContentHasher contentHasher)
        {
            _contentHasher = contentHasher;
            _cancellationTokenSource = new CancellationTokenSource();
            _hashingChannel = Channel.CreateUnbounded<HashOperationContext>();

            // Create a bunch of worker tasks to process the hash operations.
            _channelWorkerTasks = new Task[HashingParallelism];
            for (int i = 0; i < _channelWorkerTasks.Length; i++)
            {
                _channelWorkerTasks[i] = Task.Run(
                    async () =>
                    {
                        // Not using 'Reader.ReadAllAsync' because its not available in the version we use here.
                        // Also not passing using the cancellation token here as we need to drain the entire channel to ensure we don't leave dangling Tasks.
                        while (await _hashingChannel.Reader.WaitToReadAsync())
                        {
                            while (_hashingChannel.Reader.TryRead(out HashOperationContext context))
                            {
                                await ComputeHashInternalAsync(context, _cancellationTokenSource.Token);
                            }
                        }
                    });
            }
        }

        public async Task<ContentHash> ComputeHashAsync(string filePath, CancellationToken cancellationToken)
        {
            TaskCompletionSource<ContentHash> taskCompletionSource = new();
            HashOperationContext context = new(filePath, taskCompletionSource);
            await _hashingChannel.Writer.WriteAsync(context, cancellationToken);
            return await taskCompletionSource.Task;
        }

        public async ValueTask DisposeAsync()
        {
            _cancellationTokenSource.Cancel();
            _hashingChannel.Writer.Complete();
            await _hashingChannel.Reader.Completion;
            await Task.WhenAll(_channelWorkerTasks);
            _cancellationTokenSource.Dispose();
        }

        private async Task ComputeHashInternalAsync(HashOperationContext context, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                context.TaskCompletionSource.TrySetCanceled(cancellationToken);
                return;
            }

            try
            {
                ContentHash contentHash;
                using (FileStream fileStream = File.Open(context.FilePath, FileMode.Open, System.IO.FileAccess.Read, FileShare.Read))
                {
                    contentHash = await _contentHasher.GetContentHashAsync(fileStream);
                }

                context.TaskCompletionSource.TrySetResult(contentHash);
            }
            catch (Exception ex)
            {
                context.TaskCompletionSource.TrySetException(ex);
            }
        }

        private readonly struct HashOperationContext
        {
            public HashOperationContext(string filePath, TaskCompletionSource<ContentHash> taskCompletionSource)
            {
                FilePath = filePath;
                TaskCompletionSource = taskCompletionSource;
            }

            public string FilePath { get; }

            public TaskCompletionSource<ContentHash> TaskCompletionSource { get; }
        }
    }
}
