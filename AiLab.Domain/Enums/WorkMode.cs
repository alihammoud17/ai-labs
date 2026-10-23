using System.Text.Json.Serialization;

namespace AiLab.Domain.Enums;

// The converter on the type makes it a string ("Hybrid") everywhere: in the schema, in the model's reply and in the golden files
[JsonConverter(typeof(JsonStringEnumConverter<WorkMode>))]
public enum WorkMode
{
    OnSite,
    Hybrid,
    Remote,
}
