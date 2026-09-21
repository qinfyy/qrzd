using System.Text;
using Sv.Configuration;
using Sv.Gateway;

namespace Sv.Http;

public static class HttpEndpoints
{
    public static void MapQrzdEndpoints(this WebApplication app)
    {
        app.MapGet("/", (GatewayService gateway) => Results.Json(new { service = "qrzd", phase = "basic-login", gatewayImplemented = true, gatewayListening = gateway.Listening }));
        app.MapGet("/health", (GatewayService gateway) => Results.Json(new { status = gateway.Listening ? "ok" : "degraded", service = "qrzd", gatewayImplemented = true, gatewayListening = gateway.Listening, gatewayConnections = gateway.Connections }));
        app.MapMethods("/server_list_android.txt", [HttpMethods.Get, HttpMethods.Head], () => Results.Text(
            BootstrapResponses.ServerList(Config.Server), "text/plain", Encoding.UTF8));
        app.MapMethods("/announcement_android", [HttpMethods.Get, HttpMethods.Head], Announcement);
        app.MapMethods("/announcement_other", [HttpMethods.Get, HttpMethods.Head], Announcement);
        app.MapAccountEndpoints();
    }

    private static IResult Announcement() => Results.Text(
        BootstrapResponses.Announcement(Config.Server), "text/plain", Encoding.UTF8);
}
