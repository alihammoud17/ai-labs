using AiLab.Application.Services;
using AiLab.Domain.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace AiLab.Application;

public static class DependencyInjection
{
    // Singletons, as before: none of them holds per-request state.
    // They need an IChatClient, which each host registers with its own middleware (AddChatClient in Program.cs).
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton<TicketTools>();
        services.AddSingleton<ITicketAnalyzer, TicketAnalyzer>();
        services.AddSingleton<ITicketEval, TicketEval>();
        return services;
    }
}
