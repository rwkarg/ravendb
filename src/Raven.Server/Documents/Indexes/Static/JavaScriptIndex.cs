﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Esprima;
using Jint;
using Jint.Native;
using Jint.Native.Function;
using Jint.Native.Object;
using Jint.Runtime;
using Jint.Runtime.Interop;
using Raven.Client;
using Raven.Client.Documents.Indexes;
using Raven.Client.Exceptions.Documents.Indexes;
using Raven.Server.Config;
using Raven.Server.Documents.Indexes.Configuration;
using Raven.Server.Documents.Patch;
using Raven.Server.Extensions;
using Raven.Server.ServerWide;
using Sparrow.Server;

namespace Raven.Server.Documents.Indexes.Static
{
    public class TimeSeriesJavaScriptIndex : AbstractJavaScriptIndex
    {
        private const string MapPrefix = "timeSeries.";

        private const string NameProperty = "name";

        public TimeSeriesJavaScriptIndex(IndexDefinition definition, RavenConfiguration configuration)
            : base(definition, configuration)
        {
        }

        protected override string MapCode => @"
function map(collection, name, lambda) {
    var map = {
        collection: collection,
        name: name,
        method: lambda,
        moreArgs: Array.prototype.slice.call(arguments, 3)
    };
    globalDefinition.maps.push(map);
}";

        protected override List<string> GetMappingFunctions()
        {
            var maps = base.GetMappingFunctions();
            for (int i = 0; i < maps.Count; i++)
            {
                if (maps[i].StartsWith(MapPrefix, StringComparison.OrdinalIgnoreCase) == false)
                    continue;

                maps[i] = maps[i].Substring(MapPrefix.Length);
            }

            return maps;
        }

        protected override void ProcessMaps(ObjectInstance definitions, JintPreventResolvingTasksReferenceResolver resolver, List<string> mapList, List<MapMetadata> mapReferencedCollections, out Dictionary<string, Dictionary<string, List<JavaScriptMapOperation>>> collectionFunctions)
        {
            var mapsArray = definitions.GetProperty(MapsProperty).Value;
            if (mapsArray.IsNull() || mapsArray.IsUndefined() || mapsArray.IsArray() == false)
                ThrowIndexCreationException($"doesn't contain any map function or '{GlobalDefinitions}.{Maps}' was modified in the script");

            var maps = mapsArray.AsArray();
            if (maps.Length == 0)
                ThrowIndexCreationException($"doesn't contain any map functions or '{GlobalDefinitions}.{Maps}' was modified in the script");

            collectionFunctions = new Dictionary<string, Dictionary<string, List<JavaScriptMapOperation>>>();
            for (int i = 0; i < maps.Length; i++)
            {
                var mapObj = maps.Get(i.ToString());
                if (mapObj.IsNull() || mapObj.IsUndefined() || mapObj.IsObject() == false)
                    ThrowIndexCreationException($"map function #{i} is not a valid object");
                var map = mapObj.AsObject();
                if (map.HasProperty(CollectionProperty) == false)
                    ThrowIndexCreationException($"map function #{i} is missing a collection name");
                var mapCollectionStr = map.Get(CollectionProperty);
                if (mapCollectionStr.IsString() == false)
                    ThrowIndexCreationException($"map function #{i} collection name isn't a string");
                var mapCollection = mapCollectionStr.AsString();

                if (collectionFunctions.TryGetValue(mapCollection, out var subCollectionFunctions) == false)
                    collectionFunctions[mapCollection] = subCollectionFunctions = new Dictionary<string, List<JavaScriptMapOperation>>();

                if (map.HasProperty(NameProperty) == false)
                    ThrowIndexCreationException($"map function #{i} is missing its {NameProperty} property");
                var mapNameStr = map.Get(NameProperty);
                if (mapNameStr.IsString() == false)
                    ThrowIndexCreationException($"map function #{i} TimeSeries name isn't a string");
                var mapName = mapNameStr.AsString();

                if (subCollectionFunctions.TryGetValue(mapName, out var list) == false)
                    subCollectionFunctions[mapName] = list = new List<JavaScriptMapOperation>();

                if (map.HasProperty(MethodProperty) == false)
                    ThrowIndexCreationException($"map function #{i} is missing its {MethodProperty} property");
                var funcInstance = map.Get(MethodProperty).As<FunctionInstance>();
                if (funcInstance == null)
                    ThrowIndexCreationException($"map function #{i} {MethodProperty} property isn't a 'FunctionInstance'");
                var operation = new JavaScriptMapOperation(_engine, resolver)
                {
                    MapFunc = funcInstance,
                    IndexName = Definition.Name,
                    MapString = mapList[i]
                };
                if (map.HasOwnProperty(MoreArgsProperty))
                {
                    var moreArgsObj = map.Get(MoreArgsProperty);
                    if (moreArgsObj.IsArray())
                    {
                        var array = moreArgsObj.AsArray();
                        if (array.Length > 0)
                        {
                            operation.MoreArguments = array;
                        }
                    }
                }

                operation.Analyze(_engine);
                if (ReferencedCollections.TryGetValue(mapCollection, out var collectionNames) == false)
                {
                    collectionNames = new HashSet<CollectionName>();
                    ReferencedCollections.Add(mapCollection, collectionNames);
                }

                collectionNames.UnionWith(mapReferencedCollections[i].ReferencedCollections);

                if (mapReferencedCollections[i].HasCompareExchangeReferences)
                    CollectionsWithCompareExchangeReferences.Add(mapCollection);

                list.Add(operation);
            }
        }
    }

