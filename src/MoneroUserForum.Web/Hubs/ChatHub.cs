using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace MoneroUserForum.Web.Hubs;

/// <summary>
/// Real-time chatroom channel. The server broadcasts a lightweight "chatUpdated"
/// signal (via <see cref="IHubContext{ChatHub}"/>) whenever a message is posted;
/// connected clients then pull the safe, server-rendered feed. No message content
/// travels over the hub, so there's nothing new to sanitise here.
///
/// SignalR auto-negotiates transports (WebSockets → Server-Sent Events → Long
/// Polling), so it degrades gracefully behind Tor / proxies that don't upgrade
/// WebSocket connections. Auth uses the same cookie as the rest of the site.
/// </summary>
[Authorize]
public class ChatHub : Hub
{
}
