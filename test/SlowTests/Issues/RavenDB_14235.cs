﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FastTests;
using Raven.Client.Documents.Linq;
using Xunit;
using Xunit.Abstractions;

namespace SlowTests.Issues
{
    public class RavenDB_14235 : RavenTestBase
    {
        private readonly List<int> _listOfNumsThatAreNulls = new List<int> { 9, 10, 11, 12, 13 };

        public RavenDB_14235(ITestOutputHelper output) : base(output)
        {
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(4)]
        [InlineData(8)]
        [InlineData(16)]
        [InlineData(32)]
        [InlineData(64)]
        [InlineData(128)]
        [InlineData(256)]
        public async Task PassingOnlyEscapedCharactersAsId(int size)
        {
            char[] chars = new char[size];

            for (int c = 0; c < 32; c++)
            {
                if (c.In(_listOfNumsThatAreNulls))
                {
                    // id string created from 'only' those chars, will be identified as whitespace and will get replaced by guid.
                    continue;
                }

                for (int i = 0; i < size; i++)
                    chars[i] = (char)c;

                var str = new string(chars);

                using (var store = GetDocumentStore())
                {
                    using (var session = store.OpenAsyncSession())
                    {
                        await session.StoreAsync(new User { WeirdName = str }, str);
                        await session.SaveChangesAsync();

                        var u = await session.LoadAsync<User>(str);
                        var id = session.Advanced.GetDocumentId(u);

                        Assert.Equal(str, u.WeirdName);
                        Assert.Equal(str, id);
                    }
                }
            }
        }

        [InlineData(4)]
        [InlineData(8)]
        [InlineData(16)]
        [InlineData(32)]
        [InlineData(128)]
        [InlineData(256)]
        public async Task CombiningEscapedCharactersAsId(int size)
        {
            var partialSize = size / 4;
            const string abc = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            var magicNums = new List<int> { 92, 34, 8, 9, 10, 12, 13 };
            char[] chars = new char[partialSize];

            for (int c = 0; c < 32; c++)
            {
                for (int i = 0; i < partialSize; i++)
                    chars[i] = (char)c;

                var str = new string(chars);

                var random = new Random();
                str += new string(Enumerable.Repeat(abc, partialSize).Select(s => s[random.Next(s.Length)]).ToArray());

                for (int i = 0; i < partialSize; i++)
                    chars[i] = (char)magicNums[random.Next(0, magicNums.Count)];

                str += new string(chars);
                str += new string(Enumerable.Repeat(abc, partialSize).Select(s => s[random.Next(s.Length)]).ToArray());

                using (var store = GetDocumentStore())
                {
                    using (var session = store.OpenAsyncSession())
                    {
                        await session.StoreAsync(new User { WeirdName = str }, str);
                        await session.SaveChangesAsync();

                        var u = await session.LoadAsync<User>(str);
                        var id = session.Advanced.GetDocumentId(u);

                        Assert.Equal(str, u.WeirdName);
                        Assert.Equal(str, id);
                    }
                }
            }
        }

        private class User
        {
            public string WeirdName { get; set; }
        }
    }
}