    public class JavaScriptIndex : AbstractJavaScriptIndex
    {
        public JavaScriptIndex(IndexDefinition definition, RavenConfiguration configuration)
            : base(definition, configuration)
        {
        }

        protected override string MapCode => @"
function map(name, lambda) {
    var map = {
        collection: name,
        method: lambda,
        moreArgs: Array.prototype.slice.call(arguments, 2)
    };
    globalDefinition.maps.push(map);
}";

        protected override void ProcessMaps(ObjectInstance definitions, JintPreventResolvingTasksReferenceResolver resolver, List<string> mapList, List<MapMetadata> mapReferencedCollections, out Dictionary<string, Dictionary<string, List<JavaScriptMapOperation>>> collectionFunctions)
        {
            var mapsArray = definitions.GetProperty(MapsProperty).Value;
            if (mapsArray.IsNull() || mapsArray.IsUndefined() || mapsArray.IsArray() == false)
                ThrowIndexCreationException($"doesn't contain any map function or '{GlobalDefinitions}.{Maps}' was modified in the script");

            var maps = mapsArray.AsArray();
            if (maps.Length == 0)
                ThrowIndexCreationException($"doesn't contain any map functions or '{GlobalDefinitions}.{Maps}' was modified in the script");

            collectionFunctions = new Dictionary<string, Dictionary<string, List<JavaScriptMapOperation>>>();
            for (int i = 0; i < maps.Length; i++)
            {
                var mapObj = maps.Get(i.ToString());
                if (mapObj.IsNull() || mapObj.IsUndefined() || mapObj.IsObject() == false)
                    ThrowIndexCreationException($"map function #{i} is not a valid object");
                var map = mapObj.AsObject();
                if (map.HasProperty(CollectionProperty) == false)
                    ThrowIndexCreationException($"map function #{i} is missing a collection name");
                var mapCollectionStr = map.Get(CollectionProperty);
                if (mapCollectionStr.IsString() == false)
                    ThrowIndexCreationException($"map function #{i} collection name isn't a string");
                var mapCollection = mapCollectionStr.AsString();

                if (collectionFunctions.TryGetValue(mapCollection, out var subCollectionFunctions) == false)
                    collectionFunctions[mapCollection] = subCollectionFunctions = new Dictionary<string, List<JavaScriptMapOperation>>();

                if (subCollectionFunctions.TryGetValue(mapCollection, out var list) == false)
                    subCollectionFunctions[mapCollection] = list = new List<JavaScriptMapOperation>();

                if (map.HasProperty(MethodProperty) == false)
                    ThrowIndexCreationException($"map function #{i} is missing its {MethodProperty} property");
                var funcInstance = map.Get(MethodProperty).As<FunctionInstance>();
                if (funcInstance == null)
                    ThrowIndexCreationException($"map function #{i} {MethodProperty} property isn't a 'FunctionInstance'");
                var operation = new JavaScriptMapOperation(_engine, resolver)
                {
                    MapFunc = funcInstance,
                    IndexName = Definition.Name,
                    MapString = mapList[i]
                };
                if (map.HasOwnProperty(MoreArgsProperty))
                {
                    var moreArgsObj = map.Get(MoreArgsProperty);
                    if (moreArgsObj.IsArray())
                    {
                        var array = moreArgsObj.AsArray();
                        if (array.Length > 0)
                        {
                            operation.MoreArguments = array;
                        }
                    }
                }

                operation.Analyze(_engine);
                if (ReferencedCollections.TryGetValue(mapCollection, out var collectionNames) == false)
                {
                    collectionNames = new HashSet<CollectionName>();
                    ReferencedCollections.Add(mapCollection, collectionNames);
                }

                collectionNames.UnionWith(mapReferencedCollections[i].ReferencedCollections);

                if (mapReferencedCollections[i].HasCompareExchangeReferences)
                    CollectionsWithCompareExchangeReferences.Add(mapCollection);

                list.Add(operation);
            }
        }
    }

