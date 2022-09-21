using Raven.Client.Documents.Operations.ETL;
using Sparrow.Json.Parsing;

namespace Raven.Server.Documents.ETL.Stats;

public class EtlTaskState
{
    public string TaskName { get; set; }

    public EtlProcessStateForDebug[] States { get; set; }

    public DynamicJsonValue ToJson()
    {
        return new DynamicJsonValue
        {
            [nameof(TaskName)] = TaskName,
            [nameof(States)] = new DynamicJsonArray(States)
        };
    }
}

public class EtlProcessStateForDebug : IDynamicJson
{
    public string TransformationName { get; set; }

    public EtlProcessState State { get; set; }

    public DynamicJsonValue ToJson()
    {
        return new DynamicJsonValue
        {
            [nameof(TransformationName)] = TransformationName,
            [nameof(State)] = State.ToJson()
        };
    }
}
