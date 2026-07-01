using System;
using System.Threading.Tasks;
using Xunit;
using Zenith.Core;

namespace Zenith.Test;

public class MockRecorderEngineTests
{
    [Fact]
    public async Task StartAsync_ChangesStateToRecording()
    {
        // Arrange
        var engine = new MockRecorderEngine();
        
        // Act
        await engine.StartAsync();
        
        // Assert
        Assert.Equal(RecorderState.Recording, engine.State);
    }

    [Fact]
    public async Task PauseAsync_ChangesStateToPaused()
    {
        // Arrange
        var engine = new MockRecorderEngine();
        await engine.StartAsync();
        
        // Act
        await engine.PauseAsync();
        
        // Assert
        Assert.Equal(RecorderState.Paused, engine.State);
    }

    [Fact]
    public async Task ResumeAsync_ChangesStateToRecording()
    {
        // Arrange
        var engine = new MockRecorderEngine();
        await engine.StartAsync();
        await engine.PauseAsync();
        
        // Act
        await engine.ResumeAsync();
        
        // Assert
        Assert.Equal(RecorderState.Recording, engine.State);
    }

    [Fact]
    public async Task StopAsync_ChangesStateToStopped()
    {
        // Arrange
        var engine = new MockRecorderEngine();
        await engine.StartAsync();
        
        // Act
        await engine.StopAsync();
        
        // Assert
        Assert.Equal(RecorderState.Stopped, engine.State);
    }

    [Fact]
    public async Task StartAsync_RaisesStatusChangedEvent()
    {
        // Arrange
        var engine = new MockRecorderEngine();
        bool eventRaised = false;
        engine.StatusChanged += (s, e) => {
            eventRaised = true;
            Assert.Equal(RecorderState.Recording, e.State);
        };
        
        // Act
        await engine.StartAsync();
        
        // Assert
        Assert.True(eventRaised);
    }
}
