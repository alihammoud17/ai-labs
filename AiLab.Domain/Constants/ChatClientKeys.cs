namespace AiLab.Domain.Constants;

// DI keys for chat clients
public static class ChatClientKeys
{
    // The provider client at the bottom of every pipeline. Keyed, so the integration tests can replace just this
    // layer with a scripted fake and still run the real middleware above it.
    public const string Provider = "provider";
}
