using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.UseWebSockets();

var connections = new ConcurrentDictionary<string, (WebSocket Socket, string Nickname)>();

app.Map("/ws", async context =>
{
    if (context.WebSockets.IsWebSocketRequest)
    {
        using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
        var connectionId = Guid.NewGuid().ToString(); // Generate unique ID for user

        // Requests user nickname
        await webSocket.SendAsync(
            new ArraySegment<byte>(Encoding.UTF8.GetBytes("Type your Nickname:")),
            WebSocketMessageType.Text,
            true,
            CancellationToken.None);

        var nickname = await ReceiveNickname(webSocket);
        if (string.IsNullOrEmpty(nickname))
        {
            await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Invalid Nickname", CancellationToken.None);
            return;
        }

        connections.TryAdd(connectionId, (webSocket, nickname)); // Add user to 1 connection

        Console.WriteLine($"{nickname} Joined in chat.");

        await HandleWebSocketConnection(webSocket, connectionId);
    }
    else
    {
        context.Response.StatusCode = 400;
        await context.Response.WriteAsync("Only WebSocket Connections are allowed.");
    }
});

await app.RunAsync();

async Task<string> ReceiveNickname(WebSocket webSocket)
{
    var buffer = new byte[1024 * 4];

    var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

    if (result.MessageType == WebSocketMessageType.Text)
    {
        return Encoding.UTF8.GetString(buffer, 0, result.Count).Trim();
    }

    return null;
}

async Task HandleWebSocketConnection(WebSocket webSocket, string connectionId)
{
    var buffer = new byte[1024 * 4];

    while (webSocket.State == WebSocketState.Open)
    {
        var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

        if (result.MessageType == WebSocketMessageType.Text)
        {
            var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
            var (senderSocket, senderNickname) = connections[connectionId];

            foreach (var (socket, nickname) in connections.Values)
            {
                    await socket.SendAsync(
                        new ArraySegment<byte>(Encoding.UTF8.GetBytes($"{(senderNickname == nickname ? "Me" : senderNickname)}: {message}")),
                        WebSocketMessageType.Text,
                        true,
                        CancellationToken.None);
            }
        }
            else if (result.MessageType == WebSocketMessageType.Close)
        {
            connections.TryRemove(connectionId, out var disconnected);
            Console.WriteLine($"{disconnected.Nickname} Left the chat.");
            break;
        }
    }
}
