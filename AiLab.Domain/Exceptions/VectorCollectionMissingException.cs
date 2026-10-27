namespace AiLab.Domain.Exceptions;

// A search ran against a collection that doesn't exist yet (nothing indexed, or a wrong VectorStore:Collection).
// Its own type so a host can answer it with a clear status instead of a 500. The message is safe to return.
public class VectorCollectionMissingException(string collection)
    : Exception($"Collection '{collection}' does not exist. Index some chunks first, or check VectorStore:Collection.")
{
    public string Collection { get; } = collection;
}
