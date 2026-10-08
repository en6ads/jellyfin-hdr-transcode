using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Emby.Server.Implementations.Session;
using MediaBrowser.Controller.Net;
using MediaBrowser.Controller.Session;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Server.Implementations.Tests.SessionManager;

public class WebSocketControllerTests
{
    [Fact]
    public async Task OnConnectionClosed_AfterDispose_DoesNotThrow()
    {
        var sessionManager = new Mock<ISessionManager>();
        await using var session = new SessionInfo(sessionManager.Object, NullLogger.Instance);
        await using var controller = new WebSocketController(NullLogger<WebSocketController>.Instance, session, sessionManager.Object);
        var connection = new Mock<IWebSocketConnection>();
        EventHandler<EventArgs>? closed = null;
        connection
            .SetupAdd(c => c.Closed += It.IsAny<EventHandler<EventArgs>>())
            .Callback<EventHandler<EventArgs>>(handler => closed = handler);
        controller.AddWebSocket(connection.Object);
        Assert.NotNull(closed);
        await controller.DisposeAsync();

        var context = new CapturingSynchronizationContext();
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            closed(connection.Object, EventArgs.Empty);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        Assert.Empty(context.Exceptions);
        sessionManager.Verify(manager => manager.CloseIfNeededAsync(It.IsAny<SessionInfo>()), Times.Never);
    }

    [Fact]
    public async Task OnConnectionClosed_WhenActive_ClosesSessionIfNeeded()
    {
        var sessionManager = new Mock<ISessionManager>();
        sessionManager
            .Setup(manager => manager.CloseIfNeededAsync(It.IsAny<SessionInfo>()))
            .Returns(Task.CompletedTask);
        await using var session = new SessionInfo(sessionManager.Object, NullLogger.Instance);
        await using var controller = new WebSocketController(NullLogger<WebSocketController>.Instance, session, sessionManager.Object);
        var connection = new Mock<IWebSocketConnection>();
        controller.AddWebSocket(connection.Object);

        connection.Raise(c => c.Closed += null, EventArgs.Empty);

        sessionManager.Verify(manager => manager.CloseIfNeededAsync(session), Times.Once);
    }

    private sealed class CapturingSynchronizationContext : SynchronizationContext
    {
        public List<Exception> Exceptions { get; } = new();

        public override void Post(SendOrPostCallback d, object? state)
        {
            try
            {
                d(state);
            }
            catch (Exception ex)
            {
                Exceptions.Add(ex);
            }
        }
    }
}
