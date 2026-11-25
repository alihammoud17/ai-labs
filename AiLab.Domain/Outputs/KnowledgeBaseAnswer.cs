namespace AiLab.Domain.Outputs;

// What a managed knowledge base answered in one call (Bedrock's RetrieveAndGenerate): the text, its citations and the
// session the service opened. No token usage: the service doesn't report it, so a caller must never price this as $0.
public sealed record KnowledgeBaseAnswer(string Text, IReadOnlyList<KnowledgeBaseCitation> Citations, string? SessionId);

// One citation: a span of the answer text (character offsets, as the service gives them; null when it gives none) and the
// passages that support it. On aws-emulator each span is a whole quoted passage.
public sealed record KnowledgeBaseCitation(int? SpanStart, int? SpanEnd, IReadOnlyList<KnowledgeBaseReference> References);

// A cited passage: corpus-relative Source (docs/contracts.md, never the s3:// URI) and its text. No score or rank:
// RetrieveAndGenerate's references carry neither.
public sealed record KnowledgeBaseReference(string Source, string Text);
