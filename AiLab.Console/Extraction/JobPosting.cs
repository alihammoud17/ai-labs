using System.ComponentModel;
using System.Text.Json.Serialization;

namespace AiLab.Console.Extraction;

// The descriptions end up in the JSON schema that GetResponseAsync<T> sends, so they are instructions the model sees.
// Rules match inputs/README.md.
[Description("Details extracted from one job posting. Use only what the posting states; never guess.")]
public record JobPosting
{
    [Description("The job title, without a sub-brand suffix such as \" - Acom\".")]
    public string Title { get; init; } = "";

    [Description("The hiring company named in the post, not a sub-brand that appears in the title. Null if not stated.")]
    public string? Company { get; init; }

    [Description("\"City, Country\" when the post states it. Null if not stated.")]
    public string? Location { get; init; }

    [Description("OnSite, Hybrid or Remote, only if the post says so explicitly. Null when the post doesn't say; never guess.")]
    public WorkMode? WorkMode { get; init; }

    [Description("Technologies from the Requirements section only, not 'Bonus Points' or nice-to-have. CV-keyword level (\"Azure\", not every Azure service), at most about 12.")]
    public List<string> RequiredSkills { get; init; } = [];

    [Description("The minimum years of experience stated, even if softened with 'ideally'. Null if not stated.")]
    public int? YearsOfExperience { get; init; }
}

// The converter on the type makes it a string ("Hybrid") everywhere: in the schema, in the model's reply and in the golden files
[JsonConverter(typeof(JsonStringEnumConverter<WorkMode>))]
public enum WorkMode
{
    OnSite,
    Hybrid,
    Remote,
}