    public abstract class AbstractJavaScriptIndex : AbstractStaticIndexBase
    {
        protected const string GlobalDefinitions = "globalDefinition";
        protected const string CollectionProperty = "collection";
        protected const string MethodProperty = "method";
        protected const string MoreArgsProperty = "moreArgs";

        protected const string MapsProperty = "maps";

        private const string ReduceProperty = "reduce";
        private const string AggregateByProperty = "aggregateBy";
        private const string KeyProperty = "key";

        protected AbstractJavaScriptIndex(IndexDefinition definition, RavenConfiguration configuration)
        {
            Definition = definition;

            var indexConfiguration = new SingleIndexConfiguration(definition.Configuration, configuration);

            // we create the Jint instance directly instead of using SingleRun
            // because the index is single threaded and long lived
            var resolver = new JintPreventResolvingTasksReferenceResolver();
            _engine = new Engine(options =>
            {
                options.LimitRecursion(64)
                    .SetReferencesResolver(resolver)
                    .MaxStatements(indexConfiguration.MaxStepsForScript)
                    .Strict(configuration.Patching.StrictMode)
                    .AddObjectConverter(new JintGuidConverter())
                    .AddObjectConverter(new JintStringConverter())
                    .AddObjectConverter(new JintEnumConverter())
                    .AddObjectConverter(new JintDateTimeConverter())
                    .AddObjectConverter(new JintTimeSpanConverter())
                    .LocalTimeZone(TimeZoneInfo.Utc);
            });

            using (_engine.DisableMaxStatements())
            {
                var maps = GetMappingFunctions();

                var mapReferencedCollections = InitializeEngine(definition, maps);

                var definitions = GetDefinitions();

                ProcessMaps(definitions, resolver, maps, mapReferencedCollections, out var collectionFunctions);

                ProcessReduce(definition, definitions, resolver);

                ProcessFields(definition, collectionFunctions);
            }

            _javaScriptUtils = new JavaScriptUtils(null, _engine);
        }

        protected virtual List<string> GetMappingFunctions()
        {
            if (Definition.Maps == null || Definition.Maps.Count == 0)
                ThrowIndexCreationException("TODO ppekrol");

            return Definition.Maps.ToList();
        }

        internal static AbstractJavaScriptIndex Create(IndexDefinition definition, RavenConfiguration configuration)
        {
            switch (definition.SourceType)
            {
                case IndexSourceType.Documents:
                    return new JavaScriptIndex(definition, configuration);
                case IndexSourceType.TimeSeries:
                    return new TimeSeriesJavaScriptIndex(definition, configuration);
                default:
                    throw new NotSupportedException($"Not supported source type '{definition.SourceType}'.");
            }
        }

        private void ProcessFields(IndexDefinition definition, Dictionary<string, Dictionary<string, List<JavaScriptMapOperation>>> collectionFunctions)
        {
            var fields = new HashSet<string>();
            HasDynamicFields = false;
            foreach (var (collection, vals) in collectionFunctions)
            {
                foreach (var (subCollection, val) in vals)
                {
                    //TODO: Validation of matches fields between group by / collections / etc
                    foreach (var operation in val)
                    {
                        AddMapInternal(collection, subCollection, (IndexingFunc)operation.IndexingFunction);

                        HasDynamicFields |= operation.HasDynamicReturns;
                        fields.UnionWith(operation.Fields);
                        foreach (var (k, v) in operation.FieldOptions)
                        {
                            Definition.Fields.Add(k, v);
                        }
                    }
                }
            }

            if (definition.Fields != null)
            {
                foreach (var item in definition.Fields)
                {
                    if (string.Equals(item.Key, Constants.Documents.Indexing.Fields.AllFields))
                        continue;

                    fields.Add(item.Key);
                }
            }

            OutputFields = fields.ToArray();
        }

