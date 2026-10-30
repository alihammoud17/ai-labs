using System.Text.Json.Serialization;

namespace AiLab.Domain.Enums;

// The distance gate before generation: only Passed calls the model
[JsonConverter(typeof(JsonStringEnumConverter<GateResult>))]
public enum GateResult
{
    [JsonStringEnumMemberName("passed")] Passed,

    // The search returned nothing
    [JsonStringEnumMemberName("no_hits")] NoHits,

    // The closest hit is farther than Ask:MaxDistance
    [JsonStringEnumMemberName("too_far")] TooFar,
}
