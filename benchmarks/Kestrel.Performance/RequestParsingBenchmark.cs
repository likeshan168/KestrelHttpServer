﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Buffers;
using System.IO.Pipelines;
using BenchmarkDotNet.Attributes;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http;
using Microsoft.AspNetCore.Server.Kestrel.Performance.Mocks;

namespace Microsoft.AspNetCore.Server.Kestrel.Performance
{
    [ParameterizedJobConfig(typeof(CoreConfig))]
    public class RequestParsingBenchmark
    {
        public IPipe Pipe { get; set; }

        public Http1Connection<object> Http1Connection { get; set; }

        [IterationSetup]
        public void Setup()
        {
            var bufferPool = new MemoryPool();
            var pair = PipeFactory.CreateConnectionPair(bufferPool);

            var serviceContext = new ServiceContext
            {
                DateHeaderValueManager = new DateHeaderValueManager(),
                ServerOptions = new KestrelServerOptions(),
                Log = new MockTrace(),
                HttpParserFactory = f => new HttpParser<Http1ParsingHandler>()
            };

            var http1Connection = new Http1Connection<object>(application: null, context: new Http1ConnectionContext
            {
                ServiceContext = serviceContext,
                ConnectionFeatures = new FeatureCollection(),
                BufferPool = bufferPool,
                Application = pair.Application,
                Transport = pair.Transport,
                TimeoutControl = new MockTimeoutControl()
            });

            http1Connection.Reset();

            Http1Connection = http1Connection;
            Pipe = new Pipe(new PipeOptions(bufferPool));
        }

        [Benchmark(Baseline = true, OperationsPerInvoke = RequestParsingData.InnerLoopCount)]
        public void PlaintextTechEmpower()
        {
            for (var i = 0; i < RequestParsingData.InnerLoopCount; i++)
            {
                InsertData(RequestParsingData.PlaintextTechEmpowerRequest);
                ParseData();
            }
        }

        [Benchmark(OperationsPerInvoke = RequestParsingData.InnerLoopCount)]
        public void PlaintextAbsoluteUri()
        {
            for (var i = 0; i < RequestParsingData.InnerLoopCount; i++)
            {
                InsertData(RequestParsingData.PlaintextAbsoluteUriRequest);
                ParseData();
            }
        }

        [Benchmark(OperationsPerInvoke = RequestParsingData.InnerLoopCount * RequestParsingData.Pipelining)]
        public void PipelinedPlaintextTechEmpower()
        {
            for (var i = 0; i < RequestParsingData.InnerLoopCount; i++)
            {
                InsertData(RequestParsingData.PlaintextTechEmpowerPipelinedRequests);
                ParseData();
            }
        }

        [Benchmark(OperationsPerInvoke = RequestParsingData.InnerLoopCount * RequestParsingData.Pipelining)]
        public void PipelinedPlaintextTechEmpowerDrainBuffer()
        {
            for (var i = 0; i < RequestParsingData.InnerLoopCount; i++)
            {
                InsertData(RequestParsingData.PlaintextTechEmpowerPipelinedRequests);
                ParseDataDrainBuffer();
            }
        }

        [Benchmark(OperationsPerInvoke = RequestParsingData.InnerLoopCount)]
        public void LiveAspNet()
        {
            for (var i = 0; i < RequestParsingData.InnerLoopCount; i++)
            {
                InsertData(RequestParsingData.LiveaspnetRequest);
                ParseData();
            }
        }

        [Benchmark(OperationsPerInvoke = RequestParsingData.InnerLoopCount * RequestParsingData.Pipelining)]
        public void PipelinedLiveAspNet()
        {
            for (var i = 0; i < RequestParsingData.InnerLoopCount; i++)
            {
                InsertData(RequestParsingData.LiveaspnetPipelinedRequests);
                ParseData();
            }
        }

        [Benchmark(OperationsPerInvoke = RequestParsingData.InnerLoopCount)]
        public void Unicode()
        {
            for (var i = 0; i < RequestParsingData.InnerLoopCount; i++)
            {
                InsertData(RequestParsingData.UnicodeRequest);
                ParseData();
            }
        }

        [Benchmark(OperationsPerInvoke = RequestParsingData.InnerLoopCount * RequestParsingData.Pipelining)]
        public void UnicodePipelined()
        {
            for (var i = 0; i < RequestParsingData.InnerLoopCount; i++)
            {
                InsertData(RequestParsingData.UnicodePipelinedRequests);
                ParseData();
            }
        }

        private void InsertData(byte[] bytes)
        {
            var buffer = Pipe.Writer.Alloc(2048);
            buffer.Write(bytes);
            // There should not be any backpressure and task completes immediately
            buffer.FlushAsync().GetAwaiter().GetResult();
        }

        private void ParseDataDrainBuffer()
        {
            var awaitable = Pipe.Reader.ReadAsync();
            if (!awaitable.IsCompleted)
            {
                // No more data
                return;
            }

            var readableBuffer = awaitable.GetResult().Buffer;
            do
            {
                Http1Connection.Reset();

                if (!Http1Connection.TakeStartLine(readableBuffer, out var consumed, out var examined))
                {
                    ErrorUtilities.ThrowInvalidRequestLine();
                }

                readableBuffer = readableBuffer.Slice(consumed);

                if (!Http1Connection.TakeMessageHeaders(readableBuffer, out consumed, out examined))
                {
                    ErrorUtilities.ThrowInvalidRequestHeaders();
                }

                readableBuffer = readableBuffer.Slice(consumed);
            }
            while (readableBuffer.Length > 0);

            Pipe.Reader.Advance(readableBuffer.End);
        }

        private void ParseData()
        {
            do
            {
                var awaitable = Pipe.Reader.ReadAsync();
                if (!awaitable.IsCompleted)
                {
                    // No more data
                    return;
                }

                var result = awaitable.GetAwaiter().GetResult();
                var readableBuffer = result.Buffer;

                Http1Connection.Reset();

                if (!Http1Connection.TakeStartLine(readableBuffer, out var consumed, out var examined))
                {
                    ErrorUtilities.ThrowInvalidRequestLine();
                }
                Pipe.Reader.Advance(consumed, examined);

                result = Pipe.Reader.ReadAsync().GetAwaiter().GetResult();
                readableBuffer = result.Buffer;

                if (!Http1Connection.TakeMessageHeaders(readableBuffer, out consumed, out examined))
                {
                    ErrorUtilities.ThrowInvalidRequestHeaders();
                }
                Pipe.Reader.Advance(consumed, examined);
            }
            while (true);
        }
    }
}