        private void ProcessReduce(IndexDefinition definition, ObjectInstance definitions, JintPreventResolvingTasksReferenceResolver resolver)
        {
            var reduceObj = definitions.GetProperty(ReduceProperty)?.Value;
            if (reduceObj != null && reduceObj.IsObject())
            {
                var reduceAsObj = reduceObj.AsObject();
                var groupByKey = reduceAsObj.GetProperty(KeyProperty).Value.As<ArrowFunctionInstance>();
                var reduce = reduceAsObj.GetProperty(AggregateByProperty).Value.As<ArrowFunctionInstance>();
                ReduceOperation = new JavaScriptReduceOperation(reduce, groupByKey, _engine, resolver) { ReduceString = definition.Reduce };
                GroupByFields = ReduceOperation.GetReduceFieldsNames();
                Reduce = ReduceOperation.IndexingFunction;
            }
        }

        protected abstract void ProcessMaps(ObjectInstance definitions, JintPreventResolvingTasksReferenceResolver resolver, List<string> mapList, List<MapMetadata> mapReferencedCollections, out Dictionary<string, Dictionary<string, List<JavaScriptMapOperation>>> collectionFunctions);

        private ObjectInstance GetDefinitions()
        {
            var definitionsObj = _engine.GetValue(GlobalDefinitions);

            if (definitionsObj.IsNull() || definitionsObj.IsUndefined() || definitionsObj.IsObject() == false)
                ThrowIndexCreationException($"is missing its '{GlobalDefinitions}' global variable, are you modifying it in your script?");

            var definitions = definitionsObj.AsObject();
            if (definitions.HasProperty(MapsProperty) == false)
                ThrowIndexCreationException("is missing its 'globalDefinition.maps' property, are you modifying it in your script?");

            return definitions;
        }

        private static readonly ParserOptions DefaultParserOptions = new ParserOptions();

        private MapMetadata ExecuteCodeAndCollectReferencedCollections(string code, string additionalSources)
        {
            var javascriptParser = new JavaScriptParser(code, DefaultParserOptions);
            var program = javascriptParser.ParseScript();
            _engine.ExecuteWithReset(program);
            var loadVisitor = new EsprimaReferencedCollectionVisitor();
            if (string.IsNullOrEmpty(additionalSources) == false)
                loadVisitor.Visit(new JavaScriptParser(additionalSources, DefaultParserOptions).ParseScript());

            loadVisitor.Visit(program);
            return new MapMetadata
            {
                ReferencedCollections = loadVisitor.ReferencedCollection,
                HasCompareExchangeReferences = loadVisitor.HasCompareExchangeReferences
            };
        }

        private List<MapMetadata> InitializeEngine(IndexDefinition definition, List<string> maps)
        {
            _engine.SetValue("load", new ClrFunctionInstance(_engine, "load", LoadDocument));
            _engine.SetValue("cmpxchg", new ClrFunctionInstance(_engine, "cmpxchg", LoadCompareExchangeValue));
            _engine.SetValue("getMetadata", new ClrFunctionInstance(_engine, "getMetadata", GetMetadata));
            _engine.SetValue("id", new ClrFunctionInstance(_engine, "id", GetDocumentId));
            _engine.ExecuteWithReset(Code);
            _engine.ExecuteWithReset(MapCode);

            var sb = new StringBuilder();
            if (definition.AdditionalSources != null)
            {
                foreach (var script in definition.AdditionalSources.Values)
                {
                    _engine.ExecuteWithReset(script);

                    sb.Append(Environment.NewLine);
                    sb.AppendLine(script);
                }
            }

            var mapReferencedCollections = new List<MapMetadata>();
            var additionalSources = sb.ToString();
            foreach (var map in maps)
            {
                var result = ExecuteCodeAndCollectReferencedCollections(map, additionalSources);
                mapReferencedCollections.Add(result);
            }

            if (definition.Reduce != null)
            {
                _engine.ExecuteWithReset(definition.Reduce);
            }

            return mapReferencedCollections;
        }

        private JsValue GetDocumentId(JsValue self, JsValue[] args)
        {
            var scope = CurrentIndexingScope.Current;
            scope.RegisterJavaScriptUtils(_javaScriptUtils);

            return _javaScriptUtils.GetDocumentId(self, args);
        }

        private JsValue GetMetadata(JsValue self, JsValue[] args)
        {
            var scope = CurrentIndexingScope.Current;
            scope.RegisterJavaScriptUtils(_javaScriptUtils);

            return _javaScriptUtils.GetMetadata(self, args);
        }

