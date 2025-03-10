﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Analysers;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnostics.Windows.Configs;
using BenchmarkDotNet.Environments;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Validators;
using Corax;
using Corax.Mappings;
using Sparrow.Server;
using Sparrow.Threading;

namespace Voron.Benchmark.Corax
{

    //[DisassemblyDiagnoser(maxDepth: 900, printSource:true, exportHtml: true, exportDiff: true)]
    //[InliningDiagnoser(logFailuresOnly: true, allowedNamespaces: new[] { "Corax" })]
    public class OrderByBenchmark
    {
        protected StorageEnvironment Env;
        public virtual bool DeleteBeforeSuite { get; protected set; } = true;
        public virtual bool DeleteAfterSuite { get; protected set; } = true;
        public virtual bool DeleteBeforeEachBenchmark { get; protected set; } = false;


        [Params(1024, 2048, 4096, 16 * 1024)]        
        //[Params(1024)]
        public int BufferSize { get; set; }

        [Params(16, 64, 256)]
        //[Params(16)]
        public int TakeSize { get; set; }


        /// <summary>
        /// Path to store the benchmark database into.
        /// </summary>
        public const string Path = Configuration.Path;

        /// <summary>
        /// This is the job configuration for storage benchmarks. Changing this
        /// will affect all benchmarks done.
        /// </summary>
        private class Config : ManualConfig
        {
            public Config()
            {
                AddJob(new Job
                {
                    Environment =
                    {
                        Runtime = CoreRuntime.Core50,
                        Platform = BenchmarkDotNet.Environments.Platform.X64,
                        Jit = Jit.RyuJit
                    },
                    Run =
                    {
                        LaunchCount = 1,
                        WarmupCount = 1,
                        IterationCount = 1,
                        InvocationCount = 1,
                        UnrollFactor = 1
                    },
                    // TODO: Next line is just for testing. Fine tune parameters.
                });

                // Exporters for data
                AddExporter(GetExporters().ToArray());
                // Generate plots using R if %R_HOME% is correctly set
                AddExporter(RPlotExporter.Default);

                AddColumn(StatisticColumn.AllStatistics);

                AddValidator(BaselineValidator.FailOnError);
                AddValidator(JitOptimizationsValidator.FailOnError);

                AddAnalyser(EnvironmentAnalyser.Default);
            }
        }

        public OrderByBenchmark()
        {
            if (DeleteBeforeSuite)
            {
                DeleteStorage();
            }

            if (!DeleteBeforeEachBenchmark)
            {
                Env = new StorageEnvironment(StorageEnvironmentOptions.ForPath(Path));
                GenerateData(Env);
            }
        }

        ~OrderByBenchmark()
        {
            if (!DeleteBeforeEachBenchmark)
            {
                Env.Dispose();
            }

            if (DeleteAfterSuite)
            {
                DeleteStorage();
            }
        }

