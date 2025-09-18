using FluentAssertions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using VisionOps.Service.Monitoring;
using Xunit;

namespace VisionOps.Tests.Phase0;

/// <summary>
/// Critical Phase 0 tests for thermal management validation.
/// </summary>
public class ThermalManagementTests
{
    /// <summary>
    /// Test that thermal manager properly detects high temperatures
    /// </summary>
    [Fact]
    public async Task ThermalManager_ShouldDetectTemperature()
    {
        // Arrange
        var logger = new NullLogger<ThermalManager>();
        var appLifetime = Substitute.For<IHostApplicationLifetime>();
        var thermalManager = new ThermalManager(logger, appLifetime);

        // Act
        var temperature = await thermalManager.GetCpuTemperature();

        // Assert
        temperature.Should().BeGreaterThan(0, "CPU temperature should be detectable");
        temperature.Should().BeLessThan(100, "CPU temperature should be reasonable");
    }

    /// <summary>
    /// Test throttling behavior
    /// </summary>
    [Fact]
    public void ThermalManager_ShouldProvideThrottleStatus()
    {
        // Arrange
        var logger = new NullLogger<ThermalManager>();
        var appLifetime = Substitute.For<IHostApplicationLifetime>();
        var thermalManager = new ThermalManager(logger, appLifetime);

        // Act
        var isThrottled = thermalManager.IsThrottled;
        var delay = thermalManager.GetThrottleDelay();

        // Assert
        isThrottled.Should().BeFalse("should not be throttled initially");
        delay.Should().Be(0, "should have no delay when not throttled");
    }

    /// <summary>
    /// Test system metrics collection
    /// </summary>
    [Fact]
    public async Task ThermalManager_ShouldCollectSystemMetrics()
    {
        // Arrange
        var logger = new NullLogger<ThermalManager>();
        var appLifetime = Substitute.For<IHostApplicationLifetime>();
        var thermalManager = new ThermalManager(logger, appLifetime);

        // Act
        var metrics = await thermalManager.GetSystemMetrics();

        // Assert
        metrics.Should().NotBeNull();
        metrics.CpuTemperature.Should().BeGreaterThan(0);
        metrics.MemoryUsageMB.Should().BeGreaterThan(0);
        metrics.Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }
}