        protected void ThrowIndexCreationException(string message)
        {
            throw new IndexCreationException($"JavaScript index {Definition.Name} {message}");
        }

        private JsValue LoadDocument(JsValue self, JsValue[] args)
        {
            if (args.Length != 2)
            {
                throw new ArgumentException("The load(id, collection) method expects two arguments, but got: " + args.Length);
            }

            if (args[0].IsNull() || args[0].IsUndefined())
                return DynamicJsNull.ImplicitNull;

            if (args[0].IsString() == false ||
                args[1].IsString() == false)
            {
                throw new ArgumentException($"The load(id, collection) method expects two string arguments, but got: load({args[0]}, {args[1]})");
            }

            object doc = CurrentIndexingScope.Current.LoadDocument(null, args[0].AsString(), args[1].AsString());
            if (JavaScriptIndexUtils.GetValue(_engine, doc, out var item))
                return item;

            return DynamicJsNull.ImplicitNull;
        }

        private JsValue LoadCompareExchangeValue(JsValue self, JsValue[] args)
        {
            if (args.Length != 1)
                throw new ArgumentException("The cmpxchg(key) method expects one argument, but got: " + args.Length);

            var keyArgument = args[0];
            if (keyArgument.IsNull() || keyArgument.IsUndefined())
                return DynamicJsNull.ImplicitNull;

            if (keyArgument.IsString())
            {
                object value = CurrentIndexingScope.Current.LoadCompareExchangeValue(null, keyArgument.AsString());
                return ConvertToJsValue(value);
            }
            else if (keyArgument.IsArray())
            {
                var keys = keyArgument.AsArray();
                if (keys.Length == 0)
                    return DynamicJsNull.ImplicitNull;

                var values = _engine.Array.Construct(keys.Length);
                var arrayArgs = new JsValue[1];
                for (uint i = 0; i < keys.Length; i++)
                {
                    var key = keys[i];
                    if (key.IsString() == false)
                        ThrowInvalidType(key, Types.String);

                    object value = CurrentIndexingScope.Current.LoadCompareExchangeValue(null, key.AsString());
                    arrayArgs[0] = ConvertToJsValue(value);

                    _engine.Array.PrototypeObject.Push(values, args);
                }

                return values;
            }
            else
            {
                throw new InvalidOperationException($"Argument '{keyArgument}' was of type '{keyArgument.Type}', but either string or array of strings was expected.");
            }

            JsValue ConvertToJsValue(object value)
            {
                switch (value)
                {
                    case null:
                        return DynamicJsNull.ImplicitNull;
                    case DynamicNullObject dno:
                        return dno.IsExplicitNull ? DynamicJsNull.ExplicitNull : DynamicJsNull.ImplicitNull;
                    case DynamicBlittableJson dbj:
                        return new BlittableObjectInstance(_engine, null, dbj.BlittableJson, id: null, lastModified: null, changeVector: null);
                    default:
                        return _javaScriptUtils.TranslateToJs(_engine, context: null, value);
                }
            }

            static void ThrowInvalidType(JsValue value, Types expectedType)
            {
                throw new InvalidOperationException($"Argument '{value}' was of type '{value.Type}', but '{expectedType}' was expected.");
            }
        }

        protected abstract string MapCode { get; }

        private const string Code = @"
var globalDefinition =
{
    maps: [],
    reduce: null
}

function groupBy(lambda) {
    var reduce = globalDefinition.reduce = { };
    reduce.key = lambda;

    reduce.aggregate = function(reduceFunction){
        reduce.aggregateBy = reduceFunction;
    }
    return reduce;
}

function createSpatialField(wkt) {
    return { $spatial: wkt }
}

function createSpatialField(lat, lng) {
    return { $spatial: {Lng:lng, Lat:lat} }
}
";

        protected readonly IndexDefinition Definition;
        internal readonly Engine _engine;
        private readonly JavaScriptUtils _javaScriptUtils;

        public JavaScriptReduceOperation ReduceOperation { get; private set; }

        public void SetBufferPoolForTestingPurposes(UnmanagedBuffersPoolWithLowMemoryHandling bufferPool)
        {
            ReduceOperation?.SetBufferPoolForTestingPurposes(bufferPool);
        }

        public void SetAllocatorForTestingPurposes(ByteStringContext byteStringContext)
        {
            ReduceOperation?.SetAllocatorForTestingPurposes(byteStringContext);
        }

        protected class MapMetadata
        {
            public HashSet<CollectionName> ReferencedCollections;

            public bool HasCompareExchangeReferences;
        }
    }
}
