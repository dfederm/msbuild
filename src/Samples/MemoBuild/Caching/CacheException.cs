// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Runtime.Serialization;

namespace MemoBuild.Caching
{
    public sealed class CacheException : Exception
    {
        public CacheException(string message)
            : base(message) { }

        private CacheException(SerializationInfo serializationInfo, StreamingContext streamingContext)
            : base(serializationInfo, streamingContext)
        {
        }

    }
}
