using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace Bladehero.Telegram.Platform.Hub;

// How a request arrived, noted before anything in the app's pipeline rewrites it. ASP.NET's forwarded-headers middleware
// takes X-Forwarded-For away and writes X-Original-For only when the connection has a remote address, so by the time the
// pass scheme runs a request from the edge can look like one from inside. The door reads this note as well as the request.
internal sealed record HubPassArrival(bool Forwarded, string Host)
{
    private static readonly string[] ForwardingHeaders = ["X-Forwarded-For", "X-Original-For", "Forwarded"];

    public static bool IsForwarded(IHeaderDictionary headers) => ForwardingHeaders.Any(headers.ContainsKey);

    public static HubPassArrival Of(HttpRequest request) => new(IsForwarded(request.Headers), request.Host.Host);
}

// Outermost of the startup filters (AddHubPasses puts it first), so it runs before UseForwardedHeaders, whether the app
// calls it or ASPNETCORE_FORWARDEDHEADERS_ENABLED adds it.
internal sealed class HubPassArrivalFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
        app =>
        {
            app.Use(
                (context, rest) =>
                {
                    context.Features.Set(HubPassArrival.Of(context.Request));
                    return rest(context);
                }
            );
            next(app);
        };
}
