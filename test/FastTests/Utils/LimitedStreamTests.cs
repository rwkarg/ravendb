﻿using System;
using System.IO;
using System.Linq;
using FastTests.Voron.FixedSize;
using FastTests.Voron.Util;
using Raven.Client.Documents.Operations.Attachments;
using Xunit;
using Xunit.Abstractions;

namespace FastTests.Utils
{
    public class LimitedStreamTests : NoDisposalNeeded
    {
        public LimitedStreamTests(ITestOutputHelper output) : base(output)
        {
        }

        [Theory]
        [InlineDataWithRandomSeed]
        public void Should_properly_read_ranges(int seed)
        {
            var r = new Random(seed);

            var bytes = new byte[r.Next(128, 1024 * 1024 * 3)];
            r.NextBytes(bytes);

            var ms = new MemoryStream(bytes);
            var cs = new ConcatStream(new ConcatStream.RentedBuffer
            {
                Buffer = null,
                Count = 0,
                Offset = 0
            }, ms);
            var max = r.Next(1, bytes.Length / 2);

            var entireStream = new LimitedStream(cs, ms.Length);
            Assert.Equal(bytes, entireStream.ReadData());

            ms.Position = 0;

            var numberOfChunks = ms.Length / max + (ms.Length % max != 0 ? 1 : 0);

            for (int i = 0; i < numberOfChunks; i++)
            {
                var pos = ms.Position;
                var prev = Math.Min(pos + max, ms.Length);

                var ls = new LimitedStream(cs, prev - pos);

                if (i == numberOfChunks - 1)
                {
                    Assert.Equal(ms.Length % max, ls.Length);
                }
                else
                {
                    Assert.Equal(max, ls.Length);
                }

                var read = ls.ReadData();

                Assert.Equal(ls.Length, read.Length);
                Assert.Equal(bytes.Skip((int)pos).Take(read.Length).ToArray(), read);
            }
        }
    }
}