        private static void GenerateData(StorageEnvironment env)
        {
            using var bsc = new ByteStringContext(SharedMultipleUseFlag.None);
            Slice.From(bsc, "Name", ByteStringType.Immutable, out var nameSlice);
            Slice.From(bsc, "Family", ByteStringType.Immutable, out var familySlice);
            Slice.From(bsc, "Age", ByteStringType.Immutable, out var ageSlice);
            Slice.From(bsc, "Type", ByteStringType.Immutable, out var typeSlice);

            using var builder = IndexFieldsMappingBuilder.CreateForWriter(false)
                .AddBinding(0, nameSlice)
                .AddBinding(1, familySlice)
                .AddBinding(2, ageSlice)
                .AddBinding(3, typeSlice);
            using var fields = builder.Build();
            
            using (var writer = new IndexWriter(env, fields))
            {
                {
                    var entryWriter = new IndexEntryWriter(bsc, fields);
                    entryWriter.Write(0, Encoding.UTF8.GetBytes("Arava"));
                    entryWriter.Write(1, Encoding.UTF8.GetBytes("Eini"));
                    entryWriter.Write(2, Encoding.UTF8.GetBytes(12L.ToString()), 12L, 12D);
                    entryWriter.Write(3, Encoding.UTF8.GetBytes("Dog"));
                    using (var _ = entryWriter.Finish(out var entry))
                    {
                        writer.Index("dogs/arava", entry.ToSpan());
                    }
                }

                {
                    var entryWriter = new IndexEntryWriter(bsc, fields);
                    entryWriter.Write(0, Encoding.UTF8.GetBytes("Phoebe"));
                    entryWriter.Write(1, Encoding.UTF8.GetBytes("Eini"));
                    entryWriter.Write(2, Encoding.UTF8.GetBytes(7.ToString()), 7L, 7D);
                    entryWriter.Write(3, Encoding.UTF8.GetBytes("Dog"));
                    using (var _ = entryWriter.Finish(out var entry))
                    {
                        writer.Index("dogs/phoebe", entry.ToSpan());
                    }
                }

                for (int i = 0; i < 100_000; i++)
                {
                    var entryWriter = new IndexEntryWriter(bsc, fields);
                    entryWriter.Write(0, Encoding.UTF8.GetBytes("Dog #" + i));
                    entryWriter.Write(1, Encoding.UTF8.GetBytes("families/" + (i % 1024)));
                    var age = i % 15;
                    entryWriter.Write(2, Encoding.UTF8.GetBytes(age.ToString()), age, age);
                    entryWriter.Write(3, Encoding.UTF8.GetBytes("Dog"));
                    using (var _ = entryWriter.Finish(out var entry))
                    {
                        writer.Index("dogs/" + i, entry.ToSpan());
                    }
                }

                writer.Commit();
            }
        }

        [GlobalSetup]
        public virtual void Setup()
        {
            if (DeleteBeforeEachBenchmark)
            {
                DeleteStorage();
                Env = new StorageEnvironment(StorageEnvironmentOptions.ForPath(Path));
                GenerateData(Env);
            }

            _ids = new long[BufferSize];
            _indexSearcher = new IndexSearcher(Env);

            _bsc = new ByteStringContext(SharedMultipleUseFlag.None);
            Slice.From(_bsc, "Type", ByteStringType.Immutable, out _typeSlice);
            Slice.From(_bsc, "Dog", ByteStringType.Immutable, out _dogSlice);
            Slice.From(_bsc, "Age", ByteStringType.Immutable, out _ageSlice);
            Slice.From(_bsc, "1", ByteStringType.Immutable, out _ageValueSlice);
        }


        [GlobalCleanup]
        public virtual void Cleanup()
        {
            if (DeleteBeforeEachBenchmark)
            {
                Env.Dispose();
            }
        }

        private void DeleteStorage()
        {
            if (!Directory.Exists(Path))
                return;

            for (var i = 0; i < 10; ++i)
            {
                try
                {
                    Directory.Delete(Path, true);
                    break;
                }
                catch (DirectoryNotFoundException)
                {
                    break;
                }
                catch (Exception)
                {
                    Thread.Sleep(20);
                }
            }
        }

        private long[] _ids;
        private IndexSearcher _indexSearcher;
        private ByteStringContext _bsc;
        private Slice _typeSlice;
        private Slice _dogSlice;
        private Slice _ageSlice;
        private Slice _ageValueSlice;

        [Benchmark]
        public void OrderByRuntimeQuery()
        {            
            var typeTerm = _indexSearcher.TermQuery(_typeSlice, "Dog");
            var ageTerm = _indexSearcher.StartWithQuery(_ageSlice, _ageValueSlice);
            var andQuery = _indexSearcher.And(typeTerm, ageTerm);
            var query = _indexSearcher.OrderByAscending(andQuery, fieldId: 2, take: TakeSize);           

            Span<long> ids = _ids;
            while (query.Fill(ids) != 0)
                ;
        }       
    }
}
