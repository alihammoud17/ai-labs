using System.Text.Json.Serialization;

namespace AiLab.Domain.Enums;

// How far an /ask answer can be trusted. Set by the server, never by the model.
// The converter and member names make it "grounded" etc. everywhere (Rag has no global enum converter).
[JsonConverter(typeof(JsonStringEnumConverter<GroundingStatus>))]
public enum GroundingStatus
{
    // Every cited number is one of the sources sent with the question
    [JsonStringEnumMemberName("grounded")] Grounded,

    // No answer from the docs: the distance gate closed, or the model said notInDocs
    [JsonStringEnumMemberName("abstained")] Abstained,

    // An answer with no citations, or citing a number that wasn't sent
    [JsonStringEnumMemberName("unverified")] Unverified,
}